#include <memory>
#include "AI.h"
#include "API.h"

#undef GetMessage
#undef SendMessage
#undef PeekMessage

namespace
{
    constexpr double pi = 3.14159265358979323846;
}

std::future<bool> CharacterAPI::SendTextMessage(int32_t toID, std::string message)
{
    return std::async(std::launch::async, [=, message = std::move(message)]()
                      { return logic.Send(toID, std::move(message), false); });
}

std::future<bool> TeamAPI::SendTextMessage(int32_t toID, std::string message)
{
    return std::async(std::launch::async, [=, message = std::move(message)]()
                      { return logic.Send(toID, std::move(message), false); });
}

std::future<bool> CharacterAPI::SendBinaryMessage(int32_t toID, std::string message)
{
    return std::async(std::launch::async, [=, message = std::move(message)]()
                      { return logic.Send(toID, std::move(message), true); });
}

std::future<bool> TeamAPI::SendBinaryMessage(int32_t toID, std::string message)
{
    return std::async(std::launch::async, [=, message = std::move(message)]()
                      { return logic.Send(toID, std::move(message), true); });
}

bool CharacterAPI::HaveMessage()
{
    return logic.HaveMessage();
}

bool TeamAPI::HaveMessage()
{
    return logic.HaveMessage();
}

std::pair<int32_t, std::string> CharacterAPI::GetMessage()
{
    return logic.GetMessage();
}

std::pair<int32_t, std::string> TeamAPI::GetMessage()
{
    return logic.GetMessage();
}

int32_t CharacterAPI::GetFrameCount() const
{
    return logic.GetCounter();
}

int32_t TeamAPI::GetFrameCount() const
{
    return logic.GetCounter();
}

bool CharacterAPI::Wait()
{
    return logic.GetCounter() != -1 && logic.WaitThread();
}

bool TeamAPI::Wait()
{
    return logic.GetCounter() != -1 && logic.WaitThread();
}

std::future<bool> CharacterAPI::EndAllAction()
{
    return std::async(std::launch::async, [this]()
                      { return logic.EndAllAction(); });
}

std::future<std::string> CharacterAPI::AskAI(int64_t currentGameTime, std::string prompt, std::string apiKey)
{
    return std::async(std::launch::async, [this, currentGameTime, prompt = std::move(prompt), apiKey = std::move(apiKey)]() mutable
                      { return logic.AskAI(currentGameTime, std::move(prompt), std::move(apiKey)); });
}

std::future<bool> TeamAPI::EndAllAction()
{
    return std::async(std::launch::async, [this]()
                      { return logic.EndAllAction(); });
}

std::future<std::string> TeamAPI::AskAI(int64_t currentGameTime, std::string prompt, std::string apiKey)
{
    return std::async(std::launch::async, [this, currentGameTime, prompt = std::move(prompt), apiKey = std::move(apiKey)]() mutable
                      { return logic.AskAI(currentGameTime, std::move(prompt), std::move(apiKey)); });
}

std::vector<std::shared_ptr<const THUAI9::Character>> CharacterAPI::GetCharacters() const
{
    return logic.GetCharacters();
}

std::vector<std::shared_ptr<const THUAI9::Character>> TeamAPI::GetCharacters() const
{
    return logic.GetCharacters();
}

std::vector<std::shared_ptr<const THUAI9::Character>> CharacterAPI::GetEnemyCharacters() const
{
    return logic.GetEnemyCharacters();
}

std::vector<std::shared_ptr<const THUAI9::Character>> TeamAPI::GetEnemyCharacters() const
{
    return logic.GetEnemyCharacters();
}

std::vector<std::vector<THUAI9::PlaceType>> CharacterAPI::GetFullMap() const
{
    return logic.GetFullMap();
}

std::vector<std::vector<THUAI9::PlaceType>> TeamAPI::GetFullMap() const
{
    return logic.GetFullMap();
}

THUAI9::PlaceType CharacterAPI::GetPlaceType(int32_t cellX, int32_t cellY) const
{
    return logic.GetPlaceType(cellX, cellY);
}

THUAI9::PlaceType TeamAPI::GetPlaceType(int32_t cellX, int32_t cellY) const
{
    return logic.GetPlaceType(cellX, cellY);
}

std::shared_ptr<const THUAI9::GameInfo> CharacterAPI::GetGameInfo() const
{
    return logic.GetGameInfo();
}

std::shared_ptr<const THUAI9::GameInfo> TeamAPI::GetGameInfo() const
{
    return logic.GetGameInfo();
}

std::optional<THUAI9::Resource> CharacterAPI::GetResourceState(int32_t cellX, int32_t cellY) const
{
    return logic.GetResourceState(cellX, cellY);
}

std::optional<THUAI9::ComputeCenter> CharacterAPI::GetComputeCenterState(int32_t cellX, int32_t cellY) const
{
    return logic.GetComputeCenterState(cellX, cellY);
}

std::optional<THUAI9::Market> CharacterAPI::GetMarketState(int32_t cellX, int32_t cellY) const
{
    return logic.GetMarketState(cellX, cellY);
}

std::optional<THUAI9::Factory> CharacterAPI::GetFactoryState(int32_t cellX, int32_t cellY) const
{
    return logic.GetFactoryState(cellX, cellY);
}

std::optional<THUAI9::Resource> TeamAPI::GetResourceState(int32_t cellX, int32_t cellY) const
{
    return logic.GetResourceState(cellX, cellY);
}

std::optional<THUAI9::ComputeCenter> TeamAPI::GetComputeCenterState(int32_t cellX, int32_t cellY) const
{
    return logic.GetComputeCenterState(cellX, cellY);
}

