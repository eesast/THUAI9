#include "logic.h"
#include "structures.h"
#include <grpcpp/grpcpp.h>
#include <spdlog/spdlog.h>
#include <spdlog/sinks/basic_file_sink.h>
#include <spdlog/sinks/stdout_color_sinks.h>
#include <functional>
#include "utils.hpp"
#include "Communication.h"
#include <memory>
#undef GetMessage
#undef SendMessage
#undef PeekMessage

extern const bool asynchronous;

Logic::Logic(int32_t pID, int32_t tID, THUAI9::PlayerType pType, THUAI9::CharacterType cType) :
    playerID(pID),
    teamID(tID),
    playerType(pType),
    CharacterType(cType),
    side_flag(side_flag)
{
    currentState = &state[0];
    bufferState = &state[1];
    currentState->gameInfo = std::make_shared<THUAI9::GameInfo>();
    currentState->mapInfo = std::make_shared<THUAI9::GameMap>();
    bufferState->gameInfo = std::make_shared<THUAI9::GameInfo>();
    bufferState->mapInfo = std::make_shared<THUAI9::GameMap>();
    playerTeam = THUAI9::PlayerTeam::NullTeam;
}

std::vector<std::shared_ptr<const THUAI9::Character>> Logic::GetCharacters() const
{
    std::unique_lock<std::mutex> lock(mtxState);
    std::vector<std::shared_ptr<const THUAI9::Character>> temp(currentState->characters.begin(), currentState->characters.end());
    logger->debug("Called GetCharacters");
    return temp;
}

std::vector<std::shared_ptr<const THUAI9::Character>> Logic::GetEnemyCharacters() const
{
    std::unique_lock<std::mutex> lock(mtxState);
    std::vector<std::shared_ptr<const THUAI9::Character>> temp(currentState->enemyCharacters.begin(), currentState->enemyCharacters.end());
    logger->debug("Called GetEnemyCharacters");
    return temp;
}

std::shared_ptr<const THUAI9::Character> Logic::CharacterGetSelfInfo() const
{
    std::unique_lock<std::mutex> lock(mtxState);
    logger->debug("Called GetSelfInfo");
    return currentState->characterSelf;
}

std::shared_ptr<const THUAI9::Team> Logic::TeamGetSelfInfo() const
{
    std::unique_lock<std::mutex> lock(mtxState);
    logger->debug("Called TeamGetSelfInfo");
    return this->currentState->teamSelf;
}

std::vector<std::vector<THUAI9::PlaceType>> Logic::GetFullMap() const
{
    std::unique_lock<std::mutex> lock(mtxState);
    logger->debug("Called GetFullMap");
    return currentState->gameMap;
}

THUAI9::PlaceType Logic::GetPlaceType(int32_t cellX, int32_t cellY) const
{
    std::unique_lock<std::mutex> lock(mtxState);
    if (cellX < 0 || uint64_t(cellX) >= currentState->gameMap.size() || cellY < 0 || uint64_t(cellY) >= currentState->gameMap[0].size())
    {
        logger->warn("Invalid position!");
        return THUAI9::PlaceType::NullPlaceType;
    }
    logger->debug("Called GetPlaceType");
    return currentState->gameMap[cellX][cellY];
}

std::optional<THUAI9::EconomyResource> Logic::GetEconomyResourceState(int32_t cellX, int32_t cellY) const
{
    std::unique_lock<std::mutex> lock(mtxState);
    logger->debug("Called GetEconomyResourceState");

    auto pos = THUAI9::cellxy_t(cellX, cellY);
    auto it = currentState->mapInfo->economyResource.find(pos);

    if (it != currentState->mapInfo->economyResource.end())
    {
        return std::make_optional<THUAI9::EconomyResource>(
            it->second.team_id,
            it->second.process,
            it->second.economyResourceType
        );
    }
    else
    {
        logger->warn("EconomyResource not found at ({}, {})", cellX, cellY);
        // 返回一个默认值
        return std::make_optional<THUAI9::EconomyResource>(
            0,                                                    // 默认 ID
            0,                                                    // 默认进度
            THUAI9::EconomyResourceType::NullEconomyResourceType  // 默认类型
        );
    }
}

