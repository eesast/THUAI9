#pragma once
#ifndef UTILS_HPP
#define UTILS_HPP

#include <chrono>
#include <cmath>
#include <cstdint>
#include <map>
#include <utility>
#include <vector>

#include "Message2Clients.pb.h"
#include "Message2Server.pb.h"
#include "MessageType.pb.h"
#include "structures.h"

#undef GetMessage
#undef SendMessage
#undef PeekMessage

namespace AssistFunction
{
    constexpr int32_t numOfGridPerCell = 1000;

    [[nodiscard]] constexpr inline int32_t GridToCell(int32_t grid) noexcept
    {
        return grid / numOfGridPerCell;
    }

    [[nodiscard]] constexpr inline int32_t GridToCell(double grid) noexcept
    {
        return static_cast<int32_t>(grid) / numOfGridPerCell;
    }

    inline bool HaveView(int32_t x, int32_t y, int32_t newX, int32_t newY, int32_t viewRange, std::vector<std::vector<THUAI9::PlaceType>>& map)
    {
        if (map.empty() || map.front().empty())
            return false;

        double deltaX = static_cast<double>(newX - x);
        double deltaY = static_cast<double>(newY - y);
        double distance = std::pow(deltaX, 2) + std::pow(deltaY, 2);

        THUAI9::PlaceType myPlace = map[GridToCell(x)][GridToCell(y)];
        THUAI9::PlaceType newPlace = map[GridToCell(newX)][GridToCell(newY)];

        if (newPlace == THUAI9::PlaceType::Bush && myPlace != THUAI9::PlaceType::Bush)
            return false;
        if (distance > std::pow(viewRange, 2))
            return false;

        int32_t divide = static_cast<int32_t>(std::max(std::abs(deltaX), std::abs(deltaY)) / 100);
        if (divide == 0)
            return true;

        double dx = deltaX / divide;
        double dy = deltaY / divide;
        double myX = static_cast<double>(x);
        double myY = static_cast<double>(y);

        if (newPlace == THUAI9::PlaceType::Bush && myPlace == THUAI9::PlaceType::Bush)
        {
            for (int32_t i = 0; i < divide; i++)
            {
                myX += dx;
                myY += dy;
                if (map[GridToCell(myX)][GridToCell(myY)] != THUAI9::PlaceType::Bush)
                    return false;
            }
        }
        else
        {
            for (int32_t i = 0; i < divide; i++)
            {
                myX += dx;
                myY += dy;
                if (map[GridToCell(myX)][GridToCell(myY)] == THUAI9::PlaceType::Barrier)
                    return false;
            }
        }

        return true;
    }
}  // namespace AssistFunction

namespace Proto2THUAI9
{
    inline std::map<protobuf::GameState, THUAI9::GameState> gameStateDict{
        {protobuf::GameState::NULL_GAME_STATE, THUAI9::GameState::NullGameState},
        {protobuf::GameState::GAME_START, THUAI9::GameState::GameStart},
        {protobuf::GameState::GAME_RUNNING, THUAI9::GameState::GameRunning},
        {protobuf::GameState::GAME_END, THUAI9::GameState::GameEnd},
    };

