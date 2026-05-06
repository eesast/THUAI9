#include "logic.h"

#include <chrono>
#include <functional>
#include <memory>
#include <stdexcept>
#include <thread>

#include "Communication.h"
#include "structures.h"
#include "utils.hpp"

#undef GetMessage
#undef SendMessage
#undef PeekMessage

extern const bool asynchronous;

Logic::Logic(int32_t pID, int32_t tID, THUAI9::PlayerType pType, THUAI9::CharacterType cType) :
    playerType(pType),
    playerID(pID),
    teamID(tID),
    side_flag((tID % 2) == 1),
    CharacterType(cType)
{
    currentState = &state[0];
    bufferState = &state[1];
    currentState->gameInfo = std::make_shared<THUAI9::GameInfo>();
    currentState->mapInfo = std::make_shared<THUAI9::GameMap>();
    bufferState->gameInfo = std::make_shared<THUAI9::GameInfo>();
    bufferState->mapInfo = std::make_shared<THUAI9::GameMap>();
}

std::vector<std::shared_ptr<const THUAI9::Character>> Logic::GetCharacters() const
{
    std::lock_guard<std::mutex> lock(mtxState);
    return {currentState->characters.begin(), currentState->characters.end()};
}

std::vector<std::shared_ptr<const THUAI9::Character>> Logic::GetEnemyCharacters() const
{
    std::lock_guard<std::mutex> lock(mtxState);
    return {currentState->enemyCharacters.begin(), currentState->enemyCharacters.end()};
}

std::shared_ptr<const THUAI9::Character> Logic::CharacterGetSelfInfo() const
{
    std::lock_guard<std::mutex> lock(mtxState);
    return currentState->characterSelf;
}

std::shared_ptr<const THUAI9::Team> Logic::TeamGetSelfInfo() const
{
    std::lock_guard<std::mutex> lock(mtxState);
    return currentState->teamSelf;
}

std::vector<std::vector<THUAI9::PlaceType>> Logic::GetFullMap() const
{
    std::lock_guard<std::mutex> lock(mtxState);
    return currentState->gameMap;
}

THUAI9::PlaceType Logic::GetPlaceType(int32_t cellX, int32_t cellY) const
{
    std::lock_guard<std::mutex> lock(mtxState);
    if (currentState->gameMap.empty() || currentState->gameMap.front().empty())
        return THUAI9::PlaceType::NullPlaceType;
    if (cellX < 0 || cellY < 0)
        return THUAI9::PlaceType::NullPlaceType;
    if (static_cast<size_t>(cellX) >= currentState->gameMap.size() || static_cast<size_t>(cellY) >= currentState->gameMap.front().size())
        return THUAI9::PlaceType::NullPlaceType;
    return currentState->gameMap[cellX][cellY];
}

std::optional<THUAI9::Resource> Logic::GetResourceState(int32_t cellX, int32_t cellY) const
{
    std::lock_guard<std::mutex> lock(mtxState);
    auto it = currentState->mapInfo->resources.find({cellX, cellY});
    if (it == currentState->mapInfo->resources.end())
        return std::nullopt;
    return it->second;
}

std::optional<THUAI9::ComputeCenter> Logic::GetComputeCenterState(int32_t cellX, int32_t cellY) const
{
    std::lock_guard<std::mutex> lock(mtxState);
    auto it = currentState->mapInfo->computeCenters.find({cellX, cellY});
    if (it == currentState->mapInfo->computeCenters.end())
        return std::nullopt;
    return it->second;
}

std::optional<THUAI9::Market> Logic::GetMarketState(int32_t cellX, int32_t cellY) const
{
    std::lock_guard<std::mutex> lock(mtxState);
    auto it = currentState->mapInfo->markets.find({cellX, cellY});
    if (it == currentState->mapInfo->markets.end())
        return std::nullopt;
    return it->second;
}