std::optional<THUAI9::AdditionResource> Logic::GetAdditionResourceState(int32_t cellX, int32_t cellY) const
{
    std::unique_lock<std::mutex> lock(mtxState);
    logger->debug("Called GetAdditionResourceState");
    auto pos = THUAI9::cellxy_t(cellX, cellY);
    auto it = currentState->mapInfo->additionResource.find(pos);
    if (it != currentState->mapInfo->additionResource.end())
    {
        return std::make_optional<THUAI9::AdditionResource>(currentState->mapInfo->additionResource[pos].team_id, currentState->mapInfo->additionResource[pos].hp, currentState->mapInfo->additionResource[pos].additionResourceType);
    }

    else
    {
        logger->warn("AdditionResource not found at ({}, {})", cellX, cellY);
        return std::make_optional<THUAI9::AdditionResource>(
            0,                                                      // 默认 ID
            0,                                                      // 默认进度
            THUAI9::AdditionResourceType::NullAdditionResourceType  // 默认类型
        );
    }
}

std::optional<THUAI9::ConstructionState> Logic::GetConstructionState(int32_t cellX, int32_t cellY) const
{
    std::unique_lock<std::mutex> lock(mtxState);
    logger->debug("Called GetConstructionState");
    auto pos = THUAI9::cellxy_t(cellX, cellY);
    auto it = currentState->mapInfo->barracksState.find(pos);
    auto it2 = currentState->mapInfo->springState.find(pos);
    auto it3 = currentState->mapInfo->farmState.find(pos);
    if (it != currentState->mapInfo->barracksState.end())
    {
        return std::make_optional<THUAI9::ConstructionState>(currentState->mapInfo->barracksState[pos].first, currentState->mapInfo->barracksState[pos].second, THUAI9::ConstructionType::Barracks);
    }
    else if (it2 != currentState->mapInfo->springState.end())
        return std::make_optional<THUAI9::ConstructionState>(currentState->mapInfo->springState[pos].first, currentState->mapInfo->springState[pos].second, THUAI9::ConstructionType::Spring);
    else if (it3 != currentState->mapInfo->farmState.end())
        return std::make_optional<THUAI9::ConstructionState>(currentState->mapInfo->farmState[pos].first, currentState->mapInfo->farmState[pos].second, THUAI9::ConstructionType::Farm);
    else
    {
        logger->warn("Construction not found at ({}, {})", cellX, cellY);
        return std::make_optional<THUAI9::ConstructionState>(
            0,                                              // 默认 ID
            0,                                              // 默认进度
            THUAI9::ConstructionType::NullConstructionType  // 默认类型
        );
    }
}

std::optional<THUAI9::Trap> Logic::GetTrapState(int32_t cellX, int32_t cellY) const
{
    std::unique_lock<std::mutex> lock(mtxState);
    logger->debug("Called GetTrapState");
    auto pos = THUAI9::cellxy_t(cellX, cellY);
    auto it = currentState->mapInfo->trapState.find(pos);
    if (it != currentState->mapInfo->trapState.end())
    {
        return std::make_optional<THUAI9::Trap>(currentState->mapInfo->trapState[pos].trapType, currentState->mapInfo->trapState[pos].trap_valid, currentState->mapInfo->trapState[pos].team_id);
    }
    else
    {
        logger->warn("Trap not found at ({}, {})", cellX, cellY);
        return std::make_optional<THUAI9::Trap>(
            THUAI9::TrapType::NullTrapType,  // 默认类型
            false,                           // 默认有效性
            0                                // 默认 ID
        );
    }
}

int32_t Logic::GetEnergy() const
{
    std::unique_lock<std::mutex> lock(mtxState);
    logger->debug("Called GetEnergy");
    if (currentState->teamSelf)
        return static_cast<int32_t>(currentState->teamSelf->energy);
    if (teamID > 0 && static_cast<size_t>(teamID) <= currentState->gameInfo->teams.size())
        return currentState->gameInfo->teams[teamID - 1].computePower;
    logger->warn("Team info not ready when calling GetEnergy");
    return -1;
}

