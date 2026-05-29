#include "Communication.h"

#include "structures.h"
#include "utils.hpp"

#include <iostream>
#include <memory>
#include <mutex>
#include <thread>

#undef GetMessage
#undef SendMessage
#undef PeekMessage

using grpc::ClientContext;

namespace
{
    [[nodiscard]] bool ConsumeActionQuota(std::mutex& mtxLimit, int32_t& counter, int32_t& counterMove, int32_t limit, int32_t moveLimit, bool countAsMove)
    {
        std::lock_guard<std::mutex> lock(mtxLimit);
        if (counter >= limit)
        {
            std::cerr << "[CAPI] Action quota exhausted: " << counter << "/" << limit << " (total limit)" << std::endl;
            return false;
        }
        if (countAsMove && counterMove >= moveLimit)
        {
            std::cerr << "[CAPI] Move quota exhausted: " << counterMove << "/" << moveLimit << " (move limit)" << std::endl;
            return false;
        }
        ++counter;
        if (countAsMove)
            ++counterMove;
        return true;
    }

    [[nodiscard]] bool ConsumeQuota(std::mutex& mtxLimit, int32_t& counter, int32_t limit)
    {
        std::lock_guard<std::mutex> lock(mtxLimit);
        if (counter >= limit)
        {
            std::cerr << "[CAPI] Quota exhausted: " << counter << "/" << limit << std::endl;
            return false;
        }
        ++counter;
        return true;
    }
}  // namespace

Communication::Communication(std::string sIP, std::string sPort)
{
    std::string aim = sIP + ':' + sPort;
    auto channel = grpc::CreateChannel(aim, grpc::InsecureChannelCredentials());
    THUAI9Stub = protobuf::AvailableService::NewStub(channel);
}

bool Communication::Move(int32_t characterID, int32_t teamID, int64_t moveTimeInMilliseconds, double angle)
{
    if (!ConsumeActionQuota(mtxLimit, counter, counterMove, limit, moveLimit, true))
        return false;

    protobuf::MoveRes moveResult;
    ClientContext context;
    auto request = THUAI9Proto::THUAI92ProtobufMoveMsg(teamID, characterID, moveTimeInMilliseconds, angle);
    auto status = THUAI9Stub->Move(&context, request, &moveResult);
    return status.ok() && moveResult.act_success();
}

bool Communication::Send(int32_t playerID, int32_t toPlayerID, int32_t teamID, std::string message, bool binary)
{
    if (!ConsumeQuota(mtxLimit, counter, limit))
        return false;

    protobuf::BoolRes sendMessageResult;
    ClientContext context;
    auto request = THUAI9Proto::THUAI92ProtobufSendMsg(playerID, toPlayerID, teamID, std::move(message), binary);
    auto status = THUAI9Stub->Send(&context, request, &sendMessageResult);
    return status.ok() && sendMessageResult.act_success();
}

bool Communication::EndAllAction(int32_t playerID, int32_t teamID)
{
    if (!ConsumeActionQuota(mtxLimit, counter, counterMove, limit, moveLimit, true))
        return false;

    protobuf::BoolRes endAllActionsResult;
    ClientContext context;
    auto request = THUAI9Proto::THUAI92ProtobufIDMsg(playerID, teamID);
    auto status = THUAI9Stub->EndAllAction(&context, request, &endAllActionsResult);
    return status.ok() && endAllActionsResult.act_success();
}

bool Communication::Recover(int32_t playerID, int64_t recover, int32_t teamID)
{
    if (!ConsumeActionQuota(mtxLimit, counter, counterMove, limit, moveLimit, true))
        return false;

    protobuf::BoolRes recoverResult;
    ClientContext context;
    auto request = THUAI9Proto::THUAI92ProtobufRecoverMsg(playerID, recover, teamID);
    auto status = THUAI9Stub->Recover(&context, request, &recoverResult);
    return status.ok() && recoverResult.act_success();
}

bool Communication::Harvest(int64_t playerID, int64_t teamID)
{
    if (!ConsumeActionQuota(mtxLimit, counter, counterMove, limit, moveLimit, true))
        return false;

    protobuf::BoolRes harvestResult;
    ClientContext context;
    auto request = THUAI9Proto::THUAI92ProtobufHarvestMsg(playerID, teamID);
    auto status = THUAI9Stub->Harvest(&context, request, &harvestResult);
    return status.ok() && harvestResult.act_success();
}

bool Communication::Occupy(int64_t playerID, int64_t teamID)
{
    if (!ConsumeActionQuota(mtxLimit, counter, counterMove, limit, moveLimit, true))
        return false;

    protobuf::BoolRes occupyResult;
    ClientContext context;
    auto request = THUAI9Proto::THUAI92ProtobufOccupyMsg(playerID, teamID);
    auto status = THUAI9Stub->Occupy(&context, request, &occupyResult);
    return status.ok() && occupyResult.act_success();
}

bool Communication::Load(int64_t playerID, int64_t teamID, THUAI9::GoodsType goodsType, int32_t amount)
{
    if (!ConsumeActionQuota(mtxLimit, counter, counterMove, limit, moveLimit, true))
        return false;

    protobuf::BoolRes loadResult;
    ClientContext context;
    auto request = THUAI9Proto::THUAI92ProtobufLoadMsg(playerID, teamID, goodsType, amount);
    auto status = THUAI9Stub->Load(&context, request, &loadResult);
    return status.ok() && loadResult.act_success();
}

