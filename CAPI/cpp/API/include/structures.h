#pragma once
#ifndef STRUCTURES_H
#define STRUCTURES_H
#define FMT_ENABLE_ENUM_IMPLICIT
#include <cstdint>
#include <array>
#include <map>
#include <vector>
#include <string>
#include <format.h>
#undef GetMessage
#undef SendMessage
#undef PeekMessage

namespace THUAI9
{
    enum class GameMode : unsigned char
    {
        NullGameMode = 0,
        GameModePve = 1,
        GameModePvp = 2,
    };
    // 游戏状态
    enum class GameState : unsigned char
    {
        NullGameState = 0,
        GameStart = 1,
        GameRunning = 2,
        GameEnd = 3,
    };
    // 所有NullXXXType均为错误类型，其余为可能出现的正常类型

    // 位置标志
    enum class PlaceType : unsigned char
    {
        NullPlaceType = 0,
        Factory = 1,
        Space = 2,
        Barrier = 3,
        Bush = 4,
        Resource = 5,
        ComputeCenter = 6,
        Market = 7,
    };

    // 形状标志
    enum class ShapeType : unsigned char
    {
        NullShapeType = 0,
        Circle = 1,
        Square = 2,
    };

    enum class PlayerType : unsigned char
    {
        NullPlayerType = 0,
        Character = 1,
        Team = 2,
    };

    enum class CharacterType : unsigned char
    {
        NullCharacterType = 0,
        Drone = 1,
        Robot = 2,
        AutonomousCar = 3,
    };

    enum class CharacterState : unsigned char
    {
        None = 0,
        Idle = 1,
        Harvesting = 2,
        Attacking = 3,
        Ocuppying = 4,
        Trading = 5,
        Moving = 6,
        KnockedBack = 7,
        Deceased = 8,
    };

    enum class HomeState : unsigned char
    {
        NullHomeState = 0,
        HomeStateIdle = 1,
        HomeStateProducingProduct = 2,
        HomeStateRepairing = 3,
        HomeStateProducingCharacter = 4,
    };

    enum class ComputeCenterState : unsigned char
    {
        NullComputeCenterState = 0,
        Occupyable = 1,
        Occupied = 2,
        Robbed = 3,
    };

    enum class ResourceState : unsigned char
    {
        NullResourceState = 0,
        Harvestable = 1,
        BeingHarvested = 2,
        Harvested = 3,
    };

    enum class ResourceType : unsigned char
    {
        NullResourceType = 0,
        SmallResource = 1,
        MediumResource = 2,
        LargeResource = 3,
    };

    enum class NewsType : unsigned char
    {
        NullNewsType = 0,
        Text = 1,
        Binary = 2,
    };

    enum class GoodsType : unsigned char
    {
        NullGoodsType = 0,
        Semiconductor = 1,
        Medicine = 2,
        Toys = 3,
        Clothes = 4,
        Food = 5,
    };

    enum class MarketType : unsigned char
    {
        NullMarketType = 0,
        SmallMarket = 1,
        MediumMarket = 2,
        LargeMarket = 3,
    };

    enum class TechType : unsigned char
    {
        NullTechType = 0,
        IncreaseHP = 1,
        IncreaseAttackPower = 2,
        IncreaseAttackSize = 3,
        IncreaseRobust = 4,
        IncreaseMoveSpeed = 5,
        IncreaseCarryCapacity = 6,
        IncreaseEfficiency = 7,
        IncreaseProduction = 8,
        IncreaseStorage = 9,
        IncreasePrice = 10,
        DecreaseCost = 11,
    };

    enum class AIEventCategory : unsigned char
    {
        NullAIEventCategory = 0,
        EconomicEvent = 1,
        WeatherEvent = 2,
        TechnologyEvent = 3,
        CombatEvent = 4,
    };

    enum class TaskType : unsigned char
    {
        NullTaskType = 0,
        ProduceProduct = 1,
        HarvestResource = 2,
        OccupyCenter = 3,
        RepairUnit = 4,
    };

    enum class AIActionType : unsigned char
    {
        Unknown = 0,
        Produce = 1,
        Harvest = 2,
        Move = 3,
        Attack = 4,
        Repair = 5,
        Sell = 6,
        Occupy = 7,
    };

    enum class MessageOfObj : unsigned char
    {
        NullMessageOfObj = 0,
        FactoryMessage = 1,
        CharacterMessage = 2,
        ResourceMessage = 3,
        MarketMessage = 4,
        ComputeCenterMessage = 5,
        MapMessage = 6,
        NewsMessage = 7,
        TeamMessage = 8,
        BarrierMessage = 9,
        BushMessage = 10,
    };