    inline std::map<protobuf::MessageOfObj::MessageOfObjCase, THUAI9::MessageOfObj> messageOfObjDict{
        {protobuf::MessageOfObj::MessageOfObjCase::kCharacterMessage, THUAI9::MessageOfObj::CharacterMessage},
        {protobuf::MessageOfObj::MessageOfObjCase::kFactoryMessage, THUAI9::MessageOfObj::FactoryMessage},
        {protobuf::MessageOfObj::MessageOfObjCase::kResourceMessage, THUAI9::MessageOfObj::ResourceMessage},
        {protobuf::MessageOfObj::MessageOfObjCase::kMarketMessage, THUAI9::MessageOfObj::MarketMessage},
        {protobuf::MessageOfObj::MessageOfObjCase::kComputeCenterMessage, THUAI9::MessageOfObj::ComputeCenterMessage},
        {protobuf::MessageOfObj::MessageOfObjCase::kMapMessage, THUAI9::MessageOfObj::MapMessage},
        {protobuf::MessageOfObj::MessageOfObjCase::kNewsMessage, THUAI9::MessageOfObj::NewsMessage},
        {protobuf::MessageOfObj::MessageOfObjCase::kTeamMessage, THUAI9::MessageOfObj::TeamMessage},
        {protobuf::MessageOfObj::MessageOfObjCase::kBarrierMessage, THUAI9::MessageOfObj::BarrierMessage},
        {protobuf::MessageOfObj::MessageOfObjCase::kBushMessage, THUAI9::MessageOfObj::BushMessage},
        {protobuf::MessageOfObj::MessageOfObjCase::MESSAGE_OF_OBJ_NOT_SET, THUAI9::MessageOfObj::NullMessageOfObj},
    };

    inline std::map<protobuf::PlaceType, THUAI9::PlaceType> placeTypeDict{
        {protobuf::PlaceType::NULL_PLACE_TYPE, THUAI9::PlaceType::NullPlaceType},
        {protobuf::PlaceType::FACTORY, THUAI9::PlaceType::Factory},
        {protobuf::PlaceType::SPACE, THUAI9::PlaceType::Space},
        {protobuf::PlaceType::BARRIER, THUAI9::PlaceType::Barrier},
        {protobuf::PlaceType::BUSH, THUAI9::PlaceType::Bush},
        {protobuf::PlaceType::RESOURCE, THUAI9::PlaceType::Resource},
        {protobuf::PlaceType::COMPUTE_CENTER, THUAI9::PlaceType::ComputeCenter},
        {protobuf::PlaceType::MARKET, THUAI9::PlaceType::Market},
    };

    inline std::map<protobuf::CharacterType, THUAI9::CharacterType> characterTypeDict{
        {protobuf::CharacterType::NULL_CHARACTER_TYPE, THUAI9::CharacterType::NullCharacterType},
        {protobuf::CharacterType::DRONE, THUAI9::CharacterType::Drone},
        {protobuf::CharacterType::ROBOT, THUAI9::CharacterType::Robot},
        {protobuf::CharacterType::AUTONOMOUS_CAR, THUAI9::CharacterType::AutonomousCar},
    };

    inline std::map<protobuf::CharacterState, THUAI9::CharacterState> characterStateDict{
        {protobuf::CharacterState::CHARACTER_STATE_NONE, THUAI9::CharacterState::None},
        {protobuf::CharacterState::CHARACTER_STATE_IDLE, THUAI9::CharacterState::Idle},
        {protobuf::CharacterState::CHARACTER_STATE_HARVESTING, THUAI9::CharacterState::Harvesting},
        {protobuf::CharacterState::CHARACTER_STATE_ATTACKING, THUAI9::CharacterState::Attacking},
        {protobuf::CharacterState::CHARACTER_STATE_OCUPPYING, THUAI9::CharacterState::Ocuppying},
        {protobuf::CharacterState::CHARACTER_STATE_TRADING, THUAI9::CharacterState::Trading},
        {protobuf::CharacterState::CHARACTER_STATE_MOVING, THUAI9::CharacterState::Moving},
        {protobuf::CharacterState::CHARACTER_STATE_KNOCKED_BACK, THUAI9::CharacterState::KnockedBack},
        {protobuf::CharacterState::CHARACTER_STATE_DECEASED, THUAI9::CharacterState::Deceased},
    };

    inline std::map<protobuf::ResourceType, THUAI9::ResourceType> resourceTypeDict{
        {protobuf::ResourceType::NULL_RESOURCE_TYPE, THUAI9::ResourceType::NullResourceType},
        {protobuf::ResourceType::SMALL_RESOURCE, THUAI9::ResourceType::SmallResource},
        {protobuf::ResourceType::MEDIUM_RESOURCE, THUAI9::ResourceType::MediumResource},
        {protobuf::ResourceType::LARGE_RESOURCE, THUAI9::ResourceType::LargeResource},
    };