std::optional<THUAI9::Factory> Logic::GetFactoryState(int32_t cellX, int32_t cellY) const
{
    std::lock_guard<std::mutex> lock(mtxState);
    auto it = currentState->mapInfo->factories.find({cellX, cellY});
    if (it == currentState->mapInfo->factories.end())
        return std::nullopt;
    return it->second;
}

int32_t Logic::GetComputingPower() const
{
    std::lock_guard<std::mutex> lock(mtxState);
    if (currentState->teamSelf)
        return static_cast<int32_t>(currentState->teamSelf->computePower);
    if (teamID > 0 && static_cast<size_t>(teamID) <= currentState->gameInfo->teams.size())
        return currentState->gameInfo->teams[teamID - 1].computePower;
    return -1;
}

int32_t Logic::GetMaterial() const
{
    std::lock_guard<std::mutex> lock(mtxState);
    if (currentState->teamSelf)
        return static_cast<int32_t>(currentState->teamSelf->material);
    if (teamID > 0 && static_cast<size_t>(teamID) <= currentState->gameInfo->teams.size())
        return currentState->gameInfo->teams[teamID - 1].material;
    return -1;
}

int32_t Logic::GetScore() const
{
    std::lock_guard<std::mutex> lock(mtxState);
    if (currentState->teamSelf)
        return static_cast<int32_t>(currentState->teamSelf->score);
    if (teamID > 0 && static_cast<size_t>(teamID) <= currentState->gameInfo->teams.size())
        return currentState->gameInfo->teams[teamID - 1].score;
    return -1;
}

std::shared_ptr<const THUAI9::GameInfo> Logic::GetGameInfo() const
{
    std::lock_guard<std::mutex> lock(mtxState);
    return currentState->gameInfo;
}

bool Logic::Send(int32_t toID, std::string message, bool binary)
{
    return pComm->Send(playerID, toID, teamID, std::move(message), binary);
}

bool Logic::HaveMessage()
{
    return !messageQueue.empty();
}

std::pair<int32_t, std::string> Logic::GetMessage()
{
    auto msg = messageQueue.tryPop();
    if (!msg.has_value())
        return {-1, ""};
    return {static_cast<int32_t>(msg->first), std::move(msg->second)};
}

bool Logic::WaitThread()
{
    if (asynchronous)
        Wait();
    return true;
}

int32_t Logic::GetCounter() const
{
    std::lock_guard<std::mutex> lock(mtxState);
    return counterState;
}

bool Logic::EndAllAction()
{
    return pComm->EndAllAction(playerID, teamID);
}

bool Logic::Move(int64_t moveTimeInMilliseconds, double angle)
{
    return pComm->Move(playerID, teamID, moveTimeInMilliseconds, angle);
}

bool Logic::Recover(int64_t recover)
{
    return pComm->Recover(playerID, recover, teamID);
}

bool Logic::Harvest(int64_t playerIDArg, int64_t teamIDArg)
{
    return pComm->Harvest(playerIDArg, teamIDArg);
}

bool Logic::Occupy(int64_t playerIDArg, int64_t teamIDArg)
{
    return pComm->Occupy(playerIDArg, teamIDArg);
}

bool Logic::Load(int64_t playerIDArg, int64_t teamIDArg, THUAI9::GoodsType goodsType, int32_t amount)
{
    return pComm->Load(playerIDArg, teamIDArg, goodsType, amount);
}

bool Logic::Buy(int64_t playerIDArg, int64_t teamIDArg, THUAI9::GoodsType goodsType, int32_t amount)
{
    return pComm->Trade(playerIDArg, teamIDArg, goodsType, amount, true);
}

bool Logic::Sell(int64_t playerIDArg, int64_t teamIDArg, THUAI9::GoodsType goodsType, int32_t amount)
{
    return pComm->Trade(playerIDArg, teamIDArg, goodsType, amount, false);
}

bool Logic::Common_Attack(int64_t teamIDArg, int64_t playerIDArg, int64_t attacked_teamID, int64_t attacked_playerID)
{
    return pComm->Common_Attack(teamIDArg, playerIDArg, attacked_teamID, attacked_playerID);
}

