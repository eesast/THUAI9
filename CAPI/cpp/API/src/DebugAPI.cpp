#include <chrono>
#include <ctime>
#include <memory>
#include <optional>
#include <string>

#include "AI.h"
#include "API.h"
#include "structures.h"
#include "utils.hpp"

#undef GetMessage
#undef SendMessage
#undef PeekMessage

namespace
{
    constexpr double pi = 3.14159265358979323846;

    std::unique_ptr<spdlog::logger> CreateApiLogger(bool file, bool print, bool warnOnly, int32_t playerID, int32_t teamID)
    {
        const std::string fileName = "logs/api-" + std::to_string(teamID) + "-" + std::to_string(playerID) + "-log.txt";
        auto fileLogger = std::make_shared<spdlog::sinks::basic_file_sink_mt>(fileName, true);
        auto printLogger = std::make_shared<spdlog::sinks::stdout_color_sink_mt>();
        const std::string pattern = "[api " + std::to_string(teamID) + "-" + std::to_string(playerID) + "] [%H:%M:%S.%e] [%l] %v";
        fileLogger->set_pattern(pattern);
        printLogger->set_pattern(pattern);
        fileLogger->set_level(file ? spdlog::level::trace : spdlog::level::off);
        printLogger->set_level(print ? spdlog::level::info : spdlog::level::off);
        if (warnOnly)
            printLogger->set_level(spdlog::level::warn);
        auto logger = std::make_unique<spdlog::logger>("apiLogger-" + std::to_string(teamID) + "-" + std::to_string(playerID), spdlog::sinks_init_list{fileLogger, printLogger});
        logger->flush_on(spdlog::level::warn);
        return logger;
    }
}  // namespace

CharacterDebugAPI::CharacterDebugAPI(ILogic& logic, bool file, bool print, bool warnOnly, int32_t characterID, int32_t teamID) :
    logger(CreateApiLogger(file, print, warnOnly, characterID, teamID)),
    logic(logic)
{
}

void CharacterDebugAPI::StartTimer()
{
    startPoint = std::chrono::system_clock::now();
    std::time_t t = std::chrono::system_clock::to_time_t(startPoint);
    logger->info("=== AI.play() ===");
    logger->info("StartTimer: {}", std::ctime(&t));
}

void CharacterDebugAPI::EndTimer()
{
    logger->info("Time elapsed: {}ms", Time::TimeSinceStart(startPoint));
}

void CharacterDebugAPI::Play(IAI& ai)
{
    ai.play(*this);
}

std::future<bool> CharacterDebugAPI::SendTextMessage(int32_t toID, std::string message)
{
    logger->info("SendTextMessage to {}", toID);
    return std::async(std::launch::async, [=, message = std::move(message)]()
                      { return logic.Send(toID, std::move(message), false); });
}

std::future<bool> CharacterDebugAPI::SendBinaryMessage(int32_t toID, std::string message)
{
    logger->info("SendBinaryMessage to {}", toID);
    return std::async(std::launch::async, [=, message = std::move(message)]()
                      { return logic.Send(toID, std::move(message), true); });
}

bool CharacterDebugAPI::HaveMessage()
{
    return logic.HaveMessage();
}

std::pair<int32_t, std::string> CharacterDebugAPI::GetMessage()
{
    return logic.GetMessage();
}

bool CharacterDebugAPI::Wait()
{
    return logic.GetCounter() != -1 && logic.WaitThread();
}

int32_t CharacterDebugAPI::GetFrameCount() const
{
    return logic.GetCounter();
}

std::future<bool> CharacterDebugAPI::EndAllAction()
{
    return std::async(std::launch::async, [this]()
                      { return logic.EndAllAction(); });
}

std::future<bool> CharacterDebugAPI::Move(int64_t moveTimeInMilliseconds, double angle)
{
    logger->info("Move {} ms", moveTimeInMilliseconds);
    return std::async(std::launch::async, [=]()
                      { return logic.Move(moveTimeInMilliseconds, angle); });
}

std::future<bool> CharacterDebugAPI::MoveRight(int64_t timeInMilliseconds)
{
    return Move(timeInMilliseconds, pi * 0.5);
}

std::future<bool> CharacterDebugAPI::MoveUp(int64_t timeInMilliseconds)
{
    return Move(timeInMilliseconds, pi);
}

std::future<bool> CharacterDebugAPI::MoveLeft(int64_t timeInMilliseconds)
{
    return Move(timeInMilliseconds, pi * 1.5);
}

std::future<bool> CharacterDebugAPI::MoveDown(int64_t timeInMilliseconds)
{
    return Move(timeInMilliseconds, 0);
}

std::future<bool> CharacterDebugAPI::Common_Attack(int64_t attackedPlayerID)
{
    logger->info("Common_Attack {}", attackedPlayerID);
    return std::async(std::launch::async, [this, attackedPlayerID]()
                      {
                          auto self = GetSelfInfo();
                          if (!self)
                              return false;
                          return logic.Common_Attack(self->teamID, self->playerID, 0, attackedPlayerID); });
}

