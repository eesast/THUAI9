#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <limits>
#include <map>
#include <memory>
#include <queue>
#include <string>
#include <unordered_set>
#include <vector>

#include "AI.h"
#include "constants.h"

extern const bool asynchronous = false;

extern const std::array<THUAI9::CharacterType, 3> CharacterTypeDict = {
    THUAI9::CharacterType::Robot,
    THUAI9::CharacterType::Drone,
    THUAI9::CharacterType::AutonomousCar,
};

// ============================================================================
// 选手 AI —— 集成验证程序
//
// 从选手视角测试 C++ API 的核心功能:
//   Team:  召唤角色 → 生产商品 → 升级科技
//   Robot (playerID=1):    占领最近算力中心 → 搬运售卖
//   Drone (playerID=2):    攻击最近敌方工厂
//   AutonomousCar (playerID=3): 采集最近资源
//
// 所有角色使用 BFS 寻路，遇到敌人时优先战斗。
// 运行方式: ./capi -t <teamID 1-4> -p <playerID 0-3> [-d] [-o]
// ============================================================================

namespace
{
    constexpr int64_t kMoveTimeMs = 200;
    constexpr int64_t kAttackCdMs = 1050;
    constexpr int32_t kGoodsAmount = 1;
    constexpr int32_t kCellSize = 1000;
    constexpr int32_t kCellCenter = kCellSize / 2;  // 500
    constexpr int32_t kMapRows = 50;
    constexpr int32_t kMapCols = 50;
    constexpr int32_t kAttackRangeCells = 1;

    constexpr std::array<THUAI9::GoodsType, 5> kGoodsToTest = {
        THUAI9::GoodsType::Food,
        THUAI9::GoodsType::Medicine,
        THUAI9::GoodsType::Clothes,
        THUAI9::GoodsType::Toys,
        THUAI9::GoodsType::Semiconductor,
    };

    constexpr std::array<THUAI9::TechType, 4> kTechToTest = {
        THUAI9::TechType::IncreaseMoveSpeed,
        THUAI9::TechType::IncreaseCarryCapacity,
        THUAI9::TechType::IncreaseEfficiency,
        THUAI9::TechType::DecreaseCost,
    };

    constexpr std::array<std::pair<int32_t, int32_t>, 4> kDirs = {{
        {0, -1},   // 上
        {0, 1},    // 下
        {-1, 0},   // 左
        {1, 0},    // 右
    }};

    // ── 小工具 ──────────────────────────────────────────────────────────

    [[nodiscard]] std::string BoolText(bool value)
    {
        return value ? "true" : "false";
    }

    [[nodiscard]] int32_t CellToGrid(int32_t cell) noexcept
    {
        return cell * kCellSize + kCellCenter;
    }

    [[nodiscard]] int32_t GridToCell(int32_t grid) noexcept
    {
        return grid / kCellSize;
    }

    [[nodiscard]] double CalcAngle(int32_t fromX, int32_t fromY, int32_t toX, int32_t toY)
    {
        // 坐标系: 竖直向下 = x 轴(角度0), 水平向右 = y 轴(角度 π/2)
        return std::atan2(toX - fromX, toY - fromY);
    }

    [[nodiscard]] bool IsPassable(THUAI9::PlaceType pt)
    {
        return pt != THUAI9::PlaceType::Barrier && pt != THUAI9::PlaceType::Factory;
    }

    template<class TAPI>
    void DrainMessages(TAPI& api, const std::string& who)
    {
        while (api.HaveMessage())
        {
            auto [fromID, message] = api.GetMessage();
            api.Print(who + " recv from " + std::to_string(fromID) + ": " + message);
        }
    }