std::optional<THUAI9::Market> TeamAPI::GetMarketState(int32_t cellX, int32_t cellY) const
{
    return logic.GetMarketState(cellX, cellY);
}

std::optional<THUAI9::Factory> TeamAPI::GetFactoryState(int32_t cellX, int32_t cellY) const
{
    return logic.GetFactoryState(cellX, cellY);
}

std::vector<int64_t> CharacterAPI::GetPlayerGUIDs() const
{
    return logic.GetPlayerGUIDs();
}

std::vector<int64_t> TeamAPI::GetPlayerGUIDs() const
{
    return logic.GetPlayerGUIDs();
}

int32_t CharacterAPI::GetComputingPower() const
{
    return logic.GetComputingPower();
}

int32_t TeamAPI::GetComputingPower() const
{
    return logic.GetComputingPower();
}

int32_t CharacterAPI::GetMaterial() const
{
    return logic.GetMaterial();
}

int32_t TeamAPI::GetMaterial() const
{
    return logic.GetMaterial();
}

int32_t CharacterAPI::GetScore() const
{
    return logic.GetScore();
}

int32_t TeamAPI::GetScore() const
{
    return logic.GetScore();
}

std::shared_ptr<const THUAI9::Character> CharacterAPI::GetSelfInfo() const
{
    return logic.CharacterGetSelfInfo();
}

std::shared_ptr<const THUAI9::Team> TeamAPI::GetSelfInfo() const
{
    return logic.TeamGetSelfInfo();
}

std::future<bool> CharacterAPI::Move(int64_t moveTimeInMilliseconds, double angle)
{
    return std::async(std::launch::async, [=]()
                      { return logic.Move(moveTimeInMilliseconds, angle); });
}

std::future<bool> CharacterAPI::MoveDown(int64_t timeInMilliseconds)
{
    return Move(timeInMilliseconds, 0);
}

std::future<bool> CharacterAPI::MoveRight(int64_t timeInMilliseconds)
{
    return Move(timeInMilliseconds, pi * 0.5);
}

std::future<bool> CharacterAPI::MoveUp(int64_t timeInMilliseconds)
{
    return Move(timeInMilliseconds, pi);
}

std::future<bool> CharacterAPI::MoveLeft(int64_t timeInMilliseconds)
{
    return Move(timeInMilliseconds, pi * 1.5);
}

std::future<bool> CharacterAPI::Common_Attack(int64_t attackedPlayerID)
{
    return std::async(std::launch::async, [this, attackedPlayerID]()
                      {
                          auto self = GetSelfInfo();
                          if (!self)
                              return false;
                          return logic.Common_Attack(self->teamID, self->playerID, 0, attackedPlayerID); });
}

std::future<bool> CharacterAPI::Recover(int64_t recover)
{
    return std::async(std::launch::async, [=]()
                      { return logic.Recover(recover); });
}

std::future<bool> CharacterAPI::Harvest()
{
    return std::async(std::launch::async, [this]()
                      {
                          auto self = GetSelfInfo();
                          if (!self)
                              return false;
                          return logic.Harvest(self->playerID, self->teamID); });
}

std::future<bool> CharacterAPI::Occupy()
{
    return std::async(std::launch::async, [this]()
                      {
                          auto self = GetSelfInfo();
                          if (!self)
                              return false;
                          return logic.Occupy(self->playerID, self->teamID); });
}

std::future<bool> CharacterAPI::Load(THUAI9::GoodsType goodsType, int32_t amount)
{
    return std::async(std::launch::async, [this, goodsType, amount]()
                      {
                          auto self = GetSelfInfo();
                          if (!self)
                              return false;
                          return logic.Load(self->playerID, self->teamID, goodsType, amount); });
}

std::future<bool> CharacterAPI::Buy(THUAI9::GoodsType goodsType, int32_t amount)
{
    return std::async(std::launch::async, [this, goodsType, amount]()
                      {
                          auto self = GetSelfInfo();
                          if (!self)
                              return false;
                          return logic.Buy(self->playerID, self->teamID, goodsType, amount); });
}

std::future<bool> CharacterAPI::Sell(THUAI9::GoodsType goodsType, int32_t amount)
{
    return std::async(std::launch::async, [this, goodsType, amount]()
                      {
                          auto self = GetSelfInfo();
                          if (!self)
                              return false;
                          return logic.Sell(self->playerID, self->teamID, goodsType, amount); });
}

bool CharacterAPI::HaveView(int32_t x, int32_t y, int32_t newX, int32_t newY, int32_t viewRange, std::vector<std::vector<THUAI9::PlaceType>>& map) const
{
    return logic.HaveView(x, y, newX, newY, viewRange, map);
}

void CharacterAPI::Play(IAI& ai)
{
    ai.play(*this);
}

std::future<bool> TeamAPI::BuildCharacter(THUAI9::CharacterType characterType, int32_t playerID)
{
    return std::async(std::launch::async, [=]()
                      { return logic.BuildCharacter(characterType, playerID); });
}

std::future<bool> TeamAPI::ProduceGoods(THUAI9::GoodsType goodsType, int32_t maxProduceNum)
{
    return std::async(std::launch::async, [=]()
                      { return logic.ProduceGoods(goodsType, maxProduceNum); });
}

std::future<bool> TeamAPI::UplevelTech(THUAI9::TechType techType)
{
    return std::async(std::launch::async, [=]()
                      { return logic.UplevelTech(techType); });
}

void TeamAPI::Play(IAI& ai)
{
    ai.play(*this);
}