int32_t Logic::GetMaterial() const
{
    std::unique_lock<std::mutex> lock(mtxState);
    logger->debug("Called GetMaterial");
    if (currentState->teamSelf)
        return static_cast<int32_t>(currentState->teamSelf->material);
    if (teamID > 0 && static_cast<size_t>(teamID) <= currentState->gameInfo->teams.size())
        return currentState->gameInfo->teams[teamID - 1].material;
    logger->warn("Team info not ready when calling GetMaterial");
    return -1;
}

int32_t Logic::GetScore() const
{
    std::unique_lock<std::mutex> lock(mtxState);
    logger->debug("Called GetScore");
    if (currentState->teamSelf)
        return static_cast<int32_t>(currentState->teamSelf->score);
    if (teamID > 0 && static_cast<size_t>(teamID) <= currentState->gameInfo->teams.size())
        return currentState->gameInfo->teams[teamID - 1].score;
    logger->warn("Team info not ready when calling GetScore");
    return -1;
}

std::shared_ptr<const THUAI9::GameInfo> Logic::GetGameInfo() const
{
    std::unique_lock<std::mutex> lock(mtxState);
    logger->debug("Called GetGameInfo");
    return currentState->gameInfo;
}

bool Logic::Send(int32_t toID, std::string message, bool binary)
{
    logger->debug("Called SendMessage");
    return pComm->Send(playerID, toID, teamID, std::move(message), binary);
}

bool Logic::HaveMessage()
{
    logger->debug("Called HaveMessage");
    return !messageQueue.empty();
}

std::pair<int32_t, std::string> Logic::GetMessage()
{
    logger->debug("Called GetMessage");
    auto msg = messageQueue.tryPop();
    if (msg.has_value())
        return msg.value();
    else
    {
        logger->warn("No message");
        return std::pair(-1, std::string(""));
    }
}

bool Logic::Common_Attack(int64_t teamID, int64_t playerID, int64_t attacked_teamID, int64_t attacked_playerID)
{
    logger->debug("Called Attack");
    return pComm->Common_Attack(teamID, playerID, attacked_teamID, attacked_playerID);
}

bool Logic::Skill_Attack(int64_t teamID, int64_t playerID, double angle)
{
    logger->debug("Called SkillAttack");
    return pComm->Skill_Attack(teamID, playerID, angle);
}

bool Logic::AttackConstruction(int64_t playerID, int64_t teamID)
{
    logger->debug("Called AttackConstruction");
    return pComm->AttackConstruction(playerID, teamID);
}

bool Logic::AttackAdditionResource(int64_t playerID, int64_t teamID)
{
    logger->debug("Called AttackAdditionResource");
    return pComm->AttackAdditionResource(playerID, teamID);
}

bool Logic::Recover(int64_t recover)
{
    logger->debug("Called Recover");
    return pComm->Recover(playerID, recover, teamID);
}

bool Logic::Harvest(int64_t playerID, int64_t teamID)
{
    logger->debug("Called Harvest");
    return pComm->Harvest(playerID, teamID);
}

bool Logic::Occupy(int64_t playerID, int64_t teamID)
{
    logger->debug("Called Occupy");
    return pComm->Occupy(playerID, teamID);
}

bool Logic::Load(int64_t playerID, int64_t teamID, THUAI9::GoodsType goodsType, int32_t amount)
{
    logger->debug("Called Load");
    return pComm->Load(playerID, teamID, goodsType, amount);
}

bool Logic::Buy(int64_t playerID, int64_t teamID, THUAI9::GoodsType goodsType, int32_t amount)
{
    logger->debug("Called Buy");
    return pComm->Trade(playerID, teamID, goodsType, amount, true);
}

bool Logic::Sell(int64_t playerID, int64_t teamID, THUAI9::GoodsType goodsType, int32_t amount)
{
    logger->debug("Called Sell");
    return pComm->Trade(playerID, teamID, goodsType, amount, false);
}

bool Logic::Construct(THUAI9::ConstructionType constructiontype)
{
    logger->debug("Called Construct");
    return pComm->Construct(playerID, teamID, constructiontype);
}

bool Logic::ConstructTrap(THUAI9::TrapType trapType)
{
    logger->debug("Called ConstructTrap");
    return pComm->ConstructTrap(playerID, teamID, trapType);
}