std::future<bool> CharacterDebugAPI::Recover(int64_t recover)
{
    logger->info("Recover {}", recover);
    return std::async(std::launch::async, [=]()
                      { return logic.Recover(recover); });
}

std::future<bool> CharacterDebugAPI::Harvest()
{
    return std::async(std::launch::async, [this]()
                      {
                          auto self = GetSelfInfo();
                          if (!self)
                              return false;
                          return logic.Harvest(self->playerID, self->teamID); });
}

std::future<bool> CharacterDebugAPI::Occupy()
{
    return std::async(std::launch::async, [this]()
                      {
                          auto self = GetSelfInfo();
                          if (!self)
                              return false;
                          return logic.Occupy(self->playerID, self->teamID); });
}

std::future<bool> CharacterDebugAPI::Load(THUAI9::GoodsType goodsType, int32_t amount)
{
    return std::async(std::launch::async, [this, goodsType, amount]()
                      {
                          auto self = GetSelfInfo();
                          if (!self)
                              return false;
                          return logic.Load(self->playerID, self->teamID, goodsType, amount); });
}

std::future<bool> CharacterDebugAPI::Buy(THUAI9::GoodsType goodsType, int32_t amount)
{
    return std::async(std::launch::async, [this, goodsType, amount]()
                      {
                          auto self = GetSelfInfo();
                          if (!self)
                              return false;
                          return logic.Buy(self->playerID, self->teamID, goodsType, amount); });
}

std::future<bool> CharacterDebugAPI::Sell(THUAI9::GoodsType goodsType, int32_t amount)
{
    return std::async(std::launch::async, [this, goodsType, amount]()
                      {
                          auto self = GetSelfInfo();
                          if (!self)
                              return false;
                          return logic.Sell(self->playerID, self->teamID, goodsType, amount); });
}

std::vector<std::shared_ptr<const THUAI9::Character>> CharacterDebugAPI::GetCharacters() const
{
    return logic.GetCharacters();
}

std::vector<std::shared_ptr<const THUAI9::Character>> CharacterDebugAPI::GetEnemyCharacters() const
{
    return logic.GetEnemyCharacters();
}

std::vector<std::vector<THUAI9::PlaceType>> CharacterDebugAPI::GetFullMap() const
{
    return logic.GetFullMap();
}

std::shared_ptr<const THUAI9::GameInfo> CharacterDebugAPI::GetGameInfo() const
{
    return logic.GetGameInfo();
}

THUAI9::PlaceType CharacterDebugAPI::GetPlaceType(int32_t cellX, int32_t cellY) const
{
    return logic.GetPlaceType(cellX, cellY);
}

std::optional<THUAI9::Resource> CharacterDebugAPI::GetResourceState(int32_t cellX, int32_t cellY) const
{
    return logic.GetResourceState(cellX, cellY);
}

std::optional<THUAI9::ComputeCenter> CharacterDebugAPI::GetComputeCenterState(int32_t cellX, int32_t cellY) const
{
    return logic.GetComputeCenterState(cellX, cellY);
}

std::optional<THUAI9::Market> CharacterDebugAPI::GetMarketState(int32_t cellX, int32_t cellY) const
{
    return logic.GetMarketState(cellX, cellY);
}

std::optional<THUAI9::Factory> CharacterDebugAPI::GetFactoryState(int32_t cellX, int32_t cellY) const
{
    return logic.GetFactoryState(cellX, cellY);
}

std::vector<int64_t> CharacterDebugAPI::GetPlayerGUIDs() const
{
    return logic.GetPlayerGUIDs();
}

int32_t CharacterDebugAPI::GetComputingPower() const
{
    return logic.GetComputingPower();
}

int32_t CharacterDebugAPI::GetMaterial() const
{
    return logic.GetMaterial();
}

int32_t CharacterDebugAPI::GetScore() const
{
    return logic.GetScore();
}

std::shared_ptr<const THUAI9::Character> CharacterDebugAPI::GetSelfInfo() const
{
    return logic.CharacterGetSelfInfo();
}

bool CharacterDebugAPI::HaveView(int32_t x, int32_t y, int32_t newX, int32_t newY, int32_t viewRange, std::vector<std::vector<THUAI9::PlaceType>>& map) const
{
    return logic.HaveView(x, y, newX, newY, viewRange, map);
}

void CharacterDebugAPI::Print(std::string str) const
{
    logger->info("{}", str);
}

void CharacterDebugAPI::PrintCharacter() const
{
    for (const auto& character : logic.GetCharacters())
    {
        logger->info("Character id={}, team={}, type={}, pos=({}, {})", character->playerID, character->teamID, character->characterType, character->x, character->y);
    }
}

void CharacterDebugAPI::PrintSelfInfo() const
{
    auto self = logic.CharacterGetSelfInfo();
    if (!self)
        return;
    logger->info("Self id={}, team={}, type={}, pos=({}, {})", self->playerID, self->teamID, self->characterType, self->x, self->y);
}