    template<class TAPI>
    void PrintCommonSnapshot(TAPI& api, const std::string& who)
    {
        auto map = api.GetFullMap();
        auto characters = api.GetCharacters();
        auto enemies = api.GetEnemyCharacters();
        auto guids = api.GetPlayerGUIDs();
        auto gameInfo = api.GetGameInfo();

        std::string summary =
            who +
            " frame=" + std::to_string(api.GetFrameCount()) +
            " map=" + std::to_string(map.size()) + "x" + std::to_string(map.empty() ? 0 : map.front().size()) +
            " allyChars=" + std::to_string(characters.size()) +
            " enemyChars=" + std::to_string(enemies.size()) +
            " guids=" + std::to_string(guids.size()) +
            " material=" + std::to_string(api.GetMaterial()) +
            " compute=" + std::to_string(api.GetComputingPower()) +
            " score=" + std::to_string(api.GetScore());

        if (gameInfo)
        {
            summary +=
                " gameTime=" + std::to_string(gameInfo->gameTime) +
                " teamCount=" + std::to_string(gameInfo->teams.size());
        }

        api.Print(summary);
    }

    // ── BFS 寻路 ────────────────────────────────────────────────────────

    struct CellPairHash
    {
        size_t operator()(const std::pair<int32_t, int32_t>& p) const noexcept
        {
            return static_cast<size_t>(p.first) * 10007 + static_cast<size_t>(p.second);
        }
    };

    using CellSet = std::unordered_set<std::pair<int32_t, int32_t>, CellPairHash>;
    using CellMap = std::map<std::pair<int32_t, int32_t>, std::pair<int32_t, int32_t>>;
    using CellQueue = std::queue<std::pair<int32_t, int32_t>>;
    using CellPath = std::vector<std::pair<int32_t, int32_t>>;

    // 返回从 (sx,sy) 到最近 targetType 格子的路径（含起点和终点）
    [[nodiscard]] CellPath BfsToNearest(
        const std::vector<std::vector<THUAI9::PlaceType>>& map,
        int32_t sx, int32_t sy,
        THUAI9::PlaceType targetType)
    {
        if (map.empty() || map.front().empty()) return {};
        const int32_t cols = static_cast<int32_t>(map.size());
        const int32_t rows = static_cast<int32_t>(map.front().size());

        if (sx < 0 || sy < 0 || sx >= cols || sy >= rows) return {};
        if (!IsPassable(map[sx][sy])) return {};

        CellSet visited;
        CellMap prev;
        CellQueue q;

        visited.insert({sx, sy});
        q.push({sx, sy});

        std::pair<int32_t, int32_t> target = {-1, -1};

        while (!q.empty())
        {
            auto [cx, cy] = q.front();
            q.pop();

            if (map[cx][cy] == targetType)
            {
                target = {cx, cy};
                break;
            }

            for (auto [dx, dy] : kDirs)
            {
                int32_t nx = cx + dx;
                int32_t ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                if (!IsPassable(map[nx][ny]) && map[nx][ny] != targetType) continue;
                if (visited.count({nx, ny})) continue;

                visited.insert({nx, ny});
                prev[{nx, ny}] = {cx, cy};
                q.push({nx, ny});
            }
        }

        if (target.first < 0) return {};

        CellPath path;
        auto cur = target;
        while (cur != std::pair<int32_t, int32_t>{sx, sy})
        {
            path.push_back(cur);
            cur = prev[cur];
        }
        path.push_back({sx, sy});
        std::reverse(path.begin(), path.end());
        return path;
    }

