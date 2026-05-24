#ifndef COMMUNICATION_H
#define COMMUNICATION_H

#include "Message2Server.pb.h"
#include "Message2Clients.pb.h"
#include "MessageType.pb.h"
#include "Services.grpc.pb.h"
#include "Services.pb.h"
#include "structures.h"

#include <grpcpp/grpcpp.h>
#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <memory>
#include <mutex>
#include <queue>
#include <string>
#include <thread>

#undef GetMessage
#undef SendMessage
#undef PeekMessage

class Logic;

class Communication
{
public:
    Communication(std::string sIP, std::string sPort);
    ~Communication() = default;

    bool TryConnection(int32_t playerID, int32_t teamID);
    protobuf::MessageToClient GetMessage2Client();
    void AddPlayer(int32_t playerID, int32_t teamID, THUAI9::CharacterType CharacterType, bool side_flag);
    bool EndAllAction(int32_t playerID, int32_t teamID);

    // Character
    bool Move(int32_t playerID, int32_t teamID, int64_t moveTimeInMilliseconds, double angle);
    bool Recover(int32_t playerID, int64_t recover, int32_t teamID);
    bool Harvest(int64_t playerID, int64_t teamID);
    bool Occupy(int64_t playerID, int64_t teamID);
    bool Load(int64_t playerID, int64_t teamID, THUAI9::GoodsType goodsType, int32_t amount);
    bool Trade(int64_t playerID, int64_t teamID, THUAI9::GoodsType goodsType, int32_t amount, bool isBuy);
    bool Common_Attack(int64_t teamID, int64_t playerID, int64_t attacked_teamID, int64_t attacked_playerID);
    bool Send(int32_t playerID, int32_t toPlayerID, int32_t teamID, std::string message, bool binary);

    // Team
    bool BuildCharacter(int32_t teamID, int32_t playerID, THUAI9::CharacterType CharacterType);
    bool ProduceGoods(int64_t teamID, THUAI9::GoodsType goodsType, int32_t maxProduceNum);
    bool UplevelTech(int64_t teamID, THUAI9::TechType techType);

    std::string AskAI(int64_t teamID, int64_t currentGameTime, const std::string& prompt, const std::string& apiKey);

private:
    std::unique_ptr<protobuf::AvailableService::Stub> THUAI9Stub;
    bool haveNewMessage = false;
    protobuf::MessageToClient message2Client;
    std::mutex mtxMessage;
    std::mutex mtxLimit;
    int32_t counter{};
    int32_t counterMove{};
    static constexpr int32_t limit = 50;
    static constexpr int32_t moveLimit = 10;
    std::condition_variable cvMessage;
};

#endif
