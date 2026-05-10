#include <array>
#include <cstdint>
#include <deque>
#include <limits>
#include <memory>
#include <optional>
#include <string>
#include <utility>
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
    constexpr int64_t moveTimeMs = 120;
    constexpr THUAI9::GoodsType goodsToProduce = THUAI9::GoodsType::Food;
    constexpr int32_t goodsPerProduce = 1;
    constexpr int64_t foodCost = 3;

    enum class CharacterMission
    {
        ToResource,
        Harvesting,
        ToFactory,
    };

    [[nodiscard]] bool IsWalkable(THUAI9::PlaceType place)
    {
        return place != THUAI9::PlaceType::Barrier && place != THUAI9::PlaceType::NullPlaceType;
    }

    [[nodiscard]] int32_t CellX(const THUAI9::Character& self)
    {
        return IAPI::GridToCell(self.x);
    }

    [[nodiscard]] int32_t CellY(const THUAI9::Character& self)
    {
        return IAPI::GridToCell(self.y);
    }

    [[nodiscard]] bool AtCell(const THUAI9::Character& self, const THUAI9::cellxy_t& cell)
    {
        return CellX(self) == cell.first && CellY(self) == cell.second;
    }

    [[nodiscard]] int64_t DistanceSquared(const THUAI9::cellxy_t& a, const THUAI9::cellxy_t& b)
    {
        const int64_t dx = static_cast<int64_t>(a.first) - b.first;
        const int64_t dy = static_cast<int64_t>(a.second) - b.second;
        return dx * dx + dy * dy;
    }

    template<class TAPI>
    [[nodiscard]] std::optional<THUAI9::cellxy_t> FindTeamFactoryCell(TAPI& api, int64_t teamID)
    {
        const auto map = api.GetFullMap();
        if (map.empty() || map.front().empty())
            return std::nullopt;

        for (int32_t x = 0; x < static_cast<int32_t>(map.size()); ++x)
        {
            for (int32_t y = 0; y < static_cast<int32_t>(map[x].size()); ++y)
            {
                auto factory = api.GetFactoryState(x, y);
                if (factory.has_value() && factory->teamID == teamID)
                    return THUAI9::cellxy_t{x, y};
            }
        }
        return std::nullopt;
    }

    template<class TAPI>
    [[nodiscard]] std::optional<THUAI9::Factory> FindTeamFactoryState(TAPI& api, int64_t teamID)
    {
        const auto map = api.GetFullMap();
        if (map.empty() || map.front().empty())
            return std::nullopt;

        for (int32_t x = 0; x < static_cast<int32_t>(map.size()); ++x)
        {
            for (int32_t y = 0; y < static_cast<int32_t>(map[x].size()); ++y)
            {
                auto factory = api.GetFactoryState(x, y);
                if (factory.has_value() && factory->teamID == teamID)
                    return factory;
            }
        }
        return std::nullopt;
    }

    template<class TAPI>
    [[nodiscard]] std::optional<THUAI9::cellxy_t> FindNearestHarvestableResource(
        TAPI& api,
        const THUAI9::cellxy_t& from
    )
    {
        const auto map = api.GetFullMap();
        if (map.empty() || map.front().empty())
            return std::nullopt;

        std::optional<THUAI9::cellxy_t> bestCell;
        int64_t bestDistance = std::numeric_limits<int64_t>::max();

        for (int32_t x = 0; x < static_cast<int32_t>(map.size()); ++x)
        {
            for (int32_t y = 0; y < static_cast<int32_t>(map[x].size()); ++y)
            {
                auto resource = api.GetResourceState(x, y);
                if (!resource.has_value())
                    continue;
                if (resource->state != THUAI9::ResourceState::Harvestable)
                    continue;

                const THUAI9::cellxy_t current{x, y};
                const int64_t distance = DistanceSquared(from, current);
                if (!bestCell.has_value() || distance < bestDistance)
                {
                    bestCell = current;
                    bestDistance = distance;
                }
            }
        }

        return bestCell;
    }

    [[nodiscard]] std::optional<THUAI9::cellxy_t> FindNextStep(
        const std::vector<std::vector<THUAI9::PlaceType>>& map,
        const THUAI9::cellxy_t& start,
        const THUAI9::cellxy_t& goal
    )
    {
        if (map.empty() || map.front().empty())
            return std::nullopt;
        if (start == goal)
            return goal;

        const int32_t rows = static_cast<int32_t>(map.size());
        const int32_t cols = static_cast<int32_t>(map.front().size());
        const std::array<THUAI9::cellxy_t, 4> dirs = {
            THUAI9::cellxy_t{1, 0},
            THUAI9::cellxy_t{-1, 0},
            THUAI9::cellxy_t{0, 1},
            THUAI9::cellxy_t{0, -1},
        };

        std::vector<std::vector<bool>> visited(rows, std::vector<bool>(cols, false));
        std::vector<std::vector<THUAI9::cellxy_t>> parent(
            rows,
            std::vector<THUAI9::cellxy_t>(cols, THUAI9::cellxy_t{-1, -1})
        );

        std::deque<THUAI9::cellxy_t> queue;
        queue.push_back(start);
        visited[start.first][start.second] = true;

        while (!queue.empty())
        {
            const auto current = queue.front();
            queue.pop_front();

            for (const auto& dir : dirs)
            {
                const int32_t nx = current.first + dir.first;
                const int32_t ny = current.second + dir.second;
                if (nx < 0 || ny < 0 || nx >= rows || ny >= cols)
                    continue;
                if (visited[nx][ny] || !IsWalkable(map[nx][ny]))
                    continue;

                visited[nx][ny] = true;
                parent[nx][ny] = current;
                if (THUAI9::cellxy_t{nx, ny} == goal)
                {
                    THUAI9::cellxy_t step = goal;
                    while (parent[step.first][step.second] != start)
                    {
                        step = parent[step.first][step.second];
                        if (step.first < 0 || step.second < 0)
                            return std::nullopt;
                    }
                    return step;
                }

                queue.push_back({nx, ny});
            }
        }

        return std::nullopt;
    }

    void MoveOneStep(ICharacterAPI& api, const THUAI9::cellxy_t& from, const THUAI9::cellxy_t& to)
    {
        if (to.first > from.first)
            (void)api.MoveDown(moveTimeMs).get();
        else if (to.first < from.first)
            (void)api.MoveUp(moveTimeMs).get();
        else if (to.second > from.second)
            (void)api.MoveRight(moveTimeMs).get();
        else if (to.second < from.second)
            (void)api.MoveLeft(moveTimeMs).get();
    }
}  // namespace