    // 返回从 (sx,sy) 到 (tx,ty) 的路径
    [[nodiscard]] CellPath BfsTo(
        const std::vector<std::vector<THUAI9::PlaceType>>& map,
        int32_t sx, int32_t sy,
        int32_t tx, int32_t ty)
    {
        if (map.empty() || map.front().empty()) return {};
        const int32_t cols = static_cast<int32_t>(map.size());
        const int32_t rows = static_cast<int32_t>(map.front().size());

        if (sx < 0 || sy < 0 || sx >= cols || sy >= rows) return {};
        if (tx < 0 || ty < 0 || tx >= cols || ty >= rows) return {};
        if (!IsPassable(map[sx][sy])) return {};

        CellSet visited;
        CellMap prev;
        CellQueue q;

        visited.insert({sx, sy});
        q.push({sx, sy});
        bool found = false;

        while (!q.empty())
        {
            auto [cx, cy] = q.front();
            q.pop();

            if (cx == tx && cy == ty)
            {
                found = true;
                break;
            }

            for (auto [dx, dy] : kDirs)
            {
                int32_t nx = cx + dx;
                int32_t ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                if (!IsPassable(map[nx][ny]) && !(nx == tx && ny == ty)) continue;
                if (visited.count({nx, ny})) continue;

                visited.insert({nx, ny});
                prev[{nx, ny}] = {cx, cy};
                q.push({nx, ny});
            }
        }

        if (!found) return {};

        CellPath path;
        auto cur = std::make_pair(tx, ty);
        while (cur != std::pair<int32_t, int32_t>{sx, sy})
        {
            path.push_back(cur);
            cur = prev[cur];
        }
        path.push_back({sx, sy});
        std::reverse(path.begin(), path.end());
        return path;
    }

    // ── 角色状态 ────────────────────────────────────────────────────────

    enum class CharTask : int32_t
    {
        IDLE = 0,
        NAVIGATING,      // 正在寻路到目标
        OCCUPYING,       // 占领算力中心
        HARVESTING,      // 采集资源
        ATTACKING,       // 攻击敌方
        LOADING,         // 从工厂装载
        SELLING,         // 在市场售卖
        PATROLLING,      // 巡逻
    };

    struct CharState
    {
        CharTask task = CharTask::IDLE;
        std::vector<std::pair<int32_t, int32_t>> path;
        size_t pathIdx = 0;
        int32_t targetCellX = -1;
        int32_t targetCellY = -1;
        int32_t prevCellX = -1;
        int32_t prevCellY = -1;
        int32_t stuckFrames = 0;
        int32_t actionCooldown = 0;
        bool snapshotPrinted = false;
    };

    std::map<int32_t, CharState> g_charStates;
    CharState& GetCharState(int32_t playerID) { return g_charStates[playerID]; }

    // ── 队伍状态 ────────────────────────────────────────────────────────

    struct TeamState
    {
        bool snapshotPrinted = false;
        std::array<bool, 3> charsBuilt{};   // Robot, Drone, Car
        int32_t goodsProduced = 0;
        int32_t techsUpgraded = 0;
        bool messagesSent = false;
    };
    TeamState g_teamState;

    // ── 导航子程序 ──────────────────────────────────────────────────────

    // 每帧调用：沿 path 移动一步。到达返回 true
    bool NavigateStep(ICharacterAPI& api,
                      const std::shared_ptr<const THUAI9::Character>& self,
                      CharState& s)
    {
        if (!self || s.path.empty()) return true;

        int32_t cx = GridToCell(self->x);
        int32_t cy = GridToCell(self->y);

        // 到达路径终点？
        if (s.pathIdx >= s.path.size())
            return true;

        auto [tx, ty] = s.path[s.pathIdx];

        // 已到达当前路径点所在 cell → 前进
        if (cx == tx && cy == ty)
        {
            ++s.pathIdx;
            if (s.pathIdx >= s.path.size())
                return true;  // 到达终点
            tx = s.path[s.pathIdx].first;
            ty = s.path[s.pathIdx].second;
        }

        // 检测卡死
        if (cx == s.prevCellX && cy == s.prevCellY)
        {
            ++s.stuckFrames;
            if (s.stuckFrames > 15)
            {
                // 反卡死：EndAllAction
                api.EndAllAction();
                s.stuckFrames = 0;
                return false;
            }
        }
        else
        {
            s.stuckFrames = 0;
        }
        s.prevCellX = cx;
        s.prevCellY = cy;

        // 朝目标 cell 中心移动
        int32_t targetGx = CellToGrid(tx);
        int32_t targetGy = CellToGrid(ty);
        double angle = CalcAngle(self->x, self->y, targetGx, targetGy);
        api.Move(kMoveTimeMs, angle);
        return false;
    }