    inline std::map<protobuf::ResourceState, THUAI9::ResourceState> resourceStateDict{
        {protobuf::ResourceState::NULL_ECONOMY_RESOURCE_STSTE, THUAI9::ResourceState::NullResourceState},
        {protobuf::ResourceState::HARVESTABLE, THUAI9::ResourceState::Harvestable},
        {protobuf::ResourceState::BEING_HARVESTED, THUAI9::ResourceState::BeingHarvested},
        {protobuf::ResourceState::HARVESTED, THUAI9::ResourceState::Harvested},
    };

    inline std::map<protobuf::GoodsType, THUAI9::GoodsType> goodsTypeDict{
        {protobuf::GoodsType::NULL_GOODS_TYPE, THUAI9::GoodsType::NullGoodsType},
        {protobuf::GoodsType::SEMICONDUCTOR, THUAI9::GoodsType::Semiconductor},
        {protobuf::GoodsType::MEDICINE, THUAI9::GoodsType::Medicine},
        {protobuf::GoodsType::TOYS, THUAI9::GoodsType::Toys},
        {protobuf::GoodsType::CLOTHES, THUAI9::GoodsType::Clothes},
        {protobuf::GoodsType::FOOD, THUAI9::GoodsType::Food},
    };

    inline std::map<protobuf::MarketType, THUAI9::MarketType> marketTypeDict{
        {protobuf::MarketType::NULL_MARKET_TYPE, THUAI9::MarketType::NullMarketType},
        {protobuf::MarketType::SMALL_MARKET, THUAI9::MarketType::SmallMarket},
        {protobuf::MarketType::MEDIUM_MARKET, THUAI9::MarketType::MediumMarket},
        {protobuf::MarketType::LARGE_MARKET, THUAI9::MarketType::LargeMarket},
    };

    inline std::map<protobuf::TechType, THUAI9::TechType> techTypeDict{
        {protobuf::TechType::NULL_TECH_TYPE, THUAI9::TechType::NullTechType},
        {protobuf::TechType::INCREASE_HP, THUAI9::TechType::IncreaseHP},
        {protobuf::TechType::INCREASE_ATTACK_POWER, THUAI9::TechType::IncreaseAttackPower},
        {protobuf::TechType::INCREASE_ATTACK_SIZE, THUAI9::TechType::IncreaseAttackSize},
        {protobuf::TechType::INCREASE_ROBUST, THUAI9::TechType::IncreaseRobust},
        {protobuf::TechType::INCREASE_MOVE_SPEED, THUAI9::TechType::IncreaseMoveSpeed},
        {protobuf::TechType::INCREASE_CARRY_CAPACITY, THUAI9::TechType::IncreaseCarryCapacity},
        {protobuf::TechType::INCREASE_EFFICIENCY, THUAI9::TechType::IncreaseEfficiency},
        {protobuf::TechType::INCREASE_PRODUCTION, THUAI9::TechType::IncreaseProduction},
        {protobuf::TechType::INCREASE_STORAGE, THUAI9::TechType::IncreaseStorage},
        {protobuf::TechType::INCREASE_PRICE, THUAI9::TechType::IncreasePrice},
        {protobuf::TechType::DECREASE_COST, THUAI9::TechType::DecreaseCost},
    };

    inline std::map<protobuf::MessageOfNews::NewsCase, THUAI9::NewsType> newsTypeDict{
        {protobuf::MessageOfNews::NewsCase::NEWS_NOT_SET, THUAI9::NewsType::NullNewsType},
        {protobuf::MessageOfNews::NewsCase::kTextMessage, THUAI9::NewsType::Text},
        {protobuf::MessageOfNews::NewsCase::kBinaryMessage, THUAI9::NewsType::Binary},
    };

