using GameClass.GameObj;
using GameClass.GameObj.Map;
using GameClass.MapGenerator;
using Gaming;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Playback;
using Preparation.Utility;
using Preparation.Utility.Logging;
using Protobuf;
using System;
using System.Collections.Concurrent;
using System.Threading;
using Timothy.FrameRateTask;

namespace Server
{
    public class ContestResult
    {
        public required string status;
        public required double[] scores;
    }
    partial class GameServer : ServerBase
    {
        private readonly ConcurrentDictionary<long, (SemaphoreSlim, SemaphoreSlim)>[] semaDicts;
        // private object semaDictLock = new();
        protected readonly ArgumentOptions options;
        private readonly HttpSender httpSender;
        private readonly object gameLock = new();
        private MessageToClient currentGameInfo = new();
        private readonly MessageOfObj currentMapMsg = new();
        private readonly object newsLock = new();
        private readonly List<MessageOfNews> currentNews = [];
        private readonly SemaphoreSlim endGameSem = new(0);
        protected readonly Game game;
        private readonly uint spectatorMinPlayerID = 2023;
        public int playerNum;
        public int TeamCount => options.TeamCount;
        // protected long[][] communicationToGameID; // 通信用的ID映射到游戏内的ID，0指向队伍1，1指向队伍2，通信中0为大本营，1-5为船
        private readonly object messageToAllClientsLock = new();
        public static readonly long SendMessageToClientIntervalInMilliseconds = 50;
        private readonly MessageWriter? mwr = null;
        private readonly object spectatorJoinLock = new();
        private const int ClientAckTimeoutMs = 200;

        public void StartGame()
        {
            if (game.GameMap.Timer.IsGaming) return;
            //foreach (var team in communicationToGameID)
            //{
            //    foreach (var id in team)
            //    {
            //        if (id == GameObj.invalidID) return;//如果有未初始化的玩家，不开始游戏
            //    }
            //}
            GameServerLogging.logger.LogInfo("Game starts!");
            CreateStartFile();
            game.StartGame((int)options.GameTimeInSecond * 1000);
            Thread.Sleep(1);
            new Thread(() =>
            {
                bool flag = true;
                new FrameRateTaskExecutor<int>
                (
                    () => game.GameMap.Timer.IsGaming,
                    () =>
                    {
                        if (flag == true)
                        {
                            ReportGame(GameState.GameStart);
                            flag = false;
                        }
                        else ReportGame(GameState.GameRunning);
                    },
                    SendMessageToClientIntervalInMilliseconds,
                    () =>
                    {
                        ReportGame(GameState.GameEnd);  // 最后发一次消息，唤醒发消息的线程，防止发消息的线程由于有概率处在 Wait 状态而卡住
                        OnGameEnd();
                        return 0;
                    }
                ).Start();
            })
            { IsBackground = true }.Start();
        }

        public void CreateStartFile()
        {
            if (options.StartLockFile != DefaultArgumentOptions.FileName)
            {
                using var _ = File.Create(options.StartLockFile);
                GameServerLogging.logger.LogInfo("Successfully Created StartLockFile!");
            }
        }

        private MessageToClient BuildGameFrame(GameState gameState, bool includeMap, bool consumeNews)
        {
            var snapshot = game.GetSnapshot();
            var gameObjList = snapshot.Objects;
            var gameInfo = new MessageToClient();

            if (includeMap)
                gameInfo.ObjMessage.Add(currentMapMsg);

            long time = Environment.TickCount64;
            foreach (GameObj gameObj in gameObjList.Cast<GameObj>())
            {
                MessageOfObj? msg = CopyInfo.Auto(gameObj, time);
                if (msg != null) gameInfo.ObjMessage.Add(msg);
            }

            if (consumeNews)
            {
                lock (newsLock)
                {
                    foreach (var news in currentNews)
                    {
                        MessageOfObj? msg = CopyInfo.Auto(news);
                        if (msg != null) gameInfo.ObjMessage.Add(msg);
                    }
                    currentNews.Clear();
                }
            }

            gameInfo.GameState = gameState;
            gameInfo.AllMessage = GetMessageOfAll(game.GameMap.Timer.NowTime());
            return gameInfo;
        }