bool Logic::BuildCharacter(THUAI9::CharacterType CharacterType, int32_t birthIndex)
{
    logger->debug("Called BuildCharacter");
    return pComm->BuildCharacter(teamID, CharacterType, birthIndex);
}

// 等待完成
/* bool Logic::Recycle(int32_t playerID, int32_t targetID)
{
    logger->debug("Called Recycle");
    return pComm->Recycle(playerID, targetID);
}*/

bool Logic::Produce(int64_t playerID, int64_t teamID)
{
    logger->debug("Called Produce");
    return pComm->Produce(playerID, teamID);
}

bool Logic::Move(int64_t moveTimeInMilliseconds, double angle)
{
    logger->debug("Called Move");
    return pComm->Move(playerID, teamID, moveTimeInMilliseconds, angle);
}

/*bool Logic::Rebuild(THUAI9::ConstructionType constructionType)
{
    logger->debug("Called Rebuild");
    return pComm->Rebuild(playerID, teamID, constructionType);
}*/

bool Logic::InstallEquipment(int32_t playerID, THUAI9::EquipmentType equipmentType)
{
    logger->debug("Called InstallEquipment");
    return pComm->InstallEquipment(playerID, teamID, equipmentType);
}

bool Logic::ProduceGoods(THUAI9::GoodsType goodsType, int32_t maxProduceNum)
{
    logger->debug("Called ProduceGoods");
    return pComm->ProduceGoods(teamID, goodsType, maxProduceNum);
}

bool Logic::UplevelTech(THUAI9::TechType techType)
{
    logger->debug("Called UplevelTech");
    return pComm->UplevelTech(teamID, techType);
}

bool Logic::EndAllAction()
{
    logger->debug("Called EndAllAction");
    return pComm->EndAllAction(playerID, teamID);
}

bool Logic::WaitThread()
{
    if (asynchronous)
        Wait();
    return true;
}

void Logic::ProcessMessage()
{
    auto messageThread = [this]()
    {
        try
        {
            // TODO
            logger->info("Message thread start!");
            pComm->AddPlayer(playerID, teamID, CharacterType, side_flag);
            while (gameState != THUAI9::GameState::GameEnd)
            {
                auto clientMsg = pComm->GetMessage2Client();
                // 在获得新消息之前阻塞
                logger->debug("Get message from server!");
                gameState = Proto2THUAI9::gameStateDict[clientMsg.game_state()];
                switch (gameState)
                {
                    case THUAI9::GameState::GameStart:
                        logger->info("Game Start!");
                        // 读取地图
                        for (const auto& item : clientMsg.obj_message())
                        {
                            if (Proto2THUAI9::messageOfObjDict[item.message_of_obj_case()] == THUAI9::MessageOfObj::MapMessage)
                            {
                                auto map = std::vector<std::vector<THUAI9::PlaceType>>();
                                auto& mapResult = item.map_message();
                                for (int32_t i = 0; i < item.map_message().rows_size(); i++)
                                {
                                    std::vector<THUAI9::PlaceType> row;
                                    for (int32_t j = 0; j < mapResult.rows(i).cols_size(); j++)
                                    {
                                        if (Proto2THUAI9::placeTypeDict.count(mapResult.rows(i).cols(j)) == 0)
                                            logger->error("Unknown place type!");
                                        row.push_back(Proto2THUAI9::placeTypeDict[mapResult.rows(i).cols(j)]);
                                    }
                                    map.push_back(std::move(row));
                                }
                                bufferState->gameMap = std::move(map);
                                currentState->gameMap = bufferState->gameMap;
                                logger->info("Map loaded!");
                                break;
                            }
                        }
                        if (currentState->gameMap.empty())
                        {
                            logger->error("Map not loaded!");
                            throw std::runtime_error("Map not loaded!");
                        }
                        LoadBuffer(clientMsg);
                        AILoop = true;
                        UnBlockAI();
                        break;
                    case THUAI9::GameState::GameRunning:
                        LoadBuffer(clientMsg);
                        break;
                    default:
                        logger->debug("Unknown GameState!");
                        break;
                }
            }
            {
                std::lock_guard<std::mutex> lock(mtxBuffer);
                bufferUpdated = true;
                counterBuffer = -1;
            }
            cvBuffer.notify_one();
            logger->info("Game End!");
            AILoop = false;
        }
        catch (const std::exception& e)
        {
            std::cerr << "C++ Exception: " << e.what() << std::endl;
            AILoop = false;
        }
        catch (...)
        {
            std::cerr << "Unknown Exception!" << std::endl;
            AILoop = false;
        }
    };
    std::thread(messageThread).detach();
}

