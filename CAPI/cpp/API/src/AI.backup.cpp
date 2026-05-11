#include <array>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

#include "AI.h"
#include "constants.h"

extern const bool asynchronous = false;

extern const std::array<THUAI9::CharacterType, 3> CharacterTypeDict = {
    THUAI9::CharacterType::Robot,
    THUAI9::CharacterType::Drone,
    THUAI9::CharacterType::AutonomousCar,
};

namespace
{
    constexpr int64_t moveTimeMs = 150;
    constexpr int64_t recoverHp = 1;
    constexpr int32_t goodsAmount = 1;
    constexpr double moveAngle = 0.7853981633974483;

    constexpr std::array<THUAI9::GoodsType, 5> goodsToTest = {
        THUAI9::GoodsType::Food,
        THUAI9::GoodsType::Medicine,
        THUAI9::GoodsType::Clothes,
        THUAI9::GoodsType::Toys,
        THUAI9::GoodsType::Semiconductor,
    };

    constexpr std::array<THUAI9::TechType, 4> techToTest = {
        THUAI9::TechType::IncreaseMoveSpeed,
        THUAI9::TechType::IncreaseCarryCapacity,
        THUAI9::TechType::IncreaseEfficiency,
        THUAI9::TechType::DecreaseCost,
    };

    [[nodiscard]] std::string BoolText(bool value)
    {
        return value ? "true" : "false";
    }

    template<class TAPI>
    void DrainMessages(TAPI& api, const std::string& who)
    {
        while (api.HaveMessage())
        {
            auto [fromID, message] = api.GetMessage();
            api.Print(who + " recv from " + std::to_string(fromID) + ": " + message);
        }
    }

    template<class TAPI>
    void DrainMessagesAndReply(TAPI& api, const std::string& who)
    {
        while (api.HaveMessage())
        {
            auto [fromID, message] = api.GetMessage();
            api.Print(who + " recv from " + std::to_string(fromID) + ": " + message);
            if (message.rfind("ping", 0) == 0)
            {
                api.Print(who + " reply to " + std::to_string(fromID));
                api.Print("SendTextMessage -> " + BoolText(api.SendTextMessage(fromID, "pong from " + who).get()));
                api.Print("SendBinaryMessage -> " + BoolText(api.SendBinaryMessage(fromID, "pong-bin from " + who).get()));
            }
        }
    }

    template<class TAPI>
    void PrintCommonSnapshot(TAPI& api, const std::string& who)
    {
        auto map = api.GetFullMap();
        auto characters = api.GetCharacters();
        auto enemies = api.GetEnemyCharacters();
        auto guids = api.GetPlayerGUIDs();
        auto gameInfo = api.GetGameInfo();

        std::string summary =
            who +
            " frame=" + std::to_string(api.GetFrameCount()) +
            " map=" + std::to_string(map.size()) + "x" + std::to_string(map.empty() ? 0 : map.front().size()) +
            " allyChars=" + std::to_string(characters.size()) +
            " enemyChars=" + std::to_string(enemies.size()) +
            " guids=" + std::to_string(guids.size()) +
            " material=" + std::to_string(api.GetMaterial()) +
            " compute=" + std::to_string(api.GetComputingPower()) +
            " score=" + std::to_string(api.GetScore());

        if (gameInfo)
        {
            summary +=
                " gameTime=" + std::to_string(gameInfo->gameTime) +
                " teamCount=" + std::to_string(gameInfo->teams.size());
        }

        api.Print(summary);
    }

    void PrintCellSnapshot(ICharacterAPI& api, const THUAI9::Character& self)
    {
        auto map = api.GetFullMap();
        if (map.empty() || map.front().empty())
            return;

        const int32_t cellX = IAPI::GridToCell(self.x);
        const int32_t cellY = IAPI::GridToCell(self.y);
        api.Print("self cell=(" + std::to_string(cellX) + ", " + std::to_string(cellY) + "), place=" + std::to_string(static_cast<int>(api.GetPlaceType(cellX, cellY))));

        api.Print("resource here: " + BoolText(api.GetResourceState(cellX, cellY).has_value()));
        api.Print("compute center here: " + BoolText(api.GetComputeCenterState(cellX, cellY).has_value()));
        api.Print("market here: " + BoolText(api.GetMarketState(cellX, cellY).has_value()));
        api.Print("factory here: " + BoolText(api.GetFactoryState(cellX, cellY).has_value()));

        const int32_t targetCellX = cellX + 1 < static_cast<int32_t>(map.size()) ? cellX + 1 : cellX;
        const int32_t targetCellY = cellY + 1 < static_cast<int32_t>(map.front().size()) ? cellY + 1 : cellY;
        const int32_t targetGridX = IAPI::CellToGrid(targetCellX);
        const int32_t targetGridY = IAPI::CellToGrid(targetCellY);
        const bool haveView = api.HaveView(self.x, self.y, targetGridX, targetGridY, self.viewRange, map);
        api.Print("HaveView to nearby cell: " + BoolText(haveView));
    }