    inline std::shared_ptr<THUAI9::Character> Protobuf2THUAI9Character(const protobuf::MessageOfCharacter& characterMsg)
    {
        auto character = std::make_shared<THUAI9::Character>();

        character->guid = characterMsg.guid();
        character->teamID = characterMsg.team_id();
        character->playerID = characterMsg.player_id();

        auto typeIt = characterTypeDict.find(characterMsg.character_type());
        character->characterType = (typeIt != characterTypeDict.end()) ? typeIt->second : THUAI9::CharacterType::NullCharacterType;

        auto stateIt = characterStateDict.find(characterMsg.character_active_state());
        character->characterActiveState = (stateIt != characterStateDict.end()) ? stateIt->second : THUAI9::CharacterState::None;

        character->x = characterMsg.x();
        character->y = characterMsg.y();
        character->facingDirection = characterMsg.facing_direction();
        character->speed = characterMsg.speed();
        character->viewRange = characterMsg.view_range();

        character->commonAttack = characterMsg.common_attack();
        character->commonAttackCD = characterMsg.common_attack_cd();
        character->commonAttackRange = characterMsg.common_attack_range();

        character->hp = characterMsg.hp();
        character->carryCapacity = characterMsg.carry_capacity();
        character->currentLoad = characterMsg.current_load();
        character->harvestRatePerSec = characterMsg.harvest_rate_per_sec();

        return character;
    }

    inline std::shared_ptr<THUAI9::Team> Protobuf2THUAI9Team(const protobuf::MessageOfTeam& teamMsg)
    {
        auto team = std::make_shared<THUAI9::Team>();
        team->teamID = teamMsg.team_id();
        team->playerID = teamMsg.player_id();
        team->score = teamMsg.score();
        team->material = teamMsg.material();
        team->computePower = teamMsg.compute_power();
        team->factoryHP = 0;
        return team;
    }

    inline std::shared_ptr<THUAI9::GameInfo> Protobuf2THUAI9GameInfo(const protobuf::MessageOfAll& gameInfoMsg)
    {
        auto gameInfo = std::make_shared<THUAI9::GameInfo>();
        gameInfo->gameTime = gameInfoMsg.game_time();
        gameInfo->teams.clear();

        for (const auto& teamMsg : gameInfoMsg.teams())
        {
            gameInfo->teams.push_back(THUAI9::TeamGameInfo{
                static_cast<int32_t>(gameInfo->teams.size() + 1),
                teamMsg.score(),
                teamMsg.material(),
                teamMsg.compute_power(),
                teamMsg.factory_hp()});
        }

        return gameInfo;
    }

    inline std::shared_ptr<THUAI9::Resource> Protobuf2THUAI9EconomyResource(const protobuf::MessageOfResource& resourceMsg)
    {
        auto resource = std::make_shared<THUAI9::Resource>();
        auto typeIt = resourceTypeDict.find(resourceMsg.resource_type());
        resource->resourceType = (typeIt != resourceTypeDict.end()) ? typeIt->second : THUAI9::ResourceType::NullResourceType;
        auto stateIt = resourceStateDict.find(resourceMsg.resource_state());
        resource->state = (stateIt != resourceStateDict.end()) ? stateIt->second : THUAI9::ResourceState::NullResourceState;
        resource->resourceID = resourceMsg.id();
        resource->x = resourceMsg.x();
        resource->y = resourceMsg.y();
        return resource;
    }

    inline std::shared_ptr<THUAI9::Factory> Protobuf2THUAI9Factory(const protobuf::MessageOfFactory& factoryMsg)
    {
        auto factory = std::make_shared<THUAI9::Factory>();
        factory->factoryID = factoryMsg.factory_id();
        factory->teamID = factoryMsg.team_id();
        factory->x = factoryMsg.x();
        factory->y = factoryMsg.y();
        factory->hp = factoryMsg.hp();
        factory->robust = factoryMsg.robust();
        factory->storage = factoryMsg.storage();
        factory->efficiency = factoryMsg.efficiency();
        factory->source = factoryMsg.source();
        factory->computingPower = factoryMsg.computing_power();
        factory->canProduce = factoryMsg.can_produce();
        factory->canRecruit = factoryMsg.can_recruit();

        for (const auto& goods : factoryMsg.product_inventory())
        {
            auto it = goodsTypeDict.find(goods.product_type());
            auto type = (it != goodsTypeDict.end()) ? it->second : THUAI9::GoodsType::NullGoodsType;
            factory->productInventory[type] = goods.quantity();
        }
        return factory;
    }