        private MessageToClient BuildLateJoinStartFrame()
        {
            lock (messageToAllClientsLock)
            {
                return BuildGameFrame(GameState.GameStart, includeMap: true, consumeNews: false);
            }
        }

        public override void WaitForEnd()
        {
            endGameSem.Wait();
            mwr?.Dispose();
            game.CleanupAfterEnd();
        }

        private void SaveGameResult(string path)
        {
            Dictionary<string, int> result = [];
            int[] score = GetScore();
            for (int i = 0; i < score.Length; i++)
            {
                result.Add($"Team {i + 1}", score[i]);
            }
            JsonSerializer serializer = new();
            using StreamWriter sw = new(path);
            using JsonTextWriter writer = new(sw);
            serializer.Serialize(writer, result);
        }


        protected void SendGameResult(int[] scores, bool crashed = false)		// 天梯的 Server 给网站发消息记录比赛结果
        {
            string? url2 = Environment.GetEnvironmentVariable("FINISH_URL");
            if (url2 == null)
            {
                GameServerLogging.logger.LogInfo("Null FINISH_URL!");
                return;
            }
            else
            {
                httpSender.Url = url2;
                httpSender.Token = options.Token;
            }
            string state = crashed ? "Crashed" : "Finished";
            string[][] player_role = new string[options.TeamCount][];
            for (int i = 0; i < options.TeamCount; i++)
            {
                player_role[i] = new string[options.CharacterCount];
            }
            var characters = game.GetAllCharacters();
            var teams = game.GetAllTeamStatus();
            foreach (var team in teams)
            {
                int count = 0;

                foreach (var c in characters.Where(c => c.TeamId == team.TeamId))
                {
                    player_role[(int)team.TeamId - 1][count] = c.CharacterType.ToString();
                    count++;
                }
            }
            httpSender?.SendHttpRequest(scores, state, player_role).Wait();
        }


        protected double[] PullScore(double[] scores)
        {
            string? url2 = Environment.GetEnvironmentVariable("SCORE_URL");
            if (url2 != null)
            {
                httpSender.Url = url2;
                httpSender.Token = options.Token;
                double[] org = httpSender.GetLadderScore(scores).Result;
                if (org.Length == 0)
                {
                    GameServerLogging.logger.LogInfo("Error: No data returned from the web!");
                    return new double[0];
                }
                if (org.Length != scores.Length)
                {
                    GameServerLogging.logger.LogInfo("Error: Ladder base score count from the web does not match the team count!");
                    return new double[0];
                }
                else
                {
                    double[] final = LadderCalculate(org, scores);
                    if (final.Length == 0)
                    {
                        GameServerLogging.logger.LogInfo($"Error: Unsupported ladder team count: {scores.Length}!");
                    }
                    return final;
                }
            }
            else
            {
                GameServerLogging.logger.LogInfo("Null SCORE_URL Environment!");
                return new double[0];
            }
        }

        protected static double[] LadderCalculate(double[] oriScores, double[] competitionScores)
        {
            /*
             * 天梯得分算法注解
             *
             * 天梯得分算法是经过多轮调整得到，得分算法的设计原则参见：
             *   https://github.com/eesast/THUAI6/discussions/441
             * 以及
             *   https://github.com/eesast/THUAI5/discussions/86
             * 中的讨论
             */

            // Dispatch by team count. Only 2-team and 4-team ladder matches are supported.
            if (oriScores.Length != competitionScores.Length)
                return [];

            return oriScores.Length switch
            {
                2 => TwoTeamLadderCalculate(oriScores, competitionScores),
                4 => FourTeamLadderCalculate(oriScores, competitionScores),
                _ => []
            };
        }