    template<class TAPI>
    void PrintTeamMapSnapshot(TAPI& api, const std::string& who)
    {
        auto map = api.GetFullMap();
        if (map.empty() || map.front().empty())
            return;

        const int32_t rowCount = static_cast<int32_t>(map.size());
        const int32_t colCount = static_cast<int32_t>(map.front().size());
        const auto printCell = [&](int32_t cellX, int32_t cellY)
        {
            api.Print(
                who + " cell=(" + std::to_string(cellX) + ", " + std::to_string(cellY) + ")" +
                " place=" + std::to_string(static_cast<int>(api.GetPlaceType(cellX, cellY))) +
                " res=" + BoolText(api.GetResourceState(cellX, cellY).has_value()) +
                " center=" + BoolText(api.GetComputeCenterState(cellX, cellY).has_value()) +
                " market=" + BoolText(api.GetMarketState(cellX, cellY).has_value()) +
                " factory=" + BoolText(api.GetFactoryState(cellX, cellY).has_value())
            );
        };

        printCell(0, 0);
        printCell(rowCount / 2, colCount / 2);
        printCell(rowCount - 1, colCount - 1);

        auto findAndPrint = [&](const std::string& label, auto getter)
        {
            for (int32_t x = 0; x < rowCount; ++x)
            {
                for (int32_t y = 0; y < colCount; ++y)
                {
                    if (getter(x, y).has_value())
                    {
                        api.Print(label + " at (" + std::to_string(x) + ", " + std::to_string(y) + ")");
                        printCell(x, y);
                        return;
                    }
                }
            }
            api.Print(label + ": not found");
        };

        findAndPrint("resource", [&](int32_t x, int32_t y)
                     { return api.GetResourceState(x, y); });
        findAndPrint("compute center", [&](int32_t x, int32_t y)
                     { return api.GetComputeCenterState(x, y); });
        findAndPrint("market", [&](int32_t x, int32_t y)
                     { return api.GetMarketState(x, y); });
        findAndPrint("factory", [&](int32_t x, int32_t y)
                     { return api.GetFactoryState(x, y); });
    }

    [[nodiscard]] int64_t FirstEnemyPlayerID(const std::vector<std::shared_ptr<const THUAI9::Character>>& enemies)
    {
        for (const auto& enemy : enemies)
        {
            if (enemy)
                return enemy->playerID;
        }
        return -1;
    }

    void Patrol(ICharacterAPI& api, int32_t playerID)
    {
        switch ((api.GetFrameCount() + playerID) % 4)
        {
            case 0:
                (void)api.MoveRight(moveTimeMs).get();
                break;
            case 1:
                (void)api.MoveDown(moveTimeMs).get();
                break;
            case 2:
                (void)api.MoveLeft(moveTimeMs).get();
                break;
            default:
                (void)api.MoveUp(moveTimeMs).get();
                break;
        }
    }
}  // namespace

std::shared_ptr<const THUAI9::Character> selfinfo;
std::vector<std::vector<THUAI9::PlaceType>> mapinfo;

void AI::play(ICharacterAPI& api)
{
    selfinfo = api.GetSelfInfo();
    mapinfo = api.GetFullMap();
    if (!selfinfo)
        return;

    static bool printedSnapshot = false;
    static int32_t testStep = 0;

    const std::string who = "character " + std::to_string(playerID);

    DrainMessagesAndReply(api, who);

    if (!printedSnapshot)
    {
        PrintCommonSnapshot(api, who);
        api.PrintSelfInfo();
        api.PrintCharacter();
        PrintCellSnapshot(api, *selfinfo);
        printedSnapshot = true;
    }
    api.Print("1231432152354124");
    switch (testStep)
    {
        case 0:
            api.Print("test SendTextMessage");
            api.Print("SendTextMessage -> " + BoolText(api.SendTextMessage(0, "hello from character " + std::to_string(playerID)).get()));
            ++testStep;
            break;
        case 1:
            api.Print("test SendBinaryMessage");
            api.Print("SendBinaryMessage -> " + BoolText(api.SendBinaryMessage(0, "bin-char-" + std::to_string(playerID)).get()));
            ++testStep;
            break;
        case 2:
            api.Print("test Move(angle)");
            api.Print("Move(angle) -> " + BoolText(api.Move(moveTimeMs, moveAngle).get()));
            ++testStep;
            break;
        case 3:
            api.Print("test MoveRight");
            api.Print("MoveRight -> " + BoolText(api.MoveRight(moveTimeMs).get()));
            ++testStep;
            break;
        case 4:
            api.Print("test MoveDown");
            api.Print("MoveDown -> " + BoolText(api.MoveDown(moveTimeMs).get()));
            ++testStep;
            break;
        case 5:
            api.Print("test MoveLeft");
            api.Print("MoveLeft -> " + BoolText(api.MoveLeft(moveTimeMs).get()));
            ++testStep;
            break;
        case 6:
            api.Print("test MoveUp");
            api.Print("MoveUp -> " + BoolText(api.MoveUp(moveTimeMs).get()));
            ++testStep;
            break;
        case 7:
            api.Print("test Recover");
            api.Print("Recover -> " + BoolText(api.Recover(recoverHp).get()));
            ++testStep;
            break;
        case 8:
            api.Print("test Harvest");
            api.Print("Harvest -> " + BoolText(api.Harvest().get()));
            ++testStep;
            break;
        case 9:
            api.Print("test Occupy");
            api.Print("Occupy -> " + BoolText(api.Occupy().get()));
            ++testStep;
            break;
        case 10:
            api.Print("test Load");
            api.Print("Load -> " + BoolText(api.Load(THUAI9::GoodsType::Food, goodsAmount).get()));
            ++testStep;
            break;
        case 11:
            api.Print("test Buy");
            api.Print("Buy -> " + BoolText(api.Buy(THUAI9::GoodsType::Food, goodsAmount).get()));
            ++testStep;
            break;
        case 12:
            api.Print("test Sell");
            api.Print("Sell -> " + BoolText(api.Sell(THUAI9::GoodsType::Food, goodsAmount).get()));
            ++testStep;
            break;
        case 13:
            {
                api.Print("test Common_Attack");
                const int64_t target = FirstEnemyPlayerID(api.GetEnemyCharacters());
                if (target < 0)
                {
                    api.Print("Common_Attack skipped: no visible enemy");
                }
                else
                {
                    api.Print("Common_Attack -> " + BoolText(api.Common_Attack(target).get()));
                }
                ++testStep;
                break;
            }
        case 14:
            api.Print("test EndAllAction");
            api.Print("EndAllAction -> " + BoolText(api.EndAllAction().get()));
            ++testStep;
            break;
        default:
            Patrol(api, playerID);
            if (api.GetFrameCount() % 20 == 0)
                PrintCellSnapshot(api, *selfinfo);
            break;
    }
}