void Logic::LoadBufferSelf(const protobuf::MessageToClient& message)
{
    if (playerType == THUAI9::PlayerType::Character)
    {
        for (const auto& item : message.obj_message())
        {
            if (Proto2THUAI9::messageOfObjDict[item.message_of_obj_case()] == THUAI9::MessageOfObj::CharacterMessage && item.character_message().player_id() == playerID && item.character_message().team_id() == teamID)
            {
                bufferState->characterSelf = Proto2THUAI9::Protobuf2THUAI9Character(item.character_message());
                bufferState->characters.push_back(bufferState->characterSelf);
                logger->debug("Load Self Character!");
            }
        }
    }
    else if (playerType == THUAI9::PlayerType::Team)
    {
        if (teamID > 0 && teamID <= message.all_message().teams_size())
        {
            auto team = std::make_shared<THUAI9::Team>();
            team->teamID = teamID;
            team->playerID = playerID;
            team->score = message.all_message().teams(teamID - 1).score();
            team->material = message.all_message().teams(teamID - 1).material();
            team->energy = message.all_message().teams(teamID - 1).compute_power();
            team->factoryHP = message.all_message().teams(teamID - 1).factory_hp();
            bufferState->teamSelf = team;
            logger->debug("Load Self Team From AllMessage!");
        }
        for (const auto& item : message.obj_message())
        {
            if (Proto2THUAI9::messageOfObjDict[item.message_of_obj_case()] == THUAI9::MessageOfObj::CharacterMessage && item.character_message().team_id() == teamID)
            {
                std::shared_ptr<THUAI9::Character> Character = Proto2THUAI9::Protobuf2THUAI9Character(item.character_message());
                bufferState->characters.push_back(Character);
                logger->debug("Load Character!");
            }
        }
    }
}