    struct Character
    {
        int64_t guid;

        int64_t teamID;
        int64_t playerID;

        CharacterType characterType;
        
        CharacterState characterActiveState;

        int32_t x;
        int32_t y;

        double facingDirection;
        int32_t speed;
        int32_t viewRange;

        int32_t commonAttack;
        int64_t commonAttackCD;
        int32_t commonAttackRange;



        int32_t hp;

        int32_t carryCapacity;
        int32_t currentLoad;

        int32_t harvestRatePerSec;
    };

    struct Team
    {
        int64_t teamID;
        int64_t playerID;
        int64_t score;
        int64_t material;
        int64_t computePower;
    };

    struct Factory
    {
        int64_t factoryID = 0;
        int64_t teamID = 0;
        int32_t x = 0;
        int32_t y = 0;
        int32_t hp = 0;
        int32_t robust = 0;
        int32_t storage = 0;
        int32_t efficiency = 0;
        int64_t source = 0;
        int64_t computingPower = 0;
        bool canProduce = false;
        bool canRecruit = false;
        std::map<GoodsType, int32_t> productInventory;
    };

    struct MarketGoodsInfo
    {
        int32_t price = 0;
        int32_t tradedQuantity = 0;
    };

    struct Market
    {
        int64_t marketID = 0;
        int32_t x = 0;
        int32_t y = 0;
        MarketType marketType = MarketType::NullMarketType;
        std::map<GoodsType, MarketGoodsInfo> priceList;
    };

    struct ComputeCenter
    {
        int64_t centerID = 0;
        int32_t x = 0;
        int32_t y = 0;
        int64_t ownerTeamID = 0;
        int32_t occupyProgress = 0;
        ComputeCenterState state = ComputeCenterState::NullComputeCenterState;
    };


    using cellxy_t = std::pair<int32_t, int32_t>;

    struct GameMap
    {
        // x,y,id,hp
        std::map<cellxy_t, Factory> factories;
        std::map<cellxy_t, Market> markets;
        std::map<cellxy_t, ComputeCenter> computeCenters;
    };

    struct TeamGameInfo
    {
        int32_t teamID = 0;
        int32_t score = 0;
        int32_t material = 0;
        int32_t computePower = 0;
        int32_t factoryHP = 0;
    };

    struct GameInfo
    {
        int32_t gameTime;
        std::vector<TeamGameInfo> teams;
    };

    // 仅供DEBUG使用，名称可改动
    // 还没写完，后面待续

    inline std::map<GameState, std::string> gameStateDict{
        {GameState::NullGameState, "NullGameState"},
        {GameState::GameStart, "GameStart"},
        {GameState::GameRunning, "GameRunning"},
        {GameState::GameEnd, "GameEnd"},
    };

    inline std::map<CharacterType, std::string> characterTypeDict{
        {CharacterType::NullCharacterType, "NullCharacterType"},
        {CharacterType::Drone, "Drone"},
        {CharacterType::Robot, "Robot"},
        {CharacterType::AutonomousCar, "AutonomousCar"},
    };

    inline std::map<CharacterState, std::string> characterStateDict{
        {CharacterState::None, "NullCharacterState"},
        {CharacterState::Idle, "Idle"},
        {CharacterState::Harvesting, "Harvesting"},
        {CharacterState::Attacking, "Attacking"},
        {CharacterState::Ocuppying, "Ocuppying"},
        {CharacterState::Trading, "Trading"},
        {CharacterState::Moving, "Moving"},
        {CharacterState::KnockedBack, "KnockedBack"},
        {CharacterState::Deceased, "Deceased"},
    };

    inline std::map<PlaceType, std::string> placeTypeDict{
        {PlaceType::NullPlaceType, "NullPlaceType"},
        {PlaceType::Factory, "Factory"},
        {PlaceType::Space, "Space"},
        {PlaceType::Barrier, "Barrier"},
        {PlaceType::Bush, "Bush"},
        {PlaceType::Resource, "Resource"},
        {PlaceType::ComputeCenter, "ComputeCenter"},
        {PlaceType::Market, "Market"},
    };


    inline std::map<ResourceState, std::string> resourceStateDict{
        {ResourceState::NullResourceState, "NullResourceState"},
        {ResourceState::Harvestable, "Harvestable"},
        {ResourceState::BeingHarvested, "BeingHarvested"},
        {ResourceState::Harvested, "Harvested"},
    };