    inline std::shared_ptr<THUAI9::Market> Protobuf2THUAI9Market(const protobuf::MessageOfMarket& marketMsg)
    {
        auto market = std::make_shared<THUAI9::Market>();
        market->marketID = marketMsg.market_id();
        market->x = marketMsg.x();
        market->y = marketMsg.y();
        auto marketTypeIt = marketTypeDict.find(marketMsg.market_type());
        market->marketType = (marketTypeIt != marketTypeDict.end()) ? marketTypeIt->second : THUAI9::MarketType::NullMarketType;

        for (const auto& entry : marketMsg.price_list())
        {
            auto goodsIt = goodsTypeDict.find(entry.goods_type());
            auto goodsType = (goodsIt != goodsTypeDict.end()) ? goodsIt->second : THUAI9::GoodsType::NullGoodsType;
            market->priceList[goodsType] = THUAI9::MarketGoodsInfo{entry.price(), entry.traded_quantity()};
        }
        return market;
    }

    inline std::shared_ptr<THUAI9::ComputeCenter> Protobuf2THUAI9ComputeCenter(const protobuf::MessageOfComputeCenter& centerMsg)
    {
        auto center = std::make_shared<THUAI9::ComputeCenter>();
        center->centerID = centerMsg.center_id();
        center->x = centerMsg.x();
        center->y = centerMsg.y();
        center->ownerTeamID = centerMsg.owner_team_id();
        center->occupyProgress = centerMsg.occupy_progress();
        center->state = centerMsg.owner_team_id() == 0 ? THUAI9::ComputeCenterState::Occupyable : THUAI9::ComputeCenterState::Occupied;
        return center;
    }
}  // namespace Proto2THUAI9

namespace THUAI9Proto
{
    inline std::map<THUAI9::CharacterType, protobuf::CharacterType> characterTypeDict{
        {THUAI9::CharacterType::NullCharacterType, protobuf::CharacterType::NULL_CHARACTER_TYPE},
        {THUAI9::CharacterType::Drone, protobuf::CharacterType::DRONE},
        {THUAI9::CharacterType::Robot, protobuf::CharacterType::ROBOT},
        {THUAI9::CharacterType::AutonomousCar, protobuf::CharacterType::AUTONOMOUS_CAR},
    };

    inline std::map<THUAI9::GoodsType, protobuf::GoodsType> goodsTypeDict{
        {THUAI9::GoodsType::NullGoodsType, protobuf::GoodsType::NULL_GOODS_TYPE},
        {THUAI9::GoodsType::Semiconductor, protobuf::GoodsType::SEMICONDUCTOR},
        {THUAI9::GoodsType::Medicine, protobuf::GoodsType::MEDICINE},
        {THUAI9::GoodsType::Toys, protobuf::GoodsType::TOYS},
        {THUAI9::GoodsType::Clothes, protobuf::GoodsType::CLOTHES},
        {THUAI9::GoodsType::Food, protobuf::GoodsType::FOOD},
    };

