#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <map>
#include <memory>
#include <optional>
#include <queue>
#include <string>
#include <unordered_set>
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
    constexpr int32_t kGridPerCell = 1000;
    constexpr int32_t kCellCenter = 500;
    constexpr int64_t kMoveTimeMs = 200;
    constexpr int32_t kCenterTolerance = 80;
    using Cell = std::pair<int32_t, int32_t>;

    constexpr int32_t kPlayerIdCar = 1;
    constexpr int32_t kPlayerIdDrone = 2;

    constexpr std::array<Cell, 4> kDirs = {{
        {1, 0},
        {-1, 0},
        {0, 1},
        {0, -1},
    }};

    // ---- Hash for Cell (for unordered_set) ----
    struct CellHash
    {
        size_t operator()(const Cell& c) const noexcept
        {
            return static_cast<size_t>(c.first) * 137 + static_cast<size_t>(c.second);
        }
    };
    using CellSet = std::unordered_set<Cell, CellHash>;

    // ---- Phases ----
    enum class Phase
    {
        SeekResource,
        ToResource,
        ToFactory,
        SeekCenter,
        ToCenter,
        SeekEnemy,
        ToEnemy,
    };

    struct PathTarget
    {
        Cell object{-1, -1};
        Cell approach{-1, -1};
        std::vector<Cell> path;
        int64_t targetPlayerID = 0;  // for enemy attacks
    };

    struct CharState
    {
        Phase phase = Phase::SeekResource;
        PathTarget target{};
        size_t pathIndex = 1;
    };

    struct TeamState
    {
        bool carBuilt = false;
        bool droneBuilt = false;
        int32_t lastProduceFrame = -1000;
    };

    std::map<int64_t, CharState> g_charStates;
    std::map<int64_t, TeamState> g_teamStates;

    // ---- Coordinate helpers ----
    [[nodiscard]] int32_t CellToGrid(int32_t cell) noexcept
    {
        return cell * kGridPerCell + kCellCenter;
    }

    [[nodiscard]] int32_t GridToCell(int32_t grid) noexcept
    {
        return grid / kGridPerCell;
    }

    [[nodiscard]] std::string CellText(Cell c)
    {
        return "(" + std::to_string(c.first) + "," + std::to_string(c.second) + ")";
    }

    [[nodiscard]] bool InBounds(const std::vector<std::vector<THUAI9::PlaceType>>& map, int32_t x, int32_t y)
    {
        return !map.empty() && !map.front().empty() &&
               x >= 0 && y >= 0 &&
               x < static_cast<int32_t>(map.size()) &&
               y < static_cast<int32_t>(map.front().size());
    }

    [[nodiscard]] bool Walkable(THUAI9::PlaceType pt)
    {
        return pt == THUAI9::PlaceType::Space || pt == THUAI9::PlaceType::Bush;
    }

    [[nodiscard]] bool InInteractRange(Cell a, Cell b)
    {
        return std::abs(a.first - b.first) <= 1 && std::abs(a.second - b.second) <= 1;
    }

    [[nodiscard]] bool NearCellCenter(const THUAI9::Character& self)
    {
        const Cell cur{GridToCell(self.x), GridToCell(self.y)};
        return std::abs(self.x - CellToGrid(cur.first)) <= kCenterTolerance &&
               std::abs(self.y - CellToGrid(cur.second)) <= kCenterTolerance;
    }

    [[nodiscard]] bool IsBusy(const THUAI9::Character& self)
    {
        auto s = self.characterActiveState;
        return s == THUAI9::CharacterState::Moving ||
               s == THUAI9::CharacterState::KnockedBack ||
               s == THUAI9::CharacterState::Trading ||
               s == THUAI9::CharacterState::Attacking ||
               s == THUAI9::CharacterState::Harvesting ||
               s == THUAI9::CharacterState::Ocuppying;
    }

    // ---- Build set of teammate-occupied cells (to avoid in pathfinding) ----
    [[nodiscard]] CellSet GetTeammateCells(ICharacterAPI& api, const THUAI9::Character& self)
    {
        CellSet occupied;
        auto chars = api.GetCharacters();
        for (const auto& ch : chars)
        {
            if (!ch || ch->playerID == self.playerID)
                continue;
            if (ch->characterActiveState == THUAI9::CharacterState::Deceased)
                continue;
            occupied.insert({GridToCell(ch->x), GridToCell(ch->y)});
        }
        return occupied;
    }

    // ---- Move to cell center ----
    bool MoveToCellCenter(ICharacterAPI& api, const THUAI9::Character& self)
    {
        const Cell cur{GridToCell(self.x), GridToCell(self.y)};
        const int32_t centerX = CellToGrid(cur.first);
        const int32_t centerY = CellToGrid(cur.second);

        if (std::abs(self.x - centerX) <= kCenterTolerance &&
            std::abs(self.y - centerY) <= kCenterTolerance)
            return true;

        const int32_t dx = centerX - self.x;
        const int32_t dy = centerY - self.y;
        const int64_t moveMs = std::max<int64_t>(1, (std::max(std::abs(dx), std::abs(dy)) + 4) / 5);

        if (std::abs(dx) >= std::abs(dy))
        {
            if (dx > 0)
                (void)api.MoveDown(moveMs).get();
            else
                (void)api.MoveUp(moveMs).get();
        }
        else
        {
            if (dy > 0)
                (void)api.MoveRight(moveMs).get();
            else
                (void)api.MoveLeft(moveMs).get();
        }
        return false;
    }

    // ---- BFS that avoids blocked cells (teammates) ----
    [[nodiscard]] std::vector<Cell> Bfs(
        const std::vector<std::vector<THUAI9::PlaceType>>& map,
        Cell start,
        Cell target,
        const CellSet& blocked
    )
    {
        if (!InBounds(map, start.first, start.second) ||
            !InBounds(map, target.first, target.second))
            return {};
        if (!Walkable(map[start.first][start.second]))
            return {};
        if (start == target)
            return {start};

        const int32_t cols = static_cast<int32_t>(map.size());
        const int32_t rows = static_cast<int32_t>(map.front().size());

        std::vector<std::vector<char>> vis(cols, std::vector<char>(rows, 0));
        std::vector<std::vector<Cell>> pre(cols, std::vector<Cell>(rows, Cell{-1, -1}));
        std::queue<Cell> q;

        vis[start.first][start.second] = 1;
        q.push(start);

        bool found = false;
        while (!q.empty())
        {
            auto [x, y] = q.front();
            q.pop();

            if (x == target.first && y == target.second)
            {
                found = true;
                break;
            }

            for (const auto& [dx, dy] : kDirs)
            {
                const int32_t nx = x + dx;
                const int32_t ny = y + dy;
                if (!InBounds(map, nx, ny))
                    continue;
                if (vis[nx][ny])
                    continue;
                // Allow target cell even if not Walkable (e.g. resource, center, enemy)
                bool isTarget = (nx == target.first && ny == target.second);
                if (!isTarget)
                {
                    if (!Walkable(map[nx][ny]))
                        continue;
                    // Avoid teammate cells
                    if (blocked.count({nx, ny}))
                        continue;
                }

                vis[nx][ny] = 1;
                pre[nx][ny] = {x, y};
                q.push({nx, ny});
            }
        }

        if (!found)
            return {};

        std::vector<Cell> path;
        for (Cell cur = target; cur != start; cur = pre[cur.first][cur.second])
            path.push_back(cur);
        path.push_back(start);
        std::reverse(path.begin(), path.end());
        return path;
    }

    // ---- Find path to an adjacent walkable cell near a target ----
    [[nodiscard]] std::optional<PathTarget> FindPathToCell(
        ICharacterAPI& api, const THUAI9::Character& self, Cell targetCell
    )
    {
        const auto map = api.GetFullMap();
        if (map.empty() || map.front().empty())
            return std::nullopt;

        const Cell start{GridToCell(self.x), GridToCell(self.y)};
        auto blocked = GetTeammateCells(api, self);
        std::optional<PathTarget> best;

        for (const auto& [dx, dy] : kDirs)
        {
            const int32_t nx = targetCell.first + dx;
            const int32_t ny = targetCell.second + dy;
            if (!InBounds(map, nx, ny))
                continue;
            if (!Walkable(map[nx][ny]))
                continue;
            if (blocked.count({nx, ny}) && Cell{nx, ny} != start)
                continue;

            auto path = Bfs(map, start, {nx, ny}, blocked);
            if (path.empty())
                continue;
            if (!best.has_value() || path.size() < best->path.size())
                best = PathTarget{targetCell, {nx, ny}, std::move(path)};
        }
        return best;
    }

    // ---- Move along precomputed path, one straight segment per call ----
    [[nodiscard]] bool MoveAlongPath(ICharacterAPI& api, const THUAI9::Character& self, CharState& s)
    {
        if (IsBusy(self))
            return false;

        if (s.target.path.empty() || s.pathIndex >= s.target.path.size())
            return true;

        if (!NearCellCenter(self))
            return MoveToCellCenter(api, self);

        const Cell cur{GridToCell(self.x), GridToCell(self.y)};

        // Check if a teammate is blocking the next cell — if so, re-plan
        if (s.pathIndex < s.target.path.size())
        {
            auto nextCell = s.target.path[s.pathIndex];
            auto teammates = api.GetCharacters();
            for (const auto& ch : teammates)
            {
                if (!ch || ch->playerID == self.playerID)
                    continue;
                if (ch->characterActiveState == THUAI9::CharacterState::Deceased)
                    continue;
                Cell tc{GridToCell(ch->x), GridToCell(ch->y)};
                if (tc == nextCell)
                {
                    // Teammate in the way, abort path and replan
                    s.phase = Phase::SeekResource;
                    s.target = {};
                    return false;
                }
            }
        }

        // Advance past cells we're already on
        while (s.pathIndex < s.target.path.size() && s.target.path[s.pathIndex] == cur)
            ++s.pathIndex;
        if (s.pathIndex >= s.target.path.size())
            return true;

        const auto [dx, dy] = Cell{
            s.target.path[s.pathIndex].first - cur.first,
            s.target.path[s.pathIndex].second - cur.second,
        };
        if (std::abs(dx) + std::abs(dy) != 1)
        {
            s.phase = Phase::SeekResource;
            s.target = {};
            return false;
        }

        // Count consecutive cells in same direction for a multi-step move
        size_t runLen = 1;
        while (s.pathIndex + runLen < s.target.path.size())
        {
            const auto next = s.target.path[s.pathIndex + runLen];
            const auto prev = s.target.path[s.pathIndex + runLen - 1];
            if (next.first - prev.first != dx || next.second - prev.second != dy)
                break;
            ++runLen;
        }

        const int64_t moveMs = static_cast<int64_t>(runLen) * kMoveTimeMs;
        if (dx == 1)
            (void)api.MoveDown(moveMs).get();
        else if (dx == -1)
            (void)api.MoveUp(moveMs).get();
        else if (dy == 1)
            (void)api.MoveRight(moveMs).get();
        else if (dy == -1)
            (void)api.MoveLeft(moveMs).get();
        s.pathIndex += runLen;
        return false;
    }

    // ---- Find team factory ----
    template<class TAPI>
    [[nodiscard]] std::optional<Cell> FindTeamFactoryCell(TAPI& api, int64_t teamID)
    {
        const auto map = api.GetFullMap();
        if (map.empty() || map.front().empty())
            return std::nullopt;

        for (int32_t x = 0; x < static_cast<int32_t>(map.size()); ++x)
        {
            for (int32_t y = 0; y < static_cast<int32_t>(map.front().size()); ++y)
            {
                auto fac = api.GetFactoryState(x, y);
                if (fac.has_value() && fac->teamID == teamID)
                    return Cell{x, y};
            }
        }
        return std::nullopt;
    }

    // ---- Find nearest harvestable resource (for car) ----
    [[nodiscard]] std::optional<PathTarget> FindNearestResource(
        ICharacterAPI& api, const THUAI9::Character& self
    )
    {
        const auto map = api.GetFullMap();
        if (map.empty() || map.front().empty())
            return std::nullopt;

        const Cell start{GridToCell(self.x), GridToCell(self.y)};
        auto blocked = GetTeammateCells(api, self);
        std::optional<PathTarget> best;

        for (int32_t x = 0; x < static_cast<int32_t>(map.size()); ++x)
        {
            for (int32_t y = 0; y < static_cast<int32_t>(map.front().size()); ++y)
            {
                if (map[x][y] != THUAI9::PlaceType::Resource)
                    continue;
                auto res = api.GetResourceState(x, y);
                if (!res.has_value() || res->state == THUAI9::ResourceState::Harvested)
                    continue;

                // Try 4 adjacent cells as approach positions
                for (const auto& [dx, dy] : kDirs)
                {
                    const int32_t nx = x + dx;
                    const int32_t ny = y + dy;
                    if (!InBounds(map, nx, ny))
                        continue;
                    if (!Walkable(map[nx][ny]))
                        continue;
                    if (blocked.count({nx, ny}) && Cell{nx, ny} != start)
                        continue;

                    auto path = Bfs(map, start, {nx, ny}, blocked);
                    if (path.empty())
                        continue;
                    if (!best.has_value() || path.size() < best->path.size())
                        best = PathTarget{{x, y}, {nx, ny}, std::move(path)};
                }
            }
        }
        return best;
    }

    // ---- Find nearest unowned compute center (for drone) ----
    [[nodiscard]] std::optional<PathTarget> FindNearestFreeCenter(
        ICharacterAPI& api, const THUAI9::Character& self
    )
    {
        const auto map = api.GetFullMap();
        if (map.empty() || map.front().empty())
            return std::nullopt;

        const Cell start{GridToCell(self.x), GridToCell(self.y)};
        auto blocked = GetTeammateCells(api, self);
        std::optional<PathTarget> best;

        for (int32_t x = 0; x < static_cast<int32_t>(map.size()); ++x)
        {
            for (int32_t y = 0; y < static_cast<int32_t>(map.front().size()); ++y)
            {
                if (map[x][y] != THUAI9::PlaceType::ComputeCenter)
                    continue;
                auto cc = api.GetComputeCenterState(x, y);
                if (!cc.has_value())
                    continue;
                if (cc->ownerTeamID == self.teamID)
                    continue;

                auto pt = FindPathToCell(api, self, {x, y});
                if (!pt.has_value())
                    continue;
                if (!best.has_value() || pt->path.size() < best->path.size())
                    best = std::move(pt);
            }
        }
        return best;
    }

    // ---- Find nearest enemy character (for drone) ----
    [[nodiscard]] std::optional<PathTarget> FindNearestEnemy(
        ICharacterAPI& api, const THUAI9::Character& self
    )
    {
        auto enemies = api.GetEnemyCharacters();
        if (enemies.empty())
            return std::nullopt;

        const auto map = api.GetFullMap();
        const Cell start{GridToCell(self.x), GridToCell(self.y)};
        auto blocked = GetTeammateCells(api, self);
        std::optional<PathTarget> best;

        for (const auto& enemy : enemies)
        {
            if (!enemy || enemy->characterActiveState == THUAI9::CharacterState::Deceased)
                continue;

            Cell enemyCell{GridToCell(enemy->x), GridToCell(enemy->y)};

            // Try 4 adjacent cells as approach positions
            for (const auto& [dx, dy] : kDirs)
            {
                const int32_t nx = enemyCell.first + dx;
                const int32_t ny = enemyCell.second + dy;
                if (!InBounds(map, nx, ny))
                    continue;
                if (!Walkable(map[nx][ny]))
                    continue;
                if (blocked.count({nx, ny}) && Cell{nx, ny} != start)
                    continue;

                auto path = Bfs(map, start, {nx, ny}, blocked);
                if (path.empty())
                    continue;
                if (!best.has_value() || path.size() < best->path.size())
                {
                    PathTarget pt;
                    pt.object = enemyCell;
                    pt.approach = {nx, ny};
                    pt.path = std::move(path);
                    pt.targetPlayerID = enemy->playerID;
                    best = std::move(pt);
                }
            }
        }
        return best;
    }

    // ============================================================
    //  Harvester AI — AutonomousCar
    //  harvest resource → return to factory → repeat
    // ============================================================
    void HarvesterAI(ICharacterAPI& api, const THUAI9::Character& self, CharState& s)
    {
        const Cell selfCell{GridToCell(self.x), GridToCell(self.y)};
        api.Print(
            "car frame=" + std::to_string(api.GetFrameCount()) +
            " phase=" + std::to_string(static_cast<int>(s.phase)) +
            " cell=" + CellText(selfCell)
        );

        // --- SeekResource: find nearest resource and plan path ---
        if (s.phase == Phase::SeekResource)
        {
            auto target = FindNearestResource(api, self);
            if (!target.has_value())
            {
                api.Print("car: no resource");
                return;
            }
            s.target = std::move(*target);
            s.pathIndex = 1;
            s.phase = Phase::ToResource;
            api.Print("car: -> resource " + CellText(s.target.object));
            return;
        }

        // --- ToResource: walk to resource, then harvest ---
        if (s.phase == Phase::ToResource)
        {
            auto res = api.GetResourceState(s.target.object.first, s.target.object.second);
            if (!res.has_value() || res->state == THUAI9::ResourceState::Harvested)
            {
                s.phase = Phase::SeekResource;
                s.target = {};
                return;
            }

            if (!MoveAlongPath(api, self, s))
                return;

            if (!InInteractRange(selfCell, s.target.object))
            {
                s.phase = Phase::SeekResource;
                s.target = {};
                return;
            }

            api.Print("car: harvest");
            if (api.Harvest().get())
            {
                // Harvest complete, go home
                auto fac = FindTeamFactoryCell(api, self.teamID);
                if (!fac.has_value())
                {
                    s.phase = Phase::SeekResource;
                    s.target = {};
                    return;
                }
                auto home = FindPathToCell(api, self, *fac);
                if (!home.has_value())
                {
                    s.phase = Phase::SeekResource;
                    s.target = {};
                    return;
                }
                s.target = std::move(*home);
                s.pathIndex = 1;
                s.phase = Phase::ToFactory;
                api.Print("car: -> factory");
            }
            else
            {
                s.phase = Phase::SeekResource;
                s.target = {};
            }
            return;
        }

        // --- ToFactory: walk to factory to deposit ---
        if (s.phase == Phase::ToFactory)
        {
            auto fac = FindTeamFactoryCell(api, self.teamID);
            if (!fac.has_value())
            {
                s.phase = Phase::SeekResource;
                s.target = {};
                return;
            }

            if (!MoveAlongPath(api, self, s))
                return;

            if (InInteractRange(selfCell, *fac))
            {
                // Arrived at factory, resources auto-deposited
                api.Print("car: at factory, depositing");
                s.phase = Phase::SeekResource;
                s.target = {};
                return;
            }

            // Re-plan if we lost the path
            auto home = FindPathToCell(api, self, *fac);
            if (home.has_value())
            {
                s.target = std::move(*home);
                s.pathIndex = 1;
            }
            else
            {
                s.phase = Phase::SeekResource;
                s.target = {};
            }
            return;
        }
    }

    // ============================================================
    //  Occupier AI — Drone
    //  occupy compute centers → attack enemies if none available
    // ============================================================
    void OccupierAI(ICharacterAPI& api, const THUAI9::Character& self, CharState& s)
    {
        // Don't issue new commands while the character is busy (moving, occupying, etc.)
        if (IsBusy(self))
            return;

        const Cell selfCell{GridToCell(self.x), GridToCell(self.y)};
        api.Print(
            "drone frame=" + std::to_string(api.GetFrameCount()) +
            " phase=" + std::to_string(static_cast<int>(s.phase)) +
            " cell=" + CellText(selfCell)
        );

        // --- SeekCenter: look for a center to occupy, or an enemy to attack ---
        if (s.phase == Phase::SeekCenter)
        {
            auto center = FindNearestFreeCenter(api, self);
            if (center.has_value())
            {
                s.target = std::move(*center);
                s.pathIndex = 1;
                s.phase = Phase::ToCenter;
                api.Print("drone: -> center " + CellText(s.target.object));
                return;
            }

            // No free centers, hunt enemies
            auto enemy = FindNearestEnemy(api, self);
            if (enemy.has_value())
            {
                s.target = std::move(*enemy);
                s.pathIndex = 1;
                s.phase = Phase::ToEnemy;
                api.Print("drone: -> enemy playerID=" + std::to_string(s.target.targetPlayerID));
                return;
            }

            api.Print("drone: idle");
            return;
        }

        // --- ToCenter: walk to center, then occupy ---
        if (s.phase == Phase::ToCenter)
        {
            auto cc = api.GetComputeCenterState(s.target.object.first, s.target.object.second);
            if (!cc.has_value() || cc->ownerTeamID == self.teamID)
            {
                s.phase = Phase::SeekCenter;
                s.target = {};
                return;
            }

            if (!MoveAlongPath(api, self, s))
                return;

            if (!InInteractRange(selfCell, s.target.object))
            {
                s.phase = Phase::SeekCenter;
                s.target = {};
                return;
            }

            api.Print("drone: occupy");
            (void)api.Occupy().get();
            s.phase = Phase::SeekCenter;
            s.target = {};
            return;
        }

        // --- ToEnemy: walk to enemy, then attack ---
        if (s.phase == Phase::ToEnemy)
        {
            // Verify enemy still alive
            bool alive = false;
            auto enemies = api.GetEnemyCharacters();
            for (const auto& e : enemies)
            {
                if (e && e->playerID == s.target.targetPlayerID &&
                    e->characterActiveState != THUAI9::CharacterState::Deceased)
                {
                    alive = true;
                    break;
                }
            }
            if (!alive)
            {
                s.phase = Phase::SeekCenter;
                s.target = {};
                return;
            }

            if (!MoveAlongPath(api, self, s))
                return;

            api.Print("drone: attack playerID=" + std::to_string(s.target.targetPlayerID));
            (void)api.Common_Attack(s.target.targetPlayerID).get();
            s.phase = Phase::SeekCenter;
            s.target = {};
            return;
        }
    }

    // ============================================================
    //  Character dispatcher — route to role-specific AI
    // ============================================================
    void CharAI(ICharacterAPI& api, const THUAI9::Character& self, CharState& s)
    {
        switch (self.characterType)
        {
            case THUAI9::CharacterType::AutonomousCar:
                HarvesterAI(api, self, s);
                break;
            case THUAI9::CharacterType::Drone:
                // Ensure drone starts in SeekCenter phase
                if (s.phase == Phase::SeekResource || s.phase == Phase::ToResource || s.phase == Phase::ToFactory)
                    s.phase = Phase::SeekCenter;
                OccupierAI(api, self, s);
                break;
            default:
                break;
        }
    }

    // ============================================================
    //  Team AI — build 1 car, then 1 drone. Produce goods.
    // ============================================================
    void TeamAI(ITeamAPI& api, const THUAI9::Team& team, TeamState& s)
    {
        api.Print(
            "team frame=" + std::to_string(api.GetFrameCount()) +
            " car=" + std::string(s.carBuilt ? "yes" : "no") +
            " drone=" + std::string(s.droneBuilt ? "yes" : "no") +
            " compute=" + std::to_string(team.computePower) +
            " material=" + std::to_string(team.material)
        );

        // Priority 1: build AutonomousCar (30 compute) for resource gathering
        if (!s.carBuilt)
        {
            if (static_cast<int32_t>(team.computePower) >= 30)
            {
                api.Print("build AutonomousCar");
                if (api.BuildCharacter(THUAI9::CharacterType::AutonomousCar, kPlayerIdCar).get())
                    s.carBuilt = true;
            }
            else
            {
                api.Print("waiting for compute to build car (have " + std::to_string(team.computePower) + ", need 30)");
            }
            return;
        }

        // Priority 2: build Drone (40 compute) for occupying centers
        if (!s.droneBuilt)
        {
            if (static_cast<int32_t>(team.computePower) >= 40)
            {
                api.Print("build Drone");
                if (api.BuildCharacter(THUAI9::CharacterType::Drone, kPlayerIdDrone).get())
                    s.droneBuilt = true;
            }
            return;
        }

        // Both built — produce goods
        auto fac = FindTeamFactoryCell(api, team.teamID);
        if (!fac.has_value())
            return;

        auto factory = api.GetFactoryState(fac->first, fac->second);
        if (!factory.has_value())
            return;

        const int32_t frame = api.GetFrameCount();
        if (frame - s.lastProduceFrame < 20)
            return;

        // Produce Food: cheapest (3 material), fastest (1 time unit), good value
        if (factory->source >= 3)
        {
            api.Print("produce Food");
            if (api.ProduceGoods(THUAI9::GoodsType::Food, 1).get())
                s.lastProduceFrame = frame;
        }
    }

}  // namespace

// ================================================================
//  Entry points
// ================================================================

void AI::play(ICharacterAPI& api)
{
    auto self = api.GetSelfInfo();
    if (!self)
        return;
    auto& s = g_charStates[self->playerID];
    CharAI(api, *self, s);
}

void AI::play(ITeamAPI& api)
{
    auto team = api.GetSelfInfo();
    if (!team)
        return;
    auto& s = g_teamStates[team->teamID];
    TeamAI(api, *team, s);
}