        private static double[] TwoTeamLadderCalculate(double[] oriScores, double[] competitionScores)
        {
            const double normalDeltaThreshold = 300.0;
            const double correctParam = normalDeltaThreshold * 1.2;
            const double winnerWeight = 4e-5;
            const double loserWeight = 1.5e-5;
            const double scoreDeltaThreshold = 3000.0;

            int winnerIndex = 0;
            int loserIndex = 1;

            if (competitionScores[0] < competitionScores[1])
            {
                winnerIndex = 1;
                loserIndex = 0;
            }
            else if (competitionScores[0] == competitionScores[1])
            {
                if (oriScores[0] == oriScores[1])
                    return [0, 0];

                if (oriScores[0] > oriScores[1])
                {
                    winnerIndex = 1;
                    loserIndex = 0;
                }
            }

            double winnerCompetitionScore = competitionScores[winnerIndex];
            double loserCompetitionScore = competitionScores[loserIndex];
            double winnerOriScore = oriScores[winnerIndex];
            double loserOriScore = oriScores[loserIndex];

            double oriDelta = winnerOriScore - loserOriScore;
            double competitionDelta = winnerCompetitionScore - loserCompetitionScore;
            double normalOriDelta = oriDelta / normalDeltaThreshold;
            double correctRate = oriDelta / correctParam;
            double correct = 0.5 * (Math.Tanh((competitionDelta - scoreDeltaThreshold) / scoreDeltaThreshold
                                              - correctRate)
                                    + 1.0);

            double[] resScore = [0, 0];
            resScore[winnerIndex] = Math.Min(300, Math.Round(Math.Pow(winnerCompetitionScore, 2)
                                                             * winnerWeight
                                                             * (1 - Math.Tanh(normalOriDelta))
                                                             * correct));
            resScore[loserIndex] = Math.Max(-120, -Math.Round(Math.Pow(competitionDelta, 2)
                                                              * loserWeight
                                                              * (1 - Math.Tanh(normalOriDelta))
                                                              * correct));
            return resScore;
        }

