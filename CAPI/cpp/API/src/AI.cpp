#include <array>
#include <string>

#include "AI.h"

extern const bool asynchronous = false;

extern const std::array<THUAI9::CharacterType, 3> CharacterTypeDict = {
    THUAI9::CharacterType::Robot,
    THUAI9::CharacterType::Drone,
    THUAI9::CharacterType::AutonomousCar,
};

// Test plan (Team 1 home client only):
//  Step 1: Build(Robot, pid=1)  -> expect true  [first create, count=1]
//  Step 2: Build(Robot, pid=1)  -> expect false [BUG FIX 2: duplicate playerId rejected]
//  Step 3: Build(Robot, pid=2)  -> expect true  [count=2]
//  Step 4: Build(Robot, pid=3)  -> expect true  [count=3]
//  Step 5: Build(Robot, pid=4)  -> expect true  [count=4]
//  Step 6: Build(Robot, pid=5)  -> expect true  [count=5]
//  Step 7: Build(Robot, pid=6)  -> expect true  [count=6, team full]
//  Step 8: Build(Robot, pid=7)  -> expect false [BUG FIX 1: count >= MaxCharactersPerTeam]

struct TestStep
{
    int32_t pid;
    bool expectSuccess;
    const char* label;
};

static const TestStep steps[] = {
    {1, true, "setup  Build(Robot,pid=1) first time            "},
    {1, false, "BUG2   Build(Robot,pid=1) duplicate -> exp false "},
    {2, true, "normal Build(Robot,pid=2)                        "},
    {3, true, "normal Build(Robot,pid=3)                        "},
    {4, true, "normal Build(Robot,pid=4)                        "},
    {5, true, "normal Build(Robot,pid=5)                        "},
    {6, true, "normal Build(Robot,pid=6) fills team             "},
    {7, false, "BUG1   Build(Robot,pid=7) over limit -> exp false"},
};

static int s_step = 0;
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

    const int numSteps = static_cast<int>(sizeof(steps) / sizeof(steps[0]));
    if (s_step >= numSteps)
    {
        api.Print("[DONE] All test steps completed. Check verdicts above.");
        s_done = true;
        return;
    }

    const TestStep& step = steps[s_step];
    const int frame = api.GetFrameCount();

    bool result = api.BuildCharacter(THUAI9::CharacterType::Robot, step.pid).get();

    bool pass = (result == step.expectSuccess);
    std::string verdict = pass ? "PASS" : "*** FAIL ***";

    api.Print(
        "[TEST frame=" + std::to_string(frame) +
        " step=" + std::to_string(s_step + 1) + "/8] " +
        step.label +
        " got=" + (result ? "true " : "false") +
        " " + verdict
    );

    ++s_step;

    if (s_step >= numSteps)
    {
        api.Print(
            "[DONE frame=" + std::to_string(frame) +
            "] All 8 steps done. BUG1 and BUG2 checks complete."
        );
        s_done = true;
    }
}
