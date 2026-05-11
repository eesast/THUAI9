#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <map>
#include <memory>
#include <optional>
#include <queue>
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
    constexpr int32_t kGridPerCell = 1000;
    constexpr int32_t kCellCenter = 500;
    constexpr int64_t kMoveTimeMs = 200;
    constexpr int32_t kBuilderPlayerId = 1;
    constexpr THUAI9::GoodsType kProduceGoods = THUAI9::GoodsType::Food;
    constexpr int32_t kProduceCost = 3;
    constexpr int32_t kCenterTolerance = 80;
    using Cell = std::pair<int32_t, int32_t>;

    constexpr std::array<Cell, 4> kDirs = {{
        {1, 0},
        {-1, 0},
        {0, 1},
        {0, -1},
    }};

    enum class Phase
    {
        SeekResource,
        ToResource,
        ToFactory,
    };

    struct PathTarget
    {
        Cell object{-1, -1};
        Cell approach{-1, -1};
        std::vector<Cell> path;
    };

    struct CharState
    {
        Phase phase = Phase::SeekResource;
        PathTarget target{};
        size_t pathIndex = 1;
    };

    struct TeamState
    {
        bool builderBuilt = false;
        int32_t lastProduceFrame = -1000;
    };

    std::map<int64_t, CharState> g_charStates;
    std::map<int64_t, TeamState> g_teamStates;

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

    bool MoveToCellCenter(ICharacterAPI& api, const THUAI9::Character& self)
    {
        const Cell cur{GridToCell(self.x), GridToCell(self.y)};
        const int32_t centerX = CellToGrid(cur.first);
        const int32_t centerY = CellToGrid(cur.second);

        if (std::abs(self.x - centerX) <= kCenterTolerance && std::abs(self.y - centerY) <= kCenterTolerance)
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

    [[nodiscard]] std::vector<Cell> Bfs(
        const std::vector<std::vector<THUAI9::PlaceType>>& map,
        Cell start,
        Cell target
    )
    {
        if (!InBounds(map, start.first, start.second) || !InBounds(map, target.first, target.second))
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
                if (!Walkable(map[nx][ny]) && !(nx == target.first && ny == target.second))
                    continue;
                if (vis[nx][ny])
                    continue;

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

    [[nodiscard]] std::optional<PathTarget> FindNearestResource(ICharacterAPI& api, const THUAI9::Character& self)
    {
        const auto map = api.GetFullMap();
        if (map.empty() || map.front().empty())
            return std::nullopt;

        const Cell start{GridToCell(self.x), GridToCell(self.y)};
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

                for (const auto& [dx, dy] : kDirs)
                {
                    const int32_t nx = x + dx;
                    const int32_t ny = y + dy;
                    if (!InBounds(map, nx, ny))
                        continue;
                    if (!Walkable(map[nx][ny]))
                        continue;

                    auto path = Bfs(map, start, {nx, ny});
                    if (path.empty())
                        continue;
                    if (!best.has_value() || path.size() < best->path.size())
                        best = PathTarget{{x, y}, {nx, ny}, std::move(path)};
                }
            }
        }
        return best;
    }

    [[nodiscard]] std::optional<PathTarget> FindHomePath(ICharacterAPI& api, const THUAI9::Character& self)
    {
        const auto map = api.GetFullMap();
        auto fac = FindTeamFactoryCell(api, self.teamID);
        if (!fac.has_value())
            return std::nullopt;

        const Cell start{GridToCell(self.x), GridToCell(self.y)};
        std::optional<PathTarget> best;

        for (const auto& [dx, dy] : kDirs)
        {
            const int32_t nx = fac->first + dx;
            const int32_t ny = fac->second + dy;
            if (!InBounds(map, nx, ny))
                continue;
            if (!Walkable(map[nx][ny]))
                continue;

            auto path = Bfs(map, start, {nx, ny});
            if (path.empty())
                continue;
            if (!best.has_value() || path.size() < best->path.size())
                best = PathTarget{{fac->first, fac->second}, {nx, ny}, std::move(path)};
        }
        return best;
    }

    [[nodiscard]] bool MoveAlongPath(ICharacterAPI& api, const THUAI9::Character& self, CharState& s)
    {
        if (s.target.path.empty() || s.pathIndex >= s.target.path.size())
            return true;

        if (self.characterActiveState == THUAI9::CharacterState::Moving ||
            self.characterActiveState == THUAI9::CharacterState::KnockedBack ||
            self.characterActiveState == THUAI9::CharacterState::Trading ||
            self.characterActiveState == THUAI9::CharacterState::Attacking ||
            self.characterActiveState == THUAI9::CharacterState::Harvesting ||
            self.characterActiveState == THUAI9::CharacterState::Ocuppying)
        {
            return false;
        }

        if (!NearCellCenter(self))
            return MoveToCellCenter(api, self);

        const Cell cur{GridToCell(self.x), GridToCell(self.y)};
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
        const auto [tx, ty] = s.target.path[s.pathIndex + runLen - 1];
        api.Print("move " + CellText({tx, ty}) + " steps=" + std::to_string(runLen));
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

    void CharAI(ICharacterAPI& api, const THUAI9::Character& self, CharState& s)
    {
        if (self.playerID != 1)
            return;

        const Cell selfCell{GridToCell(self.x), GridToCell(self.y)};
        api.Print(
            "char frame=" + std::to_string(api.GetFrameCount()) +
            " phase=" + std::to_string(static_cast<int>(s.phase)) +
            " cell=" + CellText(selfCell)
        );

        if (s.phase == Phase::SeekResource)
        {
            auto target = FindNearestResource(api, self);
            if (!target.has_value())
            {
                api.Print("no resource");
                return;
            }
            s.target = std::move(*target);
            s.pathIndex = 1;
            s.phase = Phase::ToResource;
            api.Print("resource " + CellText(s.target.object) + " -> " + CellText(s.target.approach));
            return;
        }

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

            api.Print("harvest");
            if (api.Harvest().get())
            {
                auto home = FindHomePath(api, self);
                if (!home.has_value())
                {
                    s.phase = Phase::SeekResource;
                    s.target = {};
                    return;
                }
                s.target = std::move(*home);
                s.pathIndex = 1;
                s.phase = Phase::ToFactory;
            }
            else
            {
                s.phase = Phase::SeekResource;
                s.target = {};
            }
            return;
        }

        if (s.phase == Phase::ToFactory)
        {
            auto fac = FindTeamFactoryCell(api, self.teamID);
            if (!fac.has_value())
            {
                s.phase = Phase::SeekResource;
                s.target = {};
                return;
            }

            if (InInteractRange(selfCell, *fac))
            {
                s.phase = Phase::SeekResource;
                s.target = {};
                return;
            }

            if (!MoveAlongPath(api, self, s))
                return;

            auto home = FindHomePath(api, self);
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
        }
    }

    void TeamAI(ITeamAPI& api, const THUAI9::Team& team, TeamState& s)
    {
        api.Print(
            "team frame=" + std::to_string(api.GetFrameCount()) +
            " built=" + std::string(s.builderBuilt ? "true" : "false") +
            " material=" + std::to_string(team.material) +
            " compute=" + std::to_string(team.computePower)
        );

        if (!s.builderBuilt)
        {
            api.Print("build AutonomousCar 1");
            if (api.BuildCharacter(THUAI9::CharacterType::AutonomousCar, kBuilderPlayerId).get())
                s.builderBuilt = true;
            return;
        }

        auto fac = FindTeamFactoryCell(api, team.teamID);
        if (!fac.has_value())
            return;

        auto factory = api.GetFactoryState(fac->first, fac->second);
        if (!factory.has_value())
            return;

        const int32_t frame = api.GetFrameCount();
        if (factory->source >= kProduceCost && frame - s.lastProduceFrame >= 20)
        {
            api.Print("produce Food");
            if (api.ProduceGoods(kProduceGoods, 1).get())
                s.lastProduceFrame = frame;
        }
    }
}  // namespace

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