        private static double[] FourTeamLadderCalculate(double[] oriScores, double[] competitionScores)
        {
            /*
             * 四人天梯分算法设计原则：
             * 1. 将四人局拆成 6 组“两两虚拟对局”，每一组都遵循“胜者涨、败者跌、爆冷放大、虐菜收敛”。
             * 2. 本局分差越大，这一组虚拟对局的修正越强；如果两队本局接近，则只做温和调整。
             * 3. 低天梯击败高天梯时，收益更高；高天梯击败低天梯时，收益明显收敛。
             * 4. 每组虚拟对局都设上限，避免四人局累计过快。
             */
            const int teamCount = 4;
            double[] resScore = new double[teamCount];

            // 用本局最高分与最低分的差做归一化，让不同对局总分尺度下的计算保持平滑。
            double scoreSpread = competitionScores.Max() - competitionScores.Min();
            double scoreScale = Math.Max(1.0, scoreSpread);

            // 天梯差缩放参数，值越小，爆冷放大的效果越明显。
            const double ladderScale = 120.0;

            // 四人局中每队会和另外三队各比较一次，因此单组变化上限要收紧。
            const double winnerCap = 80.0;
            const double loserCap = 40.0;

            for (int i = 0; i < teamCount; i++)
            {
                for (int j = i + 1; j < teamCount; j++)
                {
                    double firstCompetition = competitionScores[i];
                    double secondCompetition = competitionScores[j];
                    double firstLadder = oriScores[i];
                    double secondLadder = oriScores[j];

                    int winnerIndex;
                    int loserIndex;
                    double winnerCompetition;
                    double loserCompetition;
                    double winnerLadder;
                    double loserLadder;

                    // 先确定这一对虚拟对局中的“胜者”和“败者”。
                    // 如果比赛得分相同，则让天梯更低的一方视作胜者，推动分数向中间靠拢。
                    if (firstCompetition > secondCompetition ||
                        (firstCompetition == secondCompetition && firstLadder < secondLadder))
                    {
                        winnerIndex = i;
                        loserIndex = j;
                        winnerCompetition = firstCompetition;
                        loserCompetition = secondCompetition;
                        winnerLadder = firstLadder;
                        loserLadder = secondLadder;
                    }
                    else if (secondCompetition > firstCompetition ||
                             (firstCompetition == secondCompetition && secondLadder < firstLadder))
                    {
                        winnerIndex = j;
                        loserIndex = i;
                        winnerCompetition = secondCompetition;
                        loserCompetition = firstCompetition;
                        winnerLadder = secondLadder;
                        loserLadder = firstLadder;
                    }
                    else
                    {
                        continue;
                    }

                    // 比赛分差越大，这一对虚拟对局的结果越可信。
                    double competitionDelta = winnerCompetition - loserCompetition;

                    // 天梯分差越大，说明两队原本强弱越悬殊。
                    double ladderDelta = winnerLadder - loserLadder;

                    // 把比赛分差压到 [0, 1] 左右的平滑区间内，避免分数尺度过大时更新失控。
                    double gapFactor = 0.2 + 0.8 * 0.5 * (Math.Tanh((competitionDelta / scoreScale - 0.8) * 1.4) + 1.0);

                    // 爆冷时该值更大；强者按预期赢弱者时，该值会收敛。
                    double upsetFactor = 0.5 * (1.0 - Math.Tanh(ladderDelta / ladderScale));

                    // 综合“赢得多不多”和“是不是爆冷”，得到这一对虚拟对局的最终强度。
                    double pairFactor = gapFactor * (0.15 + 0.85 * upsetFactor);

                    // 胜者和败者分别按不同上限更新，保持“赢的涨得多，输的扣得少”的整体倾向。
                    double winnerDelta = Math.Round(winnerCap * pairFactor);
                    double loserDelta = Math.Round(loserCap * pairFactor);

                    resScore[winnerIndex] += winnerDelta;
                    resScore[loserIndex] -= loserDelta;
                }
            }

            return resScore;
        }

        private void OnGameEnd()
        {
            try { mwr?.Flush(); } catch (Exception ex) { GameServerLogging.logger.LogError($"Flush playback failed: {ex.Message}"); }
            if (options.ResultFileName != DefaultArgumentOptions.FileName)
                SaveGameResult(options.ResultFileName.EndsWith(".json")
                             ? options.ResultFileName
                             : options.ResultFileName + ".json");
            GameServerLogging.logger.LogInfo($"OnGameEnd enters with mode={options.Mode}");
            int[] rawMatchScores = GetScore();
            double[] competitionScores = rawMatchScores.Select(x => (double)x).ToArray();
            if (options.Mode == 2)
            {
                bool gameCrashed = false;
                double[] ladderDeltas = PullScore(competitionScores);
                if (ladderDeltas.Length == 0)
                {
                    GameServerLogging.logger.LogInfo("Error: No ladder delta returned from the web! Sending zero ladder deltas.");
                    rawMatchScores = new int[options.TeamCount];
                }
                else
                    rawMatchScores = ladderDeltas.Select(x => (int)x).ToArray();
                SendGameResult(rawMatchScores, gameCrashed);
                endGameSem.Release();
            }
            else if (options.Mode == 1)
            {
                /*
                int[] s = new int[2];
                if (scores[1] > scores[0])
                    s = [0, 2];
                else if (scores[1] == scores[0])
                    s = [1, 1];
                else
                    s = [2, 0];
                */ // 得分计算方式待定
                endGameSem.Release();
                //SendGameResult(s);
            }
            else
            {
                endGameSem.Release();
            }
        }

