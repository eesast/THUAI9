# 循环赛方案：调现有 API 接口

## 你已有的基础设施

`competition.ts` 第 19-497 行 `/start-all` 已经做完了一切：

- 从 Hasura 拉取全部队伍和代码分配
- 从 COS 下载代码到服务器本地
- 生成所有两两配对（第 303-341 行）
- 创建 `contest_room` + `contest_room_team` 数据库记录
- 推入 Docker 队列（Docker 容器由 cron 自动创建和调度）

**你什么都不用写，只需要发请求。**

## 唯一的问题

GameServer.cs 的 Mode 1（COMPETITION）下，`SendGameResult()` 被注释掉了（第 436 行），游戏结束后的得分**没有发回给 API**，所以 `/finish-one` 收不到分数，`contest_room_team.score` 永远是空的。

## 改动清单

### 1. GameServer.cs（已修复）

`D:\THUthu\THUAI9\logic\Server\GameServer.cs` 第 424 行：

```csharp
else if (options.Mode == 1)
{
    bool gameCrashed = false;
    SendGameResult(rawMatchScores, gameCrashed);  // ← 新：直接发原始得分
    endGameSem.Release();
}
```

### 2. 重建 Server Docker 镜像

```bash
cd THUAI9
dotnet build logic/Server/Server.csproj -c Release
docker build -t eesast/thuai9_run_server:latest \
  -f dependency/Dockerfile/Dockerfile_run_server .
```

### 3. API 新增两个路由

在 `api/src/routes/competition.ts` 加两个接口：

**a) `POST /competition/ranking`** — 拿排名

调 `ContHasFunc.get_round_ranking(round_id)`，从 `contest_room_team` 聚合每个队伍在所有房间的 score，求和降序排列。

**b) `POST /competition/tournament-status`** — 看进度

返回 `{ total, finished, crashed, waiting, running }`，知道还剩几场没打完。

在 `api/src/hasura/contest.ts` 加两个对应的 GraphQL 查询函数。

## 你只需要做的事

```bash
API="https://你的API地址"
TOKEN="你的JWT"
ROUND_ID="xxx-xxx-xxx"

# 1. 发起循环赛
curl -X POST "$API/competition/start-all" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"round_id\": \"$ROUND_ID\"}"

# 2. 轮询等全部打完
while true; do
  curl -X POST "$API/competition/tournament-status" \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json" \
    -d "{\"round_id\": \"$ROUND_ID\"}"
  sleep 60
done

# 3. 拿最终排名
curl -X POST "$API/competition/ranking" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"round_id\": \"$ROUND_ID\"}"
```

这三步可以包成一个简单脚本，比如 `run_tournament.sh`，放在 `THUAI9/tournament/` 下。

## 数据流（完整链路）

```
你的脚本
  │
  └─ POST /competition/start-all ──────────────────────────────────┐
       │                                                            │
       ├─ 查询 Hasura：队伍、代码、编译状态                          │
       ├─ 过滤：只取编译完成的队伍                                   │
       ├─ 从 COS 下载代码到服务器本地                                │
       ├─ 生成两两配对                                               │
       ├─ 为每对创建 contest_room + contest_room_team 记录           │
       └─ 推入 docker_queue                                         │
                                                                     │
  docker_queue (cron, 每30秒)                                        │
       │                                                            │
       ├─ 分配端口                                                   │
       ├─ docker run server  ← MODE=COMPETITION, FINISH_URL=...     │
       ├─ docker run clientA ← 挂载队伍A代码                         │
       ├─ docker run clientB ← 挂载队伍B代码                         │
       └─ 等待 server 退出                                          │
                                                                     │
  游戏结束 (GameServer.OnGameEnd, Mode 1)                            │
       │                                                            │
       ├─ GetScore() → [原始分A, 原始分B]                            │
       └─ SendGameResult() → HTTP POST /competition/finish-one       │
            │                                                        │
            └─ update_room_team_score() → contest_room_team.score    │
                                                                     │
  你的脚本                                                            │
       │                                                            │
       └─ POST /competition/ranking                                  │
            │                                                        │
            └─ 聚合所有 room 的 score → 排名                         │
```

## 改动总览

| 文件 | 改动 | 行数 |
|------|------|------|
| `THUAI9/logic/Server/GameServer.cs` | Mode 1 启用 SendGameResult | 1 行 |
| `api/src/hasura/contest.ts` | 新增 `get_round_ranking()` + `get_round_room_status_summary()` | ~50 行 |
| `api/src/routes/competition.ts` | 新增 `/ranking` + `/tournament-status` 路由 | ~60 行 |
| `THUAI9/tournament/run_tournament.sh` | 调接口的脚本（可选） | ~30 行 |

**不需要新建 Python 项目，不需要操作 Docker，不需要碰数据库。**