    inline std::map<MessageOfObj, std::string> messageOfObjDict{
        {MessageOfObj::NullMessageOfObj, "NullMessageOfObj"},
        {MessageOfObj::FactoryMessage, "FactoryMessage"},
        {MessageOfObj::CharacterMessage, "CharacterMessage"},
        {MessageOfObj::ResourceMessage, "ResourceMessage"},
        {MessageOfObj::MarketMessage, "MarketMessage"},
        {MessageOfObj::ComputeCenterMessage, "ComputeCenterMessage"},
        {MessageOfObj::MapMessage, "MapMessage"},
        {MessageOfObj::NewsMessage, "NewsMessage"},
        {MessageOfObj::TeamMessage, "TeamMessage"},
        {MessageOfObj::BarrierMessage, "BarrierMessage"},
        {MessageOfObj::BushMessage, "BushMessage"},
    };

    inline std::map<NewsType, std::string> newsTypeDict{
        {NewsType::NullNewsType, "NullNewsType"},
        {NewsType::Text, "TextMessage"},
        {NewsType::Binary, "BinaryMessage"},
    };

    inline std::map<GoodsType, std::string> goodsTypeDict{
        {GoodsType::NullGoodsType, "NullGoodsType"},
        {GoodsType::Semiconductor, "Semiconductor"},
        {GoodsType::Medicine, "Medicine"},
        {GoodsType::Toys, "Toys"},
        {GoodsType::Clothes, "Clothes"},
        {GoodsType::Food, "Food"},
    };

    inline std::map<MarketType, std::string> marketTypeDict{
        {MarketType::NullMarketType, "NullMarketType"},
        {MarketType::SmallMarket, "SmallMarket"},
        {MarketType::MediumMarket, "MediumMarket"},
        {MarketType::LargeMarket, "LargeMarket"},
    };

    inline std::map<TechType, std::string> techTypeDict{
        {TechType::NullTechType, "NullTechType"},
        {TechType::IncreaseHP, "IncreaseHP"},
        {TechType::IncreaseAttackPower, "IncreaseAttackPower"},
        {TechType::IncreaseAttackSize, "IncreaseAttackSize"},
        {TechType::IncreaseRobust, "IncreaseRobust"},
        {TechType::IncreaseMoveSpeed, "IncreaseMoveSpeed"},
        {TechType::IncreaseCarryCapacity, "IncreaseCarryCapacity"},
        {TechType::IncreaseEfficiency, "IncreaseEfficiency"},
        {TechType::IncreaseProduction, "IncreaseProduction"},
        {TechType::IncreaseStorage, "IncreaseStorage"},
        {TechType::IncreasePrice, "IncreasePrice"},
        {TechType::DecreaseCost, "DecreaseCost"},
    };

}  // namespace THUAI9

// fmt库的formatter特化，方便调试输出枚举类型



namespace fmt
{

    //预处理宏 THUAI9_REGISTER_FORMATTER，为所有 dict 都生成了对应的 fmt::formatter 特化
#define THUAI9_REGISTER_FORMATTER(EnumType, EnumDict) \
    template<> \
    struct formatter<THUAI9::EnumType> : formatter<std::string> \
    { \
        auto format(THUAI9::EnumType type, format_context& ctx) const \
        { \
            auto it = THUAI9::EnumDict.find(type); \
            formatter<std::string> stringFormatter; \
            return stringFormatter.format( \
                it != THUAI9::EnumDict.end() ? it->second : "Unknown" #EnumType, ctx \
            ); \
        } \
    };

    THUAI9_REGISTER_FORMATTER(GameState, gameStateDict)
    THUAI9_REGISTER_FORMATTER(CharacterType, characterTypeDict)
    THUAI9_REGISTER_FORMATTER(CharacterState, characterStateDict)
    THUAI9_REGISTER_FORMATTER(PlaceType, placeTypeDict)
    THUAI9_REGISTER_FORMATTER(ResourceState, resourceStateDict)
    THUAI9_REGISTER_FORMATTER(MessageOfObj, messageOfObjDict)
    THUAI9_REGISTER_FORMATTER(NewsType, newsTypeDict)
    THUAI9_REGISTER_FORMATTER(GoodsType, goodsTypeDict)
    THUAI9_REGISTER_FORMATTER(MarketType, marketTypeDict)
    THUAI9_REGISTER_FORMATTER(TechType, techTypeDict)

}  // namespace fmt

#endif