    // 启动导航到指定 cell
    bool StartNavigate(const std::vector<std::vector<THUAI9::PlaceType>>& map,
                       const std::shared_ptr<const THUAI9::Character>& self,
                       CharState& s,
                       int32_t tx, int32_t ty)
    {
        if (!self) return false;
        int32_t cx = GridToCell(self->x);
        int32_t cy = GridToCell(self->y);
        auto path = BfsTo(map, cx, cy, tx, ty);
        if (path.empty())
            return false;
        s.path = std::move(path);
        s.pathIdx = 1;  // 跳过起点
        s.task = CharTask::NAVIGATING;
        s.targetCellX = tx;
        s.targetCellY = ty;
        return true;
    }

    // 启动导航到最近的目标类型 cell
    bool StartNavigateToNearest(const std::vector<std::vector<THUAI9::PlaceType>>& map,
                                const std::shared_ptr<const THUAI9::Character>& self,
                                CharState& s,
                                THUAI9::PlaceType targetType)
    {
        if (!self) return false;
        int32_t cx = GridToCell(self->x);
        int32_t cy = GridToCell(self->y);
        auto path = BfsToNearest(map, cx, cy, targetType);
        if (path.empty())
            return false;
        s.path = std::move(path);
        s.pathIdx = 1;
        s.task = CharTask::NAVIGATING;
        s.targetCellX = s.path.back().first;
        s.targetCellY = s.path.back().second;
        return true;
    }

    // ── 战斗检测 ────────────────────────────────────────────────────────

    // 视野内最近敌人→ attack；返回 true = 本帧在处理战斗
    bool CombatCheck(ICharacterAPI& api,
                     const std::shared_ptr<const THUAI9::Character>& self)
    {
        if (!self) return false;

        auto enemies = api.GetEnemyCharacters();
        if (enemies.empty()) return false;

        // 找最近敌人
        int64_t bestDist2 = std::numeric_limits<int64_t>::max();
        const THUAI9::Character* bestEnemy = nullptr;
        for (auto& e : enemies)
        {
            if (!e) continue;
            int64_t dx = static_cast<int64_t>(e->x) - self->x;
            int64_t dy = static_cast<int64_t>(e->y) - self->y;
            int64_t d2 = dx * dx + dy * dy;
            if (d2 < bestDist2)
            {
                bestDist2 = d2;
                bestEnemy = e.get();
            }
        }
        if (!bestEnemy) return false;

        int32_t atkRange = self->commonAttackRange > 0 ? self->commonAttackRange : 1000;
        int64_t atkRangeSq = static_cast<int64_t>(atkRange) * atkRange;

        if (bestDist2 <= atkRangeSq)
        {
            api.EndAllAction();
            api.Common_Attack(bestEnemy->playerID);
            api.Print("⚔ Attacking enemy " + std::to_string(bestEnemy->playerID));
            return true;
        }

        // 若敌人在视野内但超出攻击范围，靠近
        int32_t viewRange = self->viewRange > 0 ? self->viewRange : 5000;
        int64_t viewRangeSq = static_cast<int64_t>(viewRange) * viewRange;
        if (bestDist2 <= viewRangeSq)
        {
            double angle = CalcAngle(self->x, self->y, bestEnemy->x, bestEnemy->y);
            api.Move(kMoveTimeMs, angle);
            return true;
        }

        return false;
    }

    // ── 角色 AI ─────────────────────────────────────────────────────────