bool Logic::HaveView(int32_t x, int32_t y, int32_t newX, int32_t newY, int32_t viewRange, std::vector<std::vector<THUAI9::PlaceType>>& map) const
{
    return AssistFunction::HaveView(x, y, newX, newY, viewRange, map);
}

bool Logic::BuildCharacter(THUAI9::CharacterType characterType, int32_t playerIDArg)
{
    return pComm->BuildCharacter(teamID, playerIDArg, characterType);
}

bool Logic::ProduceGoods(THUAI9::GoodsType goodsType, int32_t maxProduceNum)
{
    return pComm->ProduceGoods(teamID, goodsType, maxProduceNum);
}

bool Logic::UplevelTech(THUAI9::TechType techType)
{
    return pComm->UplevelTech(teamID, techType);
}

bool Logic::TryConnection()
{
    return pComm->TryConnection(playerID, teamID);
}

void Logic::LoadBufferSelf(const protobuf::MessageToClient& message)
{
    if (playerType == THUAI9::PlayerType::Character)
    {
        for (const auto& item : message.obj_message())
        {
            if (item.message_of_obj_case() != protobuf::MessageOfObj::kCharacterMessage)
                continue;
            const auto& msg = item.character_message();
            if (msg.player_id() == playerID && msg.team_id() == teamID)
            {
                bufferState->characterSelf = Proto2THUAI9::Protobuf2THUAI9Character(msg);
                bufferState->characters.push_back(bufferState->characterSelf);
                break;
            }
        }
        return;
    }

    if (teamID > 0 && teamID <= message.all_message().teams_size())
    {
        auto team = std::make_shared<THUAI9::Team>();
        team->teamID = teamID;
        team->playerID = playerID;
        team->score = message.all_message().teams(teamID - 1).score();
        team->material = message.all_message().teams(teamID - 1).material();
        team->computePower = message.all_message().teams(teamID - 1).compute_power();
        team->factoryHP = message.all_message().teams(teamID - 1).factory_hp();
        bufferState->teamSelf = team;
    }
}

void Logic::LoadBufferCase(const protobuf::MessageOfObj& item)
{
    switch (item.message_of_obj_case())
    {
        case protobuf::MessageOfObj::kCharacterMessage:
            {
                auto character = Proto2THUAI9::Protobuf2THUAI9Character(item.character_message());
                if (item.character_message().team_id() == teamID)
                {
                    if (item.character_message().player_id() == playerID && playerType == THUAI9::PlayerType::Character)
                        bufferState->characterSelf = character;
                    else
                        bufferState->characters.push_back(character);
                }
                else
                {
                    bufferState->enemyCharacters.push_back(character);
                }
                break;
            }
        case protobuf::MessageOfObj::kTeamMessage:
            {
                if (item.team_message().team_id() == teamID)
                    bufferState->teamSelf = Proto2THUAI9::Protobuf2THUAI9Team(item.team_message());
                break;
            }
        case protobuf::MessageOfObj::kFactoryMessage:
            {
                auto factory = Proto2THUAI9::Protobuf2THUAI9Factory(item.factory_message());
                auto pos = THUAI9::cellxy_t(
                    AssistFunction::GridToCell(item.factory_message().x()),
                    AssistFunction::GridToCell(item.factory_message().y()));
                bufferState->mapInfo->factories[pos] = *factory;
                break;
            }
        case protobuf::MessageOfObj::kResourceMessage:
            {
                auto resource = Proto2THUAI9::Protobuf2THUAI9EconomyResource(item.resource_message());
                auto pos = THUAI9::cellxy_t(
                    AssistFunction::GridToCell(item.resource_message().x()),
                    AssistFunction::GridToCell(item.resource_message().y()));
                bufferState->mapInfo->resources[pos] = *resource;
                break;
            }
        case protobuf::MessageOfObj::kMarketMessage:
            {
                auto market = Proto2THUAI9::Protobuf2THUAI9Market(item.market_message());
                auto pos = THUAI9::cellxy_t(
                    AssistFunction::GridToCell(item.market_message().x()),
                    AssistFunction::GridToCell(item.market_message().y()));
                bufferState->mapInfo->markets[pos] = *market;
                break;
            }
        case protobuf::MessageOfObj::kComputeCenterMessage:
            {
                auto center = Proto2THUAI9::Protobuf2THUAI9ComputeCenter(item.compute_center_message());
                auto pos = THUAI9::cellxy_t(
                    AssistFunction::GridToCell(item.compute_center_message().x()),
                    AssistFunction::GridToCell(item.compute_center_message().y()));
                bufferState->mapInfo->computeCenters[pos] = *center;
                break;
            }
        case protobuf::MessageOfObj::kNewsMessage:
            {
                const auto& news = item.news_message();
                if (news.to_id() == playerID && news.team_id() == teamID)
                {
                    auto newsType = Proto2THUAI9::newsTypeDict[news.news_case()];
                    if (newsType == THUAI9::NewsType::Text)
                        messageQueue.emplace(std::pair<int64_t, std::string>(news.from_id(), news.text_message()));
                    else if (newsType == THUAI9::NewsType::Binary)
                        messageQueue.emplace(std::pair<int64_t, std::string>(news.from_id(), news.binary_message()));
                }
                break;
            }
        default:
            break;
    }
}

