#include <array>
#include <memory>
#include <vector>

#include "AI.h"
#include "constants.h"

extern const bool asynchronous = false;

extern const std::array<THUAI9::CharacterType, 3> CharacterTypeDict = {
    THUAI9::CharacterType::Robot,
    THUAI9::CharacterType::Drone,
    THUAI9::CharacterType::AutonomousCar,
};

namespace
{
    std::array<bool, 3> teamCharacterBuilt{};

    void Patrol(ICharacterAPI& api, int32_t playerID)
    {
        auto self = api.GetSelfInfo();
        if (!self)
            return;

        if (self->characterActiveState == THUAI9::CharacterState::Deceased)
            return;

        constexpr int64_t moveTime = 200;
        const int32_t phase = (api.GetFrameCount() + playerID - 1) % 4;

        switch (phase)
        {
            case 0:
                (void)api.MoveRight(moveTime).get();
                break;
            case 1:
                (void)api.MoveDown(moveTime).get();
                break;
            case 2:
                (void)api.MoveLeft(moveTime).get();
                break;
            default:
                (void)api.MoveUp(moveTime).get();
                break;
        }
    }
}

std::shared_ptr<const THUAI9::Character> selfinfo;
std::vector<std::vector<THUAI9::PlaceType>> mapinfo;

void AI::play(ICharacterAPI& api)
{
    selfinfo = api.GetSelfInfo();
    mapinfo = api.GetFullMap();
    Patrol(api, playerID);
}

void AI::play(ITeamAPI& api)
{
    for (int32_t i = 1; i <= 3; ++i)
    {
        if (teamCharacterBuilt[i - 1])
            continue;

        if (api.BuildCharacter(CharacterTypeDict[i - 1], i).get())
            teamCharacterBuilt[i - 1] = true;
    }
}