    void RobotAI(ICharacterAPI& api,
                 const std::shared_ptr<const THUAI9::Character>& self,
                 CharState& s)
    {
        const std::string who = "Robot(" + std::to_string(self->playerID) + ")";

        // 首次打印快照
        if (!s.snapshotPrinted)
        {
            PrintCommonSnapshot(api, who);
            api.PrintSelfInfo();
            s.snapshotPrinted = true;
        }

        switch (s.task)
        {
            case CharTask::IDLE:
            {
                api.Print(who + ": searching for ComputeCenter...");
                auto map = api.GetFullMap();
                if (!StartNavigateToNearest(map, self, s, THUAI9::PlaceType::ComputeCenter))
                {
                    api.Print(who + ": no ComputeCenter found, patrolling.");
                    s.task = CharTask::PATROLLING;
                }
                break;
            }

            case CharTask::NAVIGATING:
            {
                if (NavigateStep(api, self, s))
                {
                    api.Print(who + ": reached target cell (" +
                              std::to_string(s.targetCellX) + "," +
                              std::to_string(s.targetCellY) + ")");
                    s.task = CharTask::OCCUPYING;
                }
                break;
            }

            case CharTask::OCCUPYING:
            {
                api.EndAllAction();
                bool ok = api.Occupy().get();
                api.Print(who + ": Occupy -> " + BoolText(ok));
                if (ok)
                {
                    api.Print(who + ": CC occupied, switching to patrol.");
                    s.task = CharTask::PATROLLING;
                }
                break;
            }

            case CharTask::PATROLLING:
            default:
            {
                // 简单巡逻：每 60 帧换个方向
                int32_t fc = api.GetFrameCount();
                switch ((fc / 60) % 4)
                {
                    case 0: api.MoveRight(kMoveTimeMs); break;
                    case 1: api.MoveDown(kMoveTimeMs);  break;
                    case 2: api.MoveLeft(kMoveTimeMs);  break;
                    case 3: api.MoveUp(kMoveTimeMs);    break;
                }

                if (fc % 180 == 0)
                    PrintCommonSnapshot(api, who);
                break;
            }
        }
    }

    void DroneAI(ICharacterAPI& api,
                 const std::shared_ptr<const THUAI9::Character>& self,
                 CharState& s)
    {
        const std::string who = "Drone(" + std::to_string(self->playerID) + ")";

        if (!s.snapshotPrinted)
        {
            PrintCommonSnapshot(api, who);
            api.PrintSelfInfo();
            s.snapshotPrinted = true;
        }

        switch (s.task)
        {
            case CharTask::IDLE:
            {
                // 扫描地图找最近的敌方工厂
                api.Print(who + ": scanning for enemy factories...");
                auto map = api.GetFullMap();
                if (map.empty() || map.front().empty()) break;

                int32_t cols = static_cast<int32_t>(map.size());
                int32_t rows = static_cast<int32_t>(map.front().size());
                int64_t bestDist2 = std::numeric_limits<int64_t>::max();
                int32_t bestX = -1, bestY = -1;

                for (int32_t x = 0; x < cols; ++x)
                {
                    for (int32_t y = 0; y < rows; ++y)
                    {
                        if (map[x][y] != THUAI9::PlaceType::Factory) continue;
                        auto facOpt = api.GetFactoryState(x, y);
                        if (!facOpt.has_value()) continue;
                        if (facOpt->teamID == self->teamID) continue;  // 跳过己方

                        int64_t dx = static_cast<int64_t>(CellToGrid(x)) - self->x;
                        int64_t dy = static_cast<int64_t>(CellToGrid(y)) - self->y;
                        int64_t d2 = dx * dx + dy * dy;
                        if (d2 < bestDist2)
                        {
                            bestDist2 = d2;
                            bestX = x;
                            bestY = y;
                        }
                    }
                }

                if (bestX < 0)
                {
                    api.Print(who + ": no enemy factory found, patrolling.");
                    s.task = CharTask::PATROLLING;
                    break;
                }

                api.Print(who + ": targeting enemy factory at (" +
                          std::to_string(bestX) + "," + std::to_string(bestY) + ")");

                auto map2 = api.GetFullMap();
                if (!StartNavigate(map2, self, s, bestX, bestY))
                {
                    api.Print(who + ": path to enemy factory blocked.");
                    s.task = CharTask::PATROLLING;
                }
                break;
            }

            case CharTask::NAVIGATING:
            {
                if (NavigateStep(api, self, s))
                {
                    api.Print(who + ": reached enemy factory cell.");
                    s.task = CharTask::ATTACKING;
                    s.actionCooldown = 0;
                }
                break;
            }

            case CharTask::ATTACKING:
            {
                // 确认目标工厂还在
                auto facOpt = api.GetFactoryState(s.targetCellX, s.targetCellY);
                if (!facOpt.has_value() || facOpt->teamID == self->teamID || facOpt->hp <= 0)
                {
                    api.Print(who + ": target factory destroyed or lost, searching new target.");
                    s.task = CharTask::IDLE;
                    s.path.clear();
                    s.pathIdx = 0;
                    break;
                }

                if (s.actionCooldown > 0)
                {
                    --s.actionCooldown;
                    break;
                }

                api.EndAllAction();
                // Attack 需要 attackedPlayerID；敌方工厂没有 playerID，传 0 服务端自动锁定最近
                bool ok = api.Common_Attack(0).get();
                api.Print(who + ": Attack factory at (" +
                          std::to_string(s.targetCellX) + "," +
                          std::to_string(s.targetCellY) +
                          ") -> " + BoolText(ok));
                s.actionCooldown = 20;  // ~1s cooldown at 50ms/frame
                break;
            }

            case CharTask::PATROLLING:
            default:
            {
                int32_t fc = api.GetFrameCount();
                switch ((fc / 60) % 4)
                {
                    case 0: api.MoveRight(kMoveTimeMs); break;
                    case 1: api.MoveDown(kMoveTimeMs);  break;
                    case 2: api.MoveLeft(kMoveTimeMs);  break;
                    case 3: api.MoveUp(kMoveTimeMs);    break;
                }
                if (fc % 180 == 0)
                    PrintCommonSnapshot(api, who);
                break;
            }
        }
    }