void Logic::LoadBuffer(const protobuf::MessageToClient& message)
{
    std::lock_guard<std::mutex> lock(mtxBuffer);

    bufferState->characters.clear();
    bufferState->enemyCharacters.clear();
    bufferState->guids.clear();
    bufferState->allGuids.clear();
    bufferState->characterSelf.reset();
    bufferState->teamSelf.reset();
    bufferState->mapInfo = std::make_shared<THUAI9::GameMap>();
    bufferState->gameInfo = Proto2THUAI9::Protobuf2THUAI9GameInfo(message.all_message());

    LoadBufferSelf(message);

    for (const auto& obj : message.obj_message())
    {
        if (obj.message_of_obj_case() == protobuf::MessageOfObj::kCharacterMessage)
        {
            bufferState->allGuids.push_back(obj.character_message().guid());
            if (obj.character_message().team_id() == teamID)
                bufferState->guids.push_back(obj.character_message().guid());
        }
        LoadBufferCase(obj);
    }

    if (asynchronous)
    {
        {
            std::lock_guard<std::mutex> stateLock(mtxState);
            std::swap(currentState, bufferState);
            counterState = counterBuffer;
        }
        freshed = true;
    }
    else
    {
        bufferUpdated = true;
    }

    ++counterBuffer;
    cvBuffer.notify_one();
}

void Logic::ProcessMessage()
{
    auto messageThread = [this]()
    {
        try
        {
            pComm->AddPlayer(playerID, teamID, CharacterType, side_flag);
            while (gameState != THUAI9::GameState::GameEnd)
            {
                auto clientMsg = pComm->GetMessage2Client();
                gameState = Proto2THUAI9::gameStateDict[clientMsg.game_state()];

                if (gameState == THUAI9::GameState::GameStart)
                {
                    for (const auto& item : clientMsg.obj_message())
                    {
                        if (item.message_of_obj_case() != protobuf::MessageOfObj::kMapMessage)
                            continue;

                        std::vector<std::vector<THUAI9::PlaceType>> map;
                        for (int32_t i = 0; i < item.map_message().rows_size(); ++i)
                        {
                            std::vector<THUAI9::PlaceType> row;
                            for (int32_t j = 0; j < item.map_message().rows(i).cols_size(); ++j)
                                row.push_back(Proto2THUAI9::placeTypeDict[item.map_message().rows(i).cols(j)]);
                            map.push_back(std::move(row));
                        }
                        {
                            std::lock_guard<std::mutex> stateLock(mtxState);
                            currentState->gameMap = map;
                            bufferState->gameMap = std::move(map);
                        }
                        break;
                    }
                    LoadBuffer(clientMsg);
                    AILoop = true;
                    UnBlockAI();
                }
                else if (gameState == THUAI9::GameState::GameRunning)
                {
                    LoadBuffer(clientMsg);
                }
            }

            {
                std::lock_guard<std::mutex> lockBuffer(mtxBuffer);
                bufferUpdated = true;
                counterBuffer = -1;
            }
            cvBuffer.notify_one();
            AILoop = false;
        }
        catch (...)
        {
            AILoop = false;
        }
    };
    std::thread(messageThread).detach();
}

