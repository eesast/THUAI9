#include <vector>
#include <thread>
#include <array>
#include <map>
#include <memory>
#include "AI.h"
#include "constants.h"
// 注意不要使用conio.h，Windows.h等非标准库
// 为假则play()期间确保游戏状态不更新，为真则只保证游戏状态在调用相关方法时不更新，大致一帧更新一次
extern const bool asynchronous = false;

// 选手需要依次将player1到player3的角色类型在这里定义
extern const std::array<THUAI9::CharacterType, 3> CharacterTypeDict = {
    THUAI9::CharacterType::Robot,
    THUAI9::CharacterType::Drone,
    THUAI9::CharacterType::AutonomousCar,
};

// 为所有AI定义提供的默认参数，在选手实现AI之前是按照这个默认参数运行的。

const std::vector<THUAI9::CharacterType> CharacterTypeDict = {
    THUAI9::CharacterType::Robot,
    THUAI9::CharacterType::Drone,
    THUAI9::CharacterType::AutonomousCar,
};

std::shared_ptr<const THUAI9::Character> selfinfo;
std::vector<std::vector<THUAI9::PlaceType>> mapinfo;

void AI::play(ICharacterAPI& api)
{
    selfinfo = api.GetSelfInfo();
    mapinfo = api.GetFullMap();

    if (this->playerID == 1)
    {
        // player1的操作
    }
    else if (this->playerID == 2)
    {
        // player2的操作
    }
    else if (this->playerID == 3)
    {
        // player3的操作
    }
}

void AI::play(ITeamAPI& api)  // 默认team playerID 为0
{
    // player0的操作
}