void AI::play(ITeamAPI& api)
{
    static bool printedSnapshot = false;
    static std::array<bool, 3> teamCharacterBuilt{};
    static std::size_t teamStep = 0;
    static bool sentMessages = false;
    static bool printedTeamMap = false;

    auto team = api.GetSelfInfo();
    if (!team)
        return;

    const std::string who = "team " + std::to_string(team->teamID);

    DrainMessages(api, who);

    if (!printedSnapshot)
    {
        PrintCommonSnapshot(api, who);
        api.PrintSelfInfo();
        printedSnapshot = true;
    }

    switch (teamStep)
    {
        case 0:
        case 1:
        case 2:
            {
                const auto index = teamStep;
                const int32_t player = static_cast<int32_t>(index + 1);
                api.Print("test BuildCharacter for player " + std::to_string(player));
                const bool ok = api.BuildCharacter(CharacterTypeDict[index], player).get();
                api.Print("BuildCharacter -> " + BoolText(ok));
                teamCharacterBuilt[index] = ok;
                ++teamStep;
                break;
            }
        case 3:
            if (!sentMessages && (teamCharacterBuilt[0] || teamCharacterBuilt[1] || teamCharacterBuilt[2]))
            {
                for (int32_t toID = 1; toID <= 3; ++toID)
                {
                    if (!teamCharacterBuilt[toID - 1])
                        continue;

                    api.Print("test team SendTextMessage to player " + std::to_string(toID));
                    api.Print("SendTextMessage -> " + BoolText(api.SendTextMessage(toID, "ping from team " + std::to_string(team->teamID)).get()));
                    api.Print("test team SendBinaryMessage to player " + std::to_string(toID));
                    api.Print("SendBinaryMessage -> " + BoolText(api.SendBinaryMessage(toID, "bin-team-" + std::to_string(team->teamID)).get()));
                }
                sentMessages = true;
            }
            ++teamStep;
            break;
        case 4:
            if (!printedTeamMap)
            {
                PrintTeamMapSnapshot(api, who);
                printedTeamMap = true;
            }
            ++teamStep;
            break;
        case 5:
        case 6:
        case 7:
        case 8:
        case 9:
            {
                const auto index = teamStep - 5;
                api.Print("test ProduceGoods");
                const bool ok = api.ProduceGoods(goodsToTest[index], goodsAmount).get();
                api.Print("ProduceGoods -> " + BoolText(ok));
                ++teamStep;
                break;
            }
        case 10:
        case 11:
        case 12:
        case 13:
            {
                const auto index = teamStep - 10;
                api.Print("test UplevelTech");
                const bool ok = api.UplevelTech(techToTest[index]).get();
                api.Print("UplevelTech -> " + BoolText(ok));
                ++teamStep;
                break;
            }
        case 14:
            api.Print("test EndAllAction");
            api.Print("EndAllAction -> " + BoolText(api.EndAllAction().get()));
            ++teamStep;
            break;
        default:
            break;
    }

    if (api.GetFrameCount() % 30 == 0)
        PrintCommonSnapshot(api, who);
}