void Logic::Update() noexcept
{
    if (asynchronous)
        return;

    std::unique_lock<std::mutex> lock(mtxBuffer);
    cvBuffer.wait(lock, [this]()
                  { return bufferUpdated; });
    {
        std::lock_guard<std::mutex> stateLock(mtxState);
        std::swap(currentState, bufferState);
        counterState = counterBuffer;
    }
    bufferUpdated = false;
}

void Logic::Wait() noexcept
{
    freshed = false;
    std::unique_lock<std::mutex> lock(mtxBuffer);
    cvBuffer.wait(lock, [this]()
                  { return freshed.load(); });
}

void Logic::UnBlockAI()
{
    {
        std::lock_guard<std::mutex> lock(mtxAI);
        AIStart = true;
    }
    cvAI.notify_one();
}

std::vector<int64_t> Logic::GetPlayerGUIDs() const
{
    std::lock_guard<std::mutex> lock(mtxState);
    return currentState->guids;
}

void Logic::Main(CreateAIFunc createAI, std::string IP, std::string port, bool file, bool print, bool warnOnly)
{
    auto fileLogger = std::make_shared<spdlog::sinks::basic_file_sink_mt>(fmt::format("logs/logic-{}-{}-log.txt", playerID, teamID), true);
    auto printLogger = std::make_shared<spdlog::sinks::stdout_color_sink_mt>();
    const std::string pattern = "[logic] [%H:%M:%S.%e] [%l] %v";
    fileLogger->set_pattern(pattern);
    printLogger->set_pattern(pattern);
    fileLogger->set_level(file ? spdlog::level::debug : spdlog::level::off);
    printLogger->set_level(print ? spdlog::level::info : spdlog::level::off);
    if (warnOnly)
        printLogger->set_level(spdlog::level::warn);
    logger = std::make_unique<spdlog::logger>("logicLogger", spdlog::sinks_init_list{fileLogger, printLogger});
    logger->flush_on(spdlog::level::warn);

    pComm = std::make_unique<Communication>(IP, port);

    if (playerType == THUAI9::PlayerType::Character)
    {
        if (!file && !print)
            timer = std::make_unique<CharacterAPI>(*this);
        else
            timer = std::make_unique<CharacterDebugAPI>(*this, file, print, warnOnly, playerID, teamID);
    }
    else
    {
        if (!file && !print)
            timer = std::make_unique<TeamAPI>(*this);
        else
            timer = std::make_unique<TeamDebugAPI>(*this, file, print, warnOnly, playerID, teamID);
    }

    auto aiThread = [&]()
    {
        try
        {
            {
                std::unique_lock<std::mutex> lock(mtxAI);
                cvAI.wait(lock, [this]()
                          { return AIStart; });
            }

            auto ai = createAI(playerID);
            while (AILoop)
            {
                if (asynchronous)
                    Wait();
                else
                    Update();

                timer->StartTimer();
                timer->Play(*ai);
                timer->EndTimer();
            }
        }
        catch (...)
        {
        }
    };

    int retryCount = 0;
    while (!TryConnection())
    {
        ++retryCount;
        logger->warn("Failed to connect to server {}:{} (attempt {}). Retrying in 1 second...", IP, port, retryCount);
        std::this_thread::sleep_for(std::chrono::seconds(1));
    }
    if (retryCount > 0)
        logger->info("Connected to server {}:{} after {} retries.", IP, port, retryCount);

    tAI = std::thread(aiThread);
    if (tAI.joinable())
    {
        ProcessMessage();
        tAI.join();
    }
}