void Logic::LoadBufferCase(const protobuf::MessageOfObj& item)
{
    switch (item.message_of_obj_case())
    {
        case protobuf::MessageOfObj::MessageOfObjCase::kCharacterMessage:
            {
                const auto& msg = item.character_message();
                auto character = Proto2THUAI9::Protobuf2THUAI9Character(msg);
                if (msg.team_id() == teamID)
                {
                    if (msg.player_id() == playerID)
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
        case protobuf::MessageOfObj::MessageOfObjCase::kTeamMessage:
            {
                const auto& msg = item.team_message();
                if (msg.team_id() == teamID)
                    bufferState->teamSelf = Proto2THUAI9::Protobuf2THUAI9Team(msg);
                break;
            }
        case protobuf::MessageOfObj::MessageOfObjCase::kFactoryMessage:
            {
                const auto& msg = item.factory_message();
                auto pos = THUAI9::cellxy_t(
                    AssistFunction::GridToCell(msg.x()),
                    AssistFunction::GridToCell(msg.y())
                );
                bufferState->mapInfo->barracksState[pos] = std::make_pair(msg.team_id(), msg.hp());
                bufferState->mapInfo->factories[pos] = *Proto2THUAI9::Protobuf2THUAI9Factory(msg);
                break;
            }
        case protobuf::MessageOfObj::MessageOfObjCase::kResourceMessage:
            {
                const auto& msg = item.resource_message();
                auto pos = THUAI9::cellxy_t(
                    AssistFunction::GridToCell(msg.x()),
                    AssistFunction::GridToCell(msg.y())
                );
                auto resource = Proto2THUAI9::Protobuf2THUAI9EconomyResource(msg);
                bufferState->mapInfo->economyResource[pos] = *resource;
                break;
            }
        case protobuf::MessageOfObj::MessageOfObjCase::kMarketMessage:
            {
                const auto& msg = item.market_message();
                auto pos = THUAI9::cellxy_t(
                    AssistFunction::GridToCell(msg.x()),
                    AssistFunction::GridToCell(msg.y())
                );
                bufferState->mapInfo->markets[pos] = *Proto2THUAI9::Protobuf2THUAI9Market(msg);
                break;
            }
        case protobuf::MessageOfObj::MessageOfObjCase::kComputeCenterMessage:
            {
                const auto& msg = item.compute_center_message();
                auto pos = THUAI9::cellxy_t(
                    AssistFunction::GridToCell(msg.x()),
                    AssistFunction::GridToCell(msg.y())
                );
                bufferState->mapInfo->springState[pos] = std::make_pair(msg.owner_team_id(), msg.occupy_progress());
                bufferState->mapInfo->computeCenters[pos] = *Proto2THUAI9::Protobuf2THUAI9ComputeCenter(msg);
                break;
            }
        case protobuf::MessageOfObj::MessageOfObjCase::kBarrierMessage:
            {
                const auto& msg = item.barrier_message();
                auto pos = THUAI9::cellxy_t(
                    AssistFunction::GridToCell(msg.x()),
                    AssistFunction::GridToCell(msg.y())
                );
                bufferState->mapInfo->trapState[pos] = THUAI9::Trap(THUAI9::TrapType::Hole, true, 0);
                break;
            }
        case protobuf::MessageOfObj::MessageOfObjCase::kBushMessage:
            {
                const auto& msg = item.bush_message();
                auto pos = THUAI9::cellxy_t(
                    AssistFunction::GridToCell(msg.x()),
                    AssistFunction::GridToCell(msg.y())
                );
                bufferState->mapInfo->farmState[pos] = std::make_pair(msg.bush_id(), msg.radius());
                break;
            }
        case protobuf::MessageOfObj::MessageOfObjCase::kNewsMessage:
            {
                const auto& news = item.news_message();
                if (news.to_id() == playerID && news.team_id() == teamID)
                {
                    auto newsType = Proto2THUAI9::newsTypeDict[news.news_case()];
                    if (newsType == THUAI9::NewsType::TextMessage)
                        messageQueue.emplace(std::pair<int32_t, std::string>(static_cast<int32_t>(news.from_id()), news.text_message()));
                    else if (newsType == THUAI9::NewsType::BinaryMessage)
                        messageQueue.emplace(std::pair<int32_t, std::string>(static_cast<int32_t>(news.from_id()), news.binary_message()));
                }
                break;
            }
        default:
            break;
    }
}
void Logic::LoadBuffer(const protobuf::MessageToClient& message)
{
    // 将消息读入到buffer中
    {
        std::lock_guard<std::mutex> lock(mtxBuffer);

        // 清空原有信息
        bufferState->characters.clear();
        bufferState->enemyCharacters.clear();
        bufferState->guids.clear();
        bufferState->allGuids.clear();
        logger->info("Buffer cleared!");
        // 读取新的信息
        for (const auto& obj : message.obj_message())
            if (Proto2THUAI9::messageOfObjDict[obj.message_of_obj_case()] == THUAI9::MessageOfObj::CharacterMessage)
            {
                bufferState->allGuids.push_back(obj.character_message().guid());
                if (obj.character_message().team_id() == teamID)
                    bufferState->guids.push_back(obj.character_message().guid());
            }
        bufferState->gameInfo = Proto2THUAI9::Protobuf2THUAI9GameInfo(message.all_message());
        LoadBufferSelf(message);
        if (playerType == THUAI9::PlayerType::Character && !bufferState->characterSelf)
        {
            logger->info("exit for nullSelf");
            return;
        }
        for (const auto& item : message.obj_message())
            LoadBufferCase(item);
    }
    if (asynchronous)
    {
        {
            std::lock_guard<std::mutex> lock(mtxState);
            std::swap(currentState, bufferState);
            counterState = counterBuffer;
            logger->info("Update State!");
        }
        freshed = true;
    }
    else
    {
        bufferUpdated = true;
    }
    counterBuffer++;
    // 唤醒其他线程
    cvBuffer.notify_one();
}
void Logic::Update() noexcept
{
    if (!asynchronous)
    {
        std::unique_lock<std::mutex> lock(mtxBuffer);
        // 缓冲区被更新之后才可以使用
        cvBuffer.wait(lock, [this]()
                      { return bufferUpdated; });
        {
            std::lock_guard<std::mutex> stateLock(mtxState);
            std::swap(currentState, bufferState);
            counterState = counterBuffer;
        }
        bufferUpdated = false;
        logger->info("Update State!");
    }
}
void Logic::Wait() noexcept
{
    freshed = false;
    {
        std::unique_lock<std::mutex> lock(mtxBuffer);
        cvBuffer.wait(lock, [this]()
                      { return freshed.load(); });
    }
}

void Logic::UnBlockAI()
{
    {
        std::lock_guard<std::mutex> lock(mtxAI);
        AIStart = true;
    }
    cvAI.notify_one();
}

int32_t Logic::GetCounter() const
{
    std::unique_lock<std::mutex> lock(mtxState);
    return counterState;
}

std::vector<int64_t> Logic::GetPlayerGUIDs() const
{
    std::unique_lock<std::mutex> lock(mtxState);
    return currentState->guids;
}

bool Logic::TryConnection()
{
    logger->info("Try to connect to server...");
    return pComm->TryConnection(playerID, teamID);
}

bool Logic::HaveView(int32_t x, int32_t y, int32_t newX, int32_t newY, int32_t viewRange, std::vector<std::vector<THUAI9::PlaceType>>& map) const
{
    std::unique_lock<std::mutex> lock(mtxState);
    return AssistFunction::HaveView(x, y, newX, newY, viewRange, map);
}

void Logic::Main(CreateAIFunc createAI, std::string IP, std::string port, bool file, bool print, bool warnOnly, bool side_flag)
{
    // 建立日志组件
    auto fileLogger = std::make_shared<spdlog::sinks::basic_file_sink_mt>(fmt::format("logs/logic-{}-{}-log.txt", playerID, teamID), true);
    auto printLogger = std::make_shared<spdlog::sinks::stdout_color_sink_mt>();
    std::string pattern = "[logic] [%H:%M:%S.%e] [%l] %v";
    fileLogger->set_pattern(pattern);
    printLogger->set_pattern(pattern);
    if (file)
        fileLogger->set_level(spdlog::level::debug);
    else
        fileLogger->set_level(spdlog::level::off);
    if (print)
        printLogger->set_level(spdlog::level::info);
    else
        printLogger->set_level(spdlog::level::off);
    if (warnOnly)
        printLogger->set_level(spdlog::level::warn);
    logger = std::make_unique<spdlog::logger>("logicLogger", spdlog::sinks_init_list{fileLogger, printLogger});

    logger->flush_on(spdlog::level::warn);
    // 打印当前的调试信息
    logger->info("*********Basic Info*********");
    logger->info("asynchronous: {}", asynchronous);
    logger->info("server: {}:{}", IP, port);
    if (playerType == THUAI9::PlayerType::Character)
        logger->info("Character ID: {}", playerID);
    logger->info("team id: {}", teamID);
    logger->info("****************************");

    // 建立与服务器之间通信的组件
    pComm = std::make_unique<Communication>(IP, port);

    // 构造timer
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

    // 构造AI线程
    auto AIThread = [&]()
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
                {
                    Wait();
                    timer->StartTimer();
                    timer->Play(*ai);
                    timer->EndTimer();
                }
                else
                {
                    Update();
                    timer->StartTimer();
                    timer->Play(*ai);
                    timer->EndTimer();
                }
            }
        }
        catch (const std::exception& e)
        {
            std::cerr << "C++ Exception: " << e.what() << std::endl;
        }
        catch (...)
        {
            std::cerr << "Unknown Exception!" << std::endl;
        }
    };

    // 连接服务器
    if (TryConnection())
    {
        logger->info("Connect to the server successfully, AI thread will be started.");
        tAI = std::thread(AIThread);
        if (tAI.joinable())
        {
            logger->info("Join the AI thread!");
            // 首先开启处理消息的线程
            ProcessMessage();
            tAI.join();
        }
    }
    else
    {
        AILoop = false;
        logger->error("Connect to the server failed, AI thread will not be started.");
        return;
    }
}