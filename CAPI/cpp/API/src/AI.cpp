#include <array>
#include <memory>
#include <thread>
#include <vector>

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

std::shared_ptr<const THUAI9::Character> selfinfo;
std::vector<std::vector<THUAI9::PlaceType>> mapinfo;

void AI::play(ICharacterAPI& api)
{
    selfinfo = api.GetSelfInfo();
    mapinfo = api.GetFullMap();

    if (playerID == 1)
    {
        // player1
    }
    else if (playerID == 2)
    {
        // player2
    }
    else if (playerID == 3)
    {
        // player3
    }
}

void AI::play(ITeamAPI& api)
{
    (void)api;
}