bool Communication::Trade(int64_t playerID, int64_t teamID, THUAI9::GoodsType goodsType, int32_t amount, bool isBuy)
{
    if (!ConsumeActionQuota(mtxLimit, counter, counterMove, limit, moveLimit, true))
        return false;

    protobuf::BoolRes tradeResult;
    ClientContext context;
    auto request = THUAI9Proto::THUAI92ProtobufTradeMsg(playerID, teamID, goodsType, amount, isBuy);
    auto status = THUAI9Stub->Trade(&context, request, &tradeResult);
    return status.ok() && tradeResult.act_success();
}

bool Communication::Common_Attack(int64_t teamID, int64_t playerID, int64_t attacked_teamID, int64_t attacked_playerID)
{
    if (!ConsumeQuota(mtxLimit, counter, limit))
        return false;

    protobuf::BoolRes commonAttackResult;
    ClientContext context;
    auto request = THUAI9Proto::THUAI92ProtobufAttackMsg(teamID, playerID, attacked_teamID, attacked_playerID);
    auto status = THUAI9Stub->Attack(&context, request, &commonAttackResult);
    return status.ok() && commonAttackResult.act_success();
}

bool Communication::BuildCharacter(int32_t teamID, int32_t playerID, THUAI9::CharacterType charactertype)
{
    if (!ConsumeQuota(mtxLimit, counter, limit))
        return false;

    protobuf::BoolRes reply;
    ClientContext context;
    auto request = THUAI9Proto::THUAI92ProtobufCreateCharacterMsg(teamID, playerID, charactertype);
    auto status = THUAI9Stub->CreateCharacter(&context, request, &reply);
    return status.ok() && reply.act_success();
}

bool Communication::ProduceGoods(int64_t teamID, THUAI9::GoodsType goodsType, int32_t maxProduceNum)
{
    if (!ConsumeQuota(mtxLimit, counter, limit))
        return false;

    protobuf::BoolRes produceResult;
    ClientContext context;
    auto request = THUAI9Proto::THUAI92ProtobufProduceGoodsMsg(teamID, goodsType, maxProduceNum);
    auto status = THUAI9Stub->Produce(&context, request, &produceResult);
    return status.ok() && produceResult.act_success();
}

bool Communication::UplevelTech(int64_t teamID, THUAI9::TechType techType)
{
    if (!ConsumeQuota(mtxLimit, counter, limit))
        return false;

    protobuf::BoolRes result;
    ClientContext context;
    auto request = THUAI9Proto::THUAI92ProtobufUplevelTechMsg(teamID, techType);
    auto status = THUAI9Stub->UplevelTech(&context, request, &result);
    return status.ok() && result.act_success();
}

std::string Communication::AskAI(int64_t teamID, int64_t currentGameTime, const std::string& prompt, const std::string& apiKey)
{
    if (!ConsumeQuota(mtxLimit, counter, limit))
        return {};

    protobuf::StrategicAIResponse reply;
    ClientContext context;
    protobuf::StrategicAIRequest request;
    request.set_team_id(teamID);
    request.set_current_game_time(currentGameTime);
    request.set_prompt(apiKey + "||" + prompt);
    auto status = THUAI9Stub->AskAI(&context, request, &reply);
    if (status.ok() && reply.act_success())
        return reply.answer();
    return {};
}

bool Communication::TryConnection(int32_t playerID, int32_t teamID)
{
    protobuf::BoolRes reply;
    ClientContext context;
    auto request = THUAI9Proto::THUAI92ProtobufIDMsg(playerID, teamID);
    auto status = THUAI9Stub->TryConnection(&context, request, &reply);
    return status.ok() && reply.act_success();
}

void Communication::AddPlayer(int32_t playerID, int32_t teamID, THUAI9::CharacterType charactertype, bool side_flag)
{
    (void)charactertype;
    auto tMessage = [=]()
    {
        try
        {
            auto playerMsg = THUAI9Proto::THUAI92ProtobufRegisterFactoryMsg(playerID, teamID, side_flag);
            grpc::ClientContext context;
            auto MessageReader = THUAI9Stub->RegisterFactory(&context, playerMsg);

            protobuf::MessageToClient buffer2Client;
            counter = 0;
            counterMove = 0;

            while (MessageReader->Read(&buffer2Client))
            {
                {
                    std::lock_guard<std::mutex> lock(mtxMessage);
                    message2Client = std::move(buffer2Client);
                    haveNewMessage = true;
                    {
                        std::lock_guard<std::mutex> limitLock(mtxLimit);
                        counter = 0;
                        counterMove = 0;
                    }
                }
                cvMessage.notify_one();
            }
        }
        catch (const std::exception& e)
        {
            // 记录异常但不让线程 crash
        }
        catch (...)
        {
        }
        // 无论什么原因退出，都要通知主循环以免永久阻塞
        {
            std::lock_guard<std::mutex> lock(mtxMessage);
            haveNewMessage = true;
        }
        cvMessage.notify_one();
    };
    std::thread(tMessage).detach();
}

protobuf::MessageToClient Communication::GetMessage2Client()
{
    std::unique_lock<std::mutex> lock(mtxMessage);
    cvMessage.wait(lock, [this]()
                   { return haveNewMessage; });
    haveNewMessage = false;
    return message2Client;
}