TeamDebugAPI::TeamDebugAPI(ILogic& logic, bool file, bool print, bool warnOnly, int32_t playerID, int32_t teamID) :
    logger(CreateApiLogger(file, print, warnOnly, playerID, teamID)),
    logic(logic)
{
}

void TeamDebugAPI::StartTimer()
{
    startPoint = std::chrono::system_clock::now();
    std::time_t t = std::chrono::system_clock::to_time_t(startPoint);
    logger->info("=== AI.play() ===");
    logger->info("StartTimer: {}", std::ctime(&t));
}

void TeamDebugAPI::EndTimer()
{
    logger->info("Time elapsed: {}ms", Time::TimeSinceStart(startPoint));
}

void TeamDebugAPI::Play(IAI& ai)
{
    ai.play(*this);
}

std::future<bool> TeamDebugAPI::SendTextMessage(int32_t toID, std::string message)
{
    return std::async(std::launch::async, [=, message = std::move(message)]()
                      { return logic.Send(toID, std::move(message), false); });
}

std::future<bool> TeamDebugAPI::SendBinaryMessage(int32_t toID, std::string message)
{
    return std::async(std::launch::async, [=, message = std::move(message)]()
                      { return logic.Send(toID, std::move(message), true); });
}

bool TeamDebugAPI::HaveMessage()
{
    return logic.HaveMessage();
}

std::pair<int32_t, std::string> TeamDebugAPI::GetMessage()
{
    return logic.GetMessage();
}

bool TeamDebugAPI::Wait()
{
    return logic.GetCounter() != -1 && logic.WaitThread();
}

int32_t TeamDebugAPI::GetFrameCount() const
{
    return logic.GetCounter();
}

std::future<bool> TeamDebugAPI::EndAllAction()
{
    return std::async(std::launch::async, [this]()
                      { return logic.EndAllAction(); });
}

std::vector<std::shared_ptr<const THUAI9::Character>> TeamDebugAPI::GetCharacters() const
{
    return logic.GetCharacters();
}

std::vector<std::shared_ptr<const THUAI9::Character>> TeamDebugAPI::GetEnemyCharacters() const
{
    return logic.GetEnemyCharacters();
}

std::vector<std::vector<THUAI9::PlaceType>> TeamDebugAPI::GetFullMap() const
{
    return logic.GetFullMap();
}

std::shared_ptr<const THUAI9::GameInfo> TeamDebugAPI::GetGameInfo() const
{
    return logic.GetGameInfo();
}

THUAI9::PlaceType TeamDebugAPI::GetPlaceType(int32_t cellX, int32_t cellY) const
{
    return logic.GetPlaceType(cellX, cellY);
}

std::optional<THUAI9::Resource> TeamDebugAPI::GetResourceState(int32_t cellX, int32_t cellY) const
{
    return logic.GetResourceState(cellX, cellY);
}

std::optional<THUAI9::ComputeCenter> TeamDebugAPI::GetComputeCenterState(int32_t cellX, int32_t cellY) const
{
    return logic.GetComputeCenterState(cellX, cellY);
}

std::optional<THUAI9::Market> TeamDebugAPI::GetMarketState(int32_t cellX, int32_t cellY) const
{
    return logic.GetMarketState(cellX, cellY);
}

std::optional<THUAI9::Factory> TeamDebugAPI::GetFactoryState(int32_t cellX, int32_t cellY) const
{
    return logic.GetFactoryState(cellX, cellY);
}

std::vector<int64_t> TeamDebugAPI::GetPlayerGUIDs() const
{
    return logic.GetPlayerGUIDs();
}

int32_t TeamDebugAPI::GetComputingPower() const
{
    return logic.GetComputingPower();
}

int32_t TeamDebugAPI::GetMaterial() const
{
    return logic.GetMaterial();
}

int32_t TeamDebugAPI::GetScore() const
{
    return logic.GetScore();
}

std::shared_ptr<const THUAI9::Team> TeamDebugAPI::GetSelfInfo() const
{
    return logic.TeamGetSelfInfo();
}

std::future<bool> TeamDebugAPI::BuildCharacter(THUAI9::CharacterType characterType, int32_t playerID)
{
    return std::async(std::launch::async, [=]()
                      { return logic.BuildCharacter(characterType, playerID); });
}

std::future<bool> TeamDebugAPI::ProduceGoods(THUAI9::GoodsType goodsType, int32_t maxProduceNum)
{
    return std::async(std::launch::async, [=]()
                      { return logic.ProduceGoods(goodsType, maxProduceNum); });
}

std::future<bool> TeamDebugAPI::UplevelTech(THUAI9::TechType techType)
{
    return std::async(std::launch::async, [=]()
                      { return logic.UplevelTech(techType); });
}

void TeamDebugAPI::Print(std::string str) const
{
    logger->info("{}", str);
}

void TeamDebugAPI::PrintSelfInfo() const
{
    auto team = logic.TeamGetSelfInfo();
    if (!team)
        return;
    logger->info("Team id={}, score={}, material={}, computePower={}", team->teamID, team->score, team->material, team->computePower);
}