        public void ReportGame(GameState gameState, bool requiredGaming = true)
        {
            lock (messageToAllClientsLock)
            {
                switch (gameState)
                {
                    case GameState.GameRunning:
                    case GameState.GameEnd:
                    case GameState.GameStart:
                        currentGameInfo = BuildGameFrame(gameState, gameState == GameState.GameStart || IsSpectatorJoin, consumeNews: true);
                        IsSpectatorJoin = false;
                        mwr?.WriteOne(currentGameInfo);
                        break;
                    default:
                        break;
                }
            }
            lock (spectatorJoinLock)
            {
                foreach (var dict in semaDicts)
                {
                    foreach (var kvp in dict)
                    {
                        try { kvp.Value.Item1.Release(); }
                        catch (SemaphoreFullException) { /* 客户端还没消费上一帧 */ }
                    }
                }

                // 若此时观战者加入，则死锁，所以需要 spectatorJoinLock

                var staleClients = new List<(int dictIndex, long playerId)>();
                for (int i = 0; i < semaDicts.Length; i++)
                {
                    foreach (var kvp in semaDicts[i])
                    {
                        if (!kvp.Value.Item2.Wait(ClientAckTimeoutMs))
                        {
                            staleClients.Add((i, kvp.Key));
                            GameServerLogging.logger.LogWarning($"Client ack timeout, remove stream listener. dict={i}, player={kvp.Key}");
                        }
                    }
                }

                foreach (var (dictIndex, playerId) in staleClients)
                {
                    if (semaDicts[dictIndex].TryRemove(playerId, out var semas))
                    {
                        try { semas.Item1.Release(); } catch { }
                        try { semas.Item2.Release(); } catch { }
                    }
                }
            }
        }

        private bool PlayerDeceased(int playerID)
        {
            var chars = game.GetAllCharacters();

            return chars.Any(c =>
                c.PlayerId == playerID &&
                c.State == Preparation.Utility.CharacterState.DECEASED
            );
        }

        public override int[] GetMaterial()
        {
            var teams = game.GetAllTeamStatus();
            int[] material = new int[teams.Count];

            foreach (var t in teams)
            {
                material[(int)t.TeamId - 1] = (int)t.FactorySource;
            }
            return material;
        }
        public override int[] GetComputePower()
        {
            var teams = game.GetAllTeamStatus();
            int[] cp = new int[teams.Count];

            foreach (var t in teams)
            {
                cp[(int)t.TeamId - 1] = (int)t.FactoryComputingPower;
            }
            return cp;
        }

        public override int[] GetScore()
        {
            var teams = game.GetAllTeamStatus();
            int[] score = new int[teams.Count];

            foreach (var t in teams)
            {
                score[(int)t.TeamId - 1] = (int)t.Score;
            }
            return score;
        }

        //private uint GetBirthPointIdx(long playerID)  // 获取出生点位置
        //{
        //    return (uint)playerID + 1; // ID从0-8,出生点从1-9
        //}

        private bool ValidPlayerID(long playerID)
        {
            if (playerID == 0 || (1 <= playerID && playerID <= options.CharacterCount))
                return true;
            return false;
        }


        private MessageOfAll GetMessageOfAll(int time)
        {
            MessageOfAll msg = new()
            {
                GameTime = time
            };

            var teams = game.GetAllTeamStatus();

            foreach (var t in teams.OrderBy(t => t.TeamId))
            {
                msg.Teams.Add(CopyInfo.TeamInfo(t));
            }

            return msg;
        }

        private MessageOfMap MapMsg()
        {
            MessageOfMap msgOfMap = new()
            {
                Height = game.GameMap.Height,
                Width = game.GameMap.Width
            };
            for (int i = 0; i < game.GameMap.Height; i++)
            {
                msgOfMap.Rows.Add(new MessageOfMap.Types.Row());
                for (int j = 0; j < game.GameMap.Width; j++)
                {
                    msgOfMap.Rows[i].Cols.Add(Transformation.PlaceTypeToProto(game.GameMap.ProtoGameMap[i, j]));
                }
            }
            return msgOfMap;
        }