    inline std::map<THUAI9::TechType, protobuf::TechType> techTypeDict{
        {THUAI9::TechType::NullTechType, protobuf::TechType::NULL_TECH_TYPE},
        {THUAI9::TechType::IncreaseHP, protobuf::TechType::INCREASE_HP},
        {THUAI9::TechType::IncreaseAttackPower, protobuf::TechType::INCREASE_ATTACK_POWER},
        {THUAI9::TechType::IncreaseAttackSize, protobuf::TechType::INCREASE_ATTACK_SIZE},
        {THUAI9::TechType::IncreaseRobust, protobuf::TechType::INCREASE_ROBUST},
        {THUAI9::TechType::IncreaseMoveSpeed, protobuf::TechType::INCREASE_MOVE_SPEED},
        {THUAI9::TechType::IncreaseCarryCapacity, protobuf::TechType::INCREASE_CARRY_CAPACITY},
        {THUAI9::TechType::IncreaseEfficiency, protobuf::TechType::INCREASE_EFFICIENCY},
        {THUAI9::TechType::IncreaseProduction, protobuf::TechType::INCREASE_PRODUCTION},
        {THUAI9::TechType::IncreaseStorage, protobuf::TechType::INCREASE_STORAGE},
        {THUAI9::TechType::IncreasePrice, protobuf::TechType::INCREASE_PRICE},
        {THUAI9::TechType::DecreaseCost, protobuf::TechType::DECREASE_COST},
    };

    inline protobuf::MoveMsg THUAI92ProtobufMoveMsg(int64_t teamID, int64_t playerID, int64_t timeInMilliseconds, double angle)
    {
        protobuf::MoveMsg moveMsg;
        moveMsg.set_player_id(playerID);
        moveMsg.set_team_id(teamID);
        moveMsg.set_time_in_milliseconds(timeInMilliseconds);
        moveMsg.set_angle(angle);
        return moveMsg;
    }

    inline protobuf::IDMsg THUAI92ProtobufIDMsg(int64_t playerID, int64_t teamID)
    {
        protobuf::IDMsg idMsg;
        idMsg.set_player_id(playerID);
        idMsg.set_team_id(teamID);
        return idMsg;
    }

    inline protobuf::SendMsg THUAI92ProtobufSendMsg(int64_t playerID, int64_t toPlayerID, int64_t teamID, std::string msg, bool binary)
    {
        protobuf::SendMsg sendMsg;
        sendMsg.set_player_id(playerID);
        sendMsg.set_to_player_id(toPlayerID);
        sendMsg.set_team_id(teamID);
        if (binary)
            sendMsg.set_binary_message(std::move(msg));
        else
            sendMsg.set_text_message(std::move(msg));
        return sendMsg;
    }

    inline protobuf::RecoverMsg THUAI92ProtobufRecoverMsg(int64_t playerID, int64_t recoveredHp, int64_t teamID)
    {
        protobuf::RecoverMsg recoverMsg;
        recoverMsg.set_player_id(playerID);
        recoverMsg.set_team_id(teamID);
        recoverMsg.set_recovered_hp(recoveredHp);
        return recoverMsg;
    }

    inline protobuf::AttackMsg THUAI92ProtobufAttackMsg(int64_t teamID, int64_t playerID, int64_t attackedTeamID, int64_t attackedPlayerID)
    {
        protobuf::AttackMsg attackMsg;
        attackMsg.set_player_id(playerID);
        attackMsg.set_team_id(teamID);
        attackMsg.set_attack_range(0);
        attackMsg.set_attacked_player_id(attackedPlayerID);
        attackMsg.set_attacked_team_id(attackedTeamID);
        return attackMsg;
    }

    inline protobuf::CreateCharacterMsg THUAI92ProtobufCreateCharacterMsg(int64_t teamID, int64_t playerID, THUAI9::CharacterType characterType)
    {
        protobuf::CreateCharacterMsg createCharacterMsg;
        createCharacterMsg.set_team_id(teamID);
        createCharacterMsg.set_player_id(playerID);

        auto it = characterTypeDict.find(characterType);
        createCharacterMsg.set_character_type((it != characterTypeDict.end()) ? it->second : protobuf::CharacterType::NULL_CHARACTER_TYPE);
        return createCharacterMsg;
    }

    inline protobuf::ResourceMsg THUAI92ProtobufHarvestMsg(int64_t playerID, int64_t teamID)
    {
        protobuf::ResourceMsg resourceMsg;
        resourceMsg.set_player_id(playerID);
        resourceMsg.set_team_id(teamID);
        resourceMsg.set_resource_id(0);
        resourceMsg.set_target_x(0);
        resourceMsg.set_target_y(0);
        resourceMsg.set_amount(0);
        return resourceMsg;
    }