void AI::play(ICharacterAPI& api)
{
    auto self = api.GetSelfInfo();
    if (!self)
        return;

    static bool printedInit = false;
    static CharacterMission mission = CharacterMission::ToResource;
    static std::optional<THUAI9::cellxy_t> homeFactory;
    static std::optional<THUAI9::cellxy_t> targetResource;

    const auto map = api.GetFullMap();
    if (map.empty() || map.front().empty())
        return;

    if (!homeFactory.has_value())
        homeFactory = FindTeamFactoryCell(api, self->teamID);

    const THUAI9::cellxy_t selfCell{CellX(*self), CellY(*self)};

    if (!printedInit)
    {
        api.Print(
            "character " + std::to_string(playerID) +
            " start at (" + std::to_string(selfCell.first) + ", " + std::to_string(selfCell.second) + ")"
        );
        if (homeFactory.has_value())
        {
            api.Print(
                "home factory at (" +
                std::to_string(homeFactory->first) + ", " +
                std::to_string(homeFactory->second) + ")"
            );
        }
        printedInit = true;
    }

    if (mission == CharacterMission::Harvesting)
    {
        if (!targetResource.has_value())
        {
            mission = CharacterMission::ToFactory;
        }
        else
        {
            auto resource = api.GetResourceState(targetResource->first, targetResource->second);
            if (!resource.has_value() || resource->state == THUAI9::ResourceState::Harvested)
            {
                api.Print("resource depleted, return to factory");
                targetResource.reset();
                mission = CharacterMission::ToFactory;
            }
            else
            {
                return;
            }
        }
    }

    if (mission == CharacterMission::ToResource)
    {
        if (!targetResource.has_value())
        {
            targetResource = FindNearestHarvestableResource(api, selfCell);
            if (targetResource.has_value())
            {
                api.Print(
                    "target resource at (" +
                    std::to_string(targetResource->first) + ", " +
                    std::to_string(targetResource->second) + ")"
                );
            }
        }

        if (!targetResource.has_value())
        {
            if (homeFactory.has_value() && !AtCell(*self, *homeFactory))
            {
                const auto nextStep = FindNextStep(map, selfCell, *homeFactory);
                if (nextStep.has_value())
                    MoveOneStep(api, selfCell, *nextStep);
            }
            return;
        }

        auto resource = api.GetResourceState(targetResource->first, targetResource->second);
        if (!resource.has_value() || resource->state == THUAI9::ResourceState::Harvested)
        {
            targetResource.reset();
            return;
        }

        if (AtCell(*self, *targetResource))
        {
            const bool ok = api.Harvest().get();
            api.Print("Harvest -> " + std::string(ok ? "true" : "false"));
            if (ok)
                mission = CharacterMission::Harvesting;
            else
                targetResource.reset();
            return;
        }

        const auto nextStep = FindNextStep(map, selfCell, *targetResource);
        if (nextStep.has_value())
            MoveOneStep(api, selfCell, *nextStep);
        return;
    }

    if (!homeFactory.has_value())
    {
        mission = CharacterMission::ToResource;
        return;
    }

    if (AtCell(*self, *homeFactory))
    {
        api.Print("arrived at factory, prepare next harvest");
        mission = CharacterMission::ToResource;
        targetResource.reset();
        return;
    }

    const auto nextStep = FindNextStep(map, selfCell, *homeFactory);
    if (nextStep.has_value())
        MoveOneStep(api, selfCell, *nextStep);
}

void AI::play(ITeamAPI& api)
{
    auto team = api.GetSelfInfo();
    if (!team)
        return;

    static bool printedInit = false;
    static std::array<bool, 3> built{};

    if (!printedInit)
    {
        api.Print("team " + std::to_string(team->teamID) + " start");
        printedInit = true;
    }

    for (int32_t i = 0; i < 3; ++i)
    {
        if (built[i])
            continue;

        const bool ok = api.BuildCharacter(CharacterTypeDict[i], i + 1).get();
        api.Print(
            "BuildCharacter player " + std::to_string(i + 1) +
            " -> " + std::string(ok ? "true" : "false")
        );
        if (ok)
            built[i] = true;
        break;
    }

    const auto factory = FindTeamFactoryState(api, team->teamID);
    if (!factory.has_value())
        return;

    if (factory->source >= foodCost)
    {
        const bool ok = api.ProduceGoods(goodsToProduce, goodsPerProduce).get();
        if (ok)
        {
            api.Print(
                "ProduceGoods Food -> true, source=" + std::to_string(factory->source)
            );
        }
    }
}