        public GameServer(ArgumentOptions options)
        {
            this.options = options;

            semaDicts = new ConcurrentDictionary<long, (SemaphoreSlim, SemaphoreSlim)>[options.TeamCount + 1];
            for (int i = 0; i <= options.TeamCount; i++)
            {
                semaDicts[i] = new ConcurrentDictionary<long, (SemaphoreSlim, SemaphoreSlim)>();
            }

            LogLevel logLevel = options.LogLevel switch
            {
                1 => LogLevel.Error,
                2 => LogLevel.Warning,
                3 => LogLevel.Information,
                4 => LogLevel.Debug,
                5 => LogLevel.Trace,
                _ => LogLevel.Information
            };
            AdvancedLoggerFactory.SetLogLevel(logLevel);
            if (options.MapResource == DefaultArgumentOptions.MapResource)
                game = new(MapInfo.defaultMapStruct, options.TeamCount);
            else
            {
                // txt文本方案
                if (options.MapResource.EndsWith(".txt"))
                {
                    try
                    {
                        uint[,] map = new uint[GameData.MapRows, GameData.MapCols];
                        string? line;
                        int i = 0, j = 0;
                        using StreamReader sr = new(options.MapResource);
                        #region 读取txt地图
                        while (!sr.EndOfStream && i < GameData.MapRows)
                        {
                            if ((line = sr.ReadLine()) != null)
                            {
                                string[] nums = line.Split(' ');
                                foreach (string item in nums)
                                {
                                    if (item.Length > 1)//以兼容原方案
                                        map[i, j] = (uint)int.Parse(item);
                                    else
                                        //2022-04-22 by LHR 十六进制编码地图方案（防止地图编辑员瞎眼x
                                        map[i, j] = (uint)MapEncoder.Hex2Dec(char.Parse(item));
                                    j++;
                                    if (j >= GameData.MapCols)
                                    {
                                        j = 0;
                                        break;
                                    }
                                }
                                i++;
                            }
                        }
                        #endregion
                        game = new(new(GameData.MapRows, GameData.MapCols, map), options.TeamCount);
                    }
                    catch
                    {
                        game = new(MapInfo.defaultMapStruct, options.TeamCount);
                    }
                }
                // MapStruct二进制方案
                else if (options.MapResource.EndsWith(".map"))
                {
                    try
                    {
                        game = new(MapStruct.FromFile(options.MapResource), options.TeamCount);
                    }
                    catch
                    {
                        game = new(MapInfo.defaultMapStruct, options.TeamCount);
                    }
                }
                else
                {
                    game = new(MapInfo.defaultMapStruct, options.TeamCount);
                }
            }
            currentMapMsg = new() { MapMessage = MapMsg() };
            playerNum = options.CharacterCount + options.HomeCount;
            /*
            communicationToGameID = new long[TeamCount][];
            for (int i = 0; i < TeamCount; i++)
            {
                communicationToGameID[i] = new long[options.CharacterCount + options.HomeCount];
            }
            //创建server时先设定待加入对象都是invalid
            for (int team = 0; team < TeamCount; team++)
            {
                communicationToGameID[team][0] = GameObj.invalidID; // team
                for (int i = 1; i <= options.CharacterCount; i++)
                {
                    communicationToGameID[team][i] = GameObj.invalidID; //character
                }
            }
            */
            if (options.FileName != DefaultArgumentOptions.FileName)
            {
                try
                {
                    mwr = new(options.FileName, options.TeamCount, options.CharacterCount);
                }
                catch
                {
                    GameServerLogging.logger.LogInfo($"Error: Cannot create the playback file: {options.FileName}!");
                }
            }

            string? token2 = Environment.GetEnvironmentVariable("TOKEN");
            if (token2 == null)
            {
                GameServerLogging.logger.LogInfo("Null TOKEN Environment!");
            }
            else
                options.Token = token2;
            if (options.Url != DefaultArgumentOptions.Url && options.Token != DefaultArgumentOptions.Token)
            {
                httpSender = new(options.Url, options.Token);
            }
            else
            {
                httpSender = new(DefaultArgumentOptions.Url, DefaultArgumentOptions.Token);
            }
        }
    }
}