    inline protobuf::OccupyMsg THUAI92ProtobufOccupyMsg(int64_t playerID, int64_t teamID)
    {
        protobuf::OccupyMsg occupyMsg;
        occupyMsg.set_player_id(playerID);
        occupyMsg.set_team_id(teamID);
        occupyMsg.set_target_x(0);
        occupyMsg.set_target_y(0);
        occupyMsg.set_target_compute_center_id(0);
        return occupyMsg;
    }

    inline protobuf::LoadMsg THUAI92ProtobufLoadMsg(int64_t playerID, int64_t teamID, THUAI9::GoodsType goodsType, int32_t amount)
    {
        protobuf::LoadMsg loadMsg;
        loadMsg.set_team_id(teamID);
        loadMsg.set_player_id(playerID);
        auto it = goodsTypeDict.find(goodsType);
        loadMsg.set_product_type((it != goodsTypeDict.end()) ? it->second : protobuf::GoodsType::NULL_GOODS_TYPE);
        loadMsg.set_product_amount(amount);
        return loadMsg;
    }

    inline protobuf::TradeMsg THUAI92ProtobufTradeMsg(int64_t playerID, int64_t teamID, THUAI9::GoodsType goodsType, int32_t amount, bool isBuy)
    {
        protobuf::TradeMsg tradeMsg;
        tradeMsg.set_team_id(teamID);
        tradeMsg.set_player_id(playerID);
        auto it = goodsTypeDict.find(goodsType);
        tradeMsg.set_product_type((it != goodsTypeDict.end()) ? it->second : protobuf::GoodsType::NULL_GOODS_TYPE);
        tradeMsg.set_product_amount(amount);
        tradeMsg.set_is_buy(isBuy);
        return tradeMsg;
    }

    inline protobuf::ProduceGoodsMsg THUAI92ProtobufProduceGoodsMsg(int64_t teamID, THUAI9::GoodsType goodsType = THUAI9::GoodsType::NullGoodsType, int32_t maxProduceNum = 1)
    {
        protobuf::ProduceGoodsMsg produceGoodsMsg;
        produceGoodsMsg.set_team_id(teamID);
        auto it = goodsTypeDict.find(goodsType);
        produceGoodsMsg.set_product_type((it != goodsTypeDict.end()) ? it->second : protobuf::GoodsType::NULL_GOODS_TYPE);
        produceGoodsMsg.set_max_produce_num(maxProduceNum);
        return produceGoodsMsg;
    }

    inline protobuf::UplevelTechMsg THUAI92ProtobufUplevelTechMsg(int64_t teamID, THUAI9::TechType techType)
    {
        protobuf::UplevelTechMsg uplevelTechMsg;
        uplevelTechMsg.set_team_id(teamID);
        auto it = techTypeDict.find(techType);
        uplevelTechMsg.set_tech_type((it != techTypeDict.end()) ? it->second : protobuf::TechType::NULL_TECH_TYPE);
        return uplevelTechMsg;
    }

    inline protobuf::RegisterFactoryMsg THUAI92ProtobufRegisterFactoryMsg(int64_t playerID, int64_t teamID, bool sideFlag)
    {
        protobuf::RegisterFactoryMsg registerMsg;
        registerMsg.set_player_id(playerID);
        registerMsg.set_team_id(teamID);
        registerMsg.set_side_flag(sideFlag ? 1 : 0);
        return registerMsg;
    }
}  // namespace THUAI9Proto

namespace Time
{
    inline double TimeSinceStart(const std::chrono::system_clock::time_point& sp)
    {
        auto tp = std::chrono::system_clock::now();
        auto timeSpan = std::chrono::duration_cast<std::chrono::duration<double, std::milli>>(tp - sp);
        return timeSpan.count();
    }
}  // namespace Time

#endif
