#include <array>
#include <string>

#include "AI.h"

extern const bool asynchronous = false;

extern const std::array<THUAI9::CharacterType, 3> CharacterTypeDict = {
    THUAI9::CharacterType::Robot,
    THUAI9::CharacterType::Drone,
    THUAI9::CharacterType::AutonomousCar,
};

// 测试：依次创建 pid=1..7，前 6 个应成功，第 7 个应被拒绝
// CharacterCount 默认为 6，所以 pid=7 超出范围

static int s_nextPid = 1;  // 下一个要尝试创建的 playerId
static bool s_done = false;

void AI::play(ICharacterAPI& api)
{
    (void)api;
}

void AI::play(ITeamAPI& api)
{
    auto team = api.GetSelfInfo();
    if (!team || team->teamID != 1 || s_done)
        return;

    const int frame = api.GetFrameCount();
    const int pid = s_nextPid;
    const int maxPid = 7;  // 比 CharacterCount(6) 多 1，用于验证边界

    // 每帧尝试创建一个角色
    bool result = api.BuildCharacter(THUAI9::CharacterType::Robot, pid).get();

    if (pid <= 6)
    {
        // pid 1~6 合法，期望成功
        api.Print(
            "[TEST frame=" + std::to_string(frame) + "] "
                                                     "BuildCharacter(Robot, pid=" +
            std::to_string(pid) + ") "
                                  "expect true : " +
            (result ? "PASS (created)" : "FAIL (rejected — check CP or limit)")
        );
    }
    else
    {
        // pid=7 超出 CharacterCount=6，期望被拒绝
        api.Print(
            "[TEST frame=" + std::to_string(frame) + "] "
                                                     "BuildCharacter(Robot, pid=" +
            std::to_string(pid) + ") "
                                  "expect false: " +
            (result ? "*** FAIL (created, bug NOT fixed) ***" : "PASS (correctly rejected)")
        );
    }

    if (result || pid > 6)
    {
        // 成功或已到达边界测试，推进到下一个 pid
        ++s_nextPid;
    }

    if (s_nextPid > maxPid)
    {
        api.Print("[DONE  frame=" + std::to_string(frame) + "] "
                                                            "Limit test complete. Check character count on UI.");
        s_done = true;
    }
}