    void CarAI(ICharacterAPI& api,
               const std::shared_ptr<const THUAI9::Character>& self,
               CharState& s)
    {
        const std::string who = "Car(" + std::to_string(self->playerID) + ")";

        if (!s.snapshotPrinted)
        {
            PrintCommonSnapshot(api, who);
            api.PrintSelfInfo();
            s.snapshotPrinted = true;
        }

        switch (s.task)
        {
            case CharTask::IDLE:
            {
                api.Print(who + ": searching for nearest Resource...");
                auto map = api.GetFullMap();
                if (!StartNavigateToNearest(map, self, s, THUAI9::PlaceType::Resource))
                {
                    api.Print(who + ": no Resource found, patrolling.");
                    s.task = CharTask::PATROLLING;
                }
                break;
            }

            case CharTask::NAVIGATING:
            {
                if (NavigateStep(api, self, s))
                {
                    api.Print(who + ": reached resource at (" +
                              std::to_string(s.targetCellX) + "," +
                              std::to_string(s.targetCellY) + ")");
                    s.task = CharTask::HARVESTING;
                }
                break;
            }

            case CharTask::HARVESTING:
            {
                // 检查资源是否还存在
                auto resOpt = api.GetResourceState(s.targetCellX, s.targetCellY);
                if (!resOpt.has_value() ||
                    resOpt->state == THUAI9::ResourceState::Harvested)
                {
                    api.Print(who + ": resource depleted, searching new one.");
                    s.task = CharTask::IDLE;
                    s.path.clear();
                    s.pathIdx = 0;
                    break;
                }

                api.EndAllAction();
                bool ok = api.Harvest().get();
                api.Print(who + ": Harvest -> " + BoolText(ok));

                if (api.GetFrameCount() % 90 == 0)  // 每 ~4.5s 报告
                    PrintCommonSnapshot(api, who);
                break;
            }

            case CharTask::PATROLLING:
            default:
            {
                int32_t fc = api.GetFrameCount();
                switch ((fc / 60) % 4)
                {
                    case 0: api.MoveRight(kMoveTimeMs); break;
                    case 1: api.MoveDown(kMoveTimeMs);  break;
                    case 2: api.MoveLeft(kMoveTimeMs);  break;
                    case 3: api.MoveUp(kMoveTimeMs);    break;
                }
                break;
            }
        }
    }

}  // namespace

