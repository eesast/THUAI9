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

    DrainMessages(api, who);

    if (!printedSnapshot)
    {
        PrintCommonSnapshot(api, who);
        api.PrintSelfInfo();
        api.PrintCharacter();
        PrintCellSnapshot(api, *selfinfo);
        printedSnapshot = true;
    }

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
    static std::size_t nextGoodsIndex = 0;
    static std::size_t nextTechIndex = 0;
    static bool sentMessages = false;

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

    for (int32_t i = 1; i <= 3; ++i)
    {
        if (teamCharacterBuilt[i - 1])
            continue;

        api.Print("test BuildCharacter for player " + std::to_string(i));
        const bool ok = api.BuildCharacter(CharacterTypeDict[i - 1], i).get();
        api.Print("BuildCharacter -> " + BoolText(ok));
        if (ok)
            teamCharacterBuilt[i - 1] = true;
        break;
    }

    const bool allCharactersBuilt = teamCharacterBuilt[0] && teamCharacterBuilt[1] && teamCharacterBuilt[2];

    if (allCharactersBuilt && !sentMessages)
    {
        for (int32_t toID = 1; toID <= 3; ++toID)
        {
            api.Print("test team SendTextMessage to player " + std::to_string(toID));
            api.Print("SendTextMessage -> " + BoolText(api.SendTextMessage(toID, "hello from team " + std::to_string(team->teamID)).get()));
            api.Print("test team SendBinaryMessage to player " + std::to_string(toID));
            api.Print("SendBinaryMessage -> " + BoolText(api.SendBinaryMessage(toID, "bin-team-" + std::to_string(team->teamID)).get()));
        }
        sentMessages = true;
    }

    if (nextGoodsIndex < goodsToTest.size() && api.GetFrameCount() % 15 == 0)
    {
        api.Print("test ProduceGoods");
        const bool ok = api.ProduceGoods(goodsToTest[nextGoodsIndex], goodsAmount).get();
        api.Print("ProduceGoods -> " + BoolText(ok));
        if (ok)
            ++nextGoodsIndex;
    }

    if (nextTechIndex < techToTest.size() && api.GetFrameCount() % 20 == 0)
    {
        api.Print("test UplevelTech");
        const bool ok = api.UplevelTech(techToTest[nextTechIndex]).get();
        api.Print("UplevelTech -> " + BoolText(ok));
        if (ok)
            ++nextTechIndex;
    }

    if (api.GetFrameCount() % 30 == 0)
        PrintCommonSnapshot(api, who);
}