// ════════════════════════════════════════════════════════════════════════
// IAI 接口实现
// ════════════════════════════════════════════════════════════════════════

void AI::play(ICharacterAPI& api)
{
    auto self = api.GetSelfInfo();
    if (!self)
        return;  // 角色尚未被召唤

    DrainMessages(api, "char " + std::to_string(playerID));

    // 战斗中断优先
    if (CombatCheck(api, self))
        return;

    auto& s = GetCharState(playerID);

    // 根据角色类型分派
    switch (self->characterType)
    {
        case THUAI9::CharacterType::Robot:
            RobotAI(api, self, s);
            break;
        case THUAI9::CharacterType::Drone:
            DroneAI(api, self, s);
            break;
        case THUAI9::CharacterType::AutonomousCar:
            CarAI(api, self, s);
            break;
        default:
            break;
    }
}

void AI::play(ITeamAPI& api)
{
    auto team = api.GetSelfInfo();
    if (!team)
        return;

    const std::string who = "Team " + std::to_string(team->teamID);
    DrainMessages(api, who);

    if (!g_teamState.snapshotPrinted)
    {
        PrintCommonSnapshot(api, who);
        api.PrintSelfInfo();
        g_teamState.snapshotPrinted = true;
    }

    // ── 召唤角色 ────────────────────────────────────────────────────
    for (int32_t i = 0; i < 3; ++i)
    {
        if (g_teamState.charsBuilt[i]) continue;

        int32_t charId = i + 1;
        bool ok = api.BuildCharacter(CharacterTypeDict[i], charId).get();
        api.Print(who + ": BuildCharacter(" +
                  std::to_string(charId) + ") -> " + BoolText(ok));
        if (ok)
        {
            g_teamState.charsBuilt[i] = true;
            api.Print(who + ": character " + std::to_string(charId) + " built successfully.");
        }
        break;  // 每帧只尝试召唤一个
    }

    // ── 角色全召唤完后发送消息 ──────────────────────────────────────
    bool allBuilt = g_teamState.charsBuilt[0] && g_teamState.charsBuilt[1] && g_teamState.charsBuilt[2];
    if (allBuilt && !g_teamState.messagesSent)
    {
        for (int32_t toID = 1; toID <= 3; ++toID)
        {
            api.SendTextMessage(toID, "Hello from Team " + std::to_string(team->teamID));
            api.SendBinaryMessage(toID, "bin-data-team" + std::to_string(team->teamID));
        }
        g_teamState.messagesSent = true;
        api.Print(who + ": messages sent to all characters.");
    }

    // ── 生产商品 ────────────────────────────────────────────────────
    int32_t fc = api.GetFrameCount();
    if (allBuilt && g_teamState.goodsProduced < static_cast<int32_t>(kGoodsToTest.size()) && fc % 30 == 0)
    {
        auto gt = kGoodsToTest[g_teamState.goodsProduced];
        bool ok = api.ProduceGoods(gt, kGoodsAmount).get();
        api.Print(who + ": ProduceGoods(" +
                  std::to_string(static_cast<int>(gt)) + ") -> " + BoolText(ok));
        if (ok)
            ++g_teamState.goodsProduced;
    }

    // ── 升级科技 ────────────────────────────────────────────────────
    if (allBuilt && g_teamState.techsUpgraded < static_cast<int32_t>(kTechToTest.size()) && fc % 50 == 0)
    {
        auto tt = kTechToTest[g_teamState.techsUpgraded];
        bool ok = api.UplevelTech(tt).get();
        api.Print(who + ": UplevelTech(" +
                  std::to_string(static_cast<int>(tt)) + ") -> " + BoolText(ok));
        if (ok)
            ++g_teamState.techsUpgraded;
    }

    // 定期快照
    if (fc % 240 == 0)
        PrintCommonSnapshot(api, who);
}
