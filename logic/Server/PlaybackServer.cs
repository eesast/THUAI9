using Grpc.Core;
using Microsoft.Extensions.Logging;
using Playback;
using Protobuf;
using System.Collections.Concurrent;
using Timothy.FrameRateTask;

namespace Server
{
    class PlaybackServer(ArgumentOptions options) : ServerBase
    {
        protected readonly ArgumentOptions options = options;
        private long[] teamScore = [];
        private readonly ConcurrentDictionary<long, (SemaphoreSlim, SemaphoreSlim)> semaDict = new();
        // private object semaDictLock = new();
        private MessageToClient? currentGameInfo = new();
        private MessageOfObj currentMapMsg = new();
        private const uint spectatorMinPlayerID = 2023;
        // private List<uint> spectatorList = new List<uint>();
        public int TeamCount => options.TeamCount;
        private readonly object spectatorJoinLock = new();
        protected object spectatorLock = new();
        protected bool isSpectatorJoin = false;
        protected bool IsSpectatorJoin
        {
            get
            {
                lock (spectatorLock)
                    return isSpectatorJoin;
            }

            set
            {
                lock (spectatorLock)
                    isSpectatorJoin = value;
            }
        }
        private bool IsGaming { get; set; } = true;
        private int[] finalScore = [];
        public int[] FinalScore
        {
            get
            {
                return finalScore;
            }
        }
        // public override int[] GetMoney() => [];
        public override int[] GetScore() => FinalScore;
        public override int[] GetMaterial()
        {
            int[] material = new int[TeamCount];
            return material;
        }
        public override int[] GetComputePower()
        {
            int[] computepower = new int[TeamCount];
            return computepower;
        }

        public override async Task RegisterFactory(RegisterFactoryMsg request, IServerStreamWriter<MessageToClient> responseStream, ServerCallContext context)
        {
            PlaybackServerLogging.logger.LogDebug($"TRY Register Factory (playback): Player {request.PlayerId}");

            // 仅处理观战者分支（回放服务器复用 RegisterFactory 接口作为观战流）
            if (request.PlayerId >= spectatorMinPlayerID && options.NotAllowSpectator == false)
            {
                PlaybackServerLogging.logger.LogDebug($"TRY Add Spectator (playback): Player {request.PlayerId}");
                lock (spectatorJoinLock)
                {
                    if (!semaDict.TryAdd(request.PlayerId, (new SemaphoreSlim(0, 1), new SemaphoreSlim(0, 1))))
                    {
                        PlaybackServerLogging.logger.LogInfo($"Duplicated Spectator ID {request.PlayerId}");
                        return;
                    }
                    PlaybackServerLogging.logger.LogInfo("A new spectator comes to watch this playback");
                    IsSpectatorJoin = true;
                }

                try
                {
                    // 循环直到回放结束或客户端取消连接
                    while (IsGaming && !context.CancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            // 等待生产者释放信号（由 ReportGame 调用）
                            semaDict[request.PlayerId].Item1.Wait(context.CancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            // 客户端取消或服务器终止等待
                            break;
                        }

                        try
                        {
                            if (currentGameInfo != null)
                            {
                                // 深拷贝并去除新闻消息
                                var info = currentGameInfo.Clone();
                                for (int i = info.ObjMessage.Count - 1; i >= 0; i--)
                                {
                                    if (info.ObjMessage[i].NewsMessage != null)
                                        info.ObjMessage.RemoveAt(i);
                                }
                                await responseStream.WriteAsync(info);
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // 客户端流已关闭 — 清理并退出
                            if (semaDict.TryRemove(request.PlayerId, out var semas))
                            {
                                try { semas.Item1.Release(); } catch { }
                                try { semas.Item2.Release(); } catch { }
                                PlaybackServerLogging.logger.LogInfo($"The spectator {request.PlayerId} exited");
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            PlaybackServerLogging.logger.LogInfo(ex.ToString());
                        }
                        finally
                        {
                            try { semaDict[request.PlayerId].Item2.Release(); } catch { }
                        }
                    }
                }
                finally
                {
                    // 确保清理（无论是正常退出还是异常）
                    if (semaDict.TryRemove(request.PlayerId, out var sema))
                    {
                        try { sema.Item1.Release(); } catch { }
                        try { sema.Item2.Release(); } catch { }
                    }
                    PlaybackServerLogging.logger.LogDebug($"END Add Spectator (playback): Player {request.PlayerId}");
                }
            }
            else
            {
                // 非观战者或不允许观战时直接返回（回放服务器当前仅支持观战流）
                PlaybackServerLogging.logger.LogDebug($"RegisterFactory (playback) ignored: Player {request.PlayerId}");
                return;
            }
        }

        public void ReportGame(MessageToClient? msg)
        {
            currentGameInfo = msg;
            if (currentGameInfo != null && currentGameInfo.GameState == GameState.GameStart)
            {
                currentMapMsg = currentGameInfo.ObjMessage[0];
            }

            if (currentGameInfo != null && IsSpectatorJoin)
            {
                currentGameInfo.ObjMessage.Add(currentMapMsg);
                IsSpectatorJoin = false;
            }

            foreach (var kvp in semaDict)
            {
                kvp.Value.Item1.Release();
            }

            foreach (var kvp in semaDict)
            {
                kvp.Value.Item2.Wait();
            }
        }

        public override void WaitForEnd()
        {
            try
            {
                if (options.ResultOnly)
                {
                    using (MessageReader mr = new(options.FileName))
                    {
                        PlaybackServerLogging.logger.LogInfo("Parsing playback file...");
                        teamScore = new long[mr.teamCount];
                        finalScore = new int[mr.teamCount];
                        int infoNo = 0;
                        object cursorLock = new();
                        var initialTop = Console.CursorTop;
                        var initialLeft = Console.CursorLeft;
                        while (true)
                        {
                            MessageToClient? msg = null;
                            for (int i = 0; i < mr.teamCount; ++i)
                            {
                                for (int j = 0; j < mr.playerCount; ++j)
                                {
                                    msg = mr.ReadOne();
                                    if (msg == null)
                                    {
                                        PlaybackServerLogging.logger.LogInfo(
                                            "The game doesn't come to an end because of timing up!");
                                        IsGaming = false;
                                        goto endParse;
                                    }

                                    lock (cursorLock)
                                    {
                                        var curTop = Console.CursorTop;
                                        var curLeft = Console.CursorLeft;
                                        Console.SetCursorPosition(initialLeft, initialTop);
                                        PlaybackServerLogging.logger.LogInfo(
                                            $"Parsing messages... Current message number: {infoNo}");
                                        Console.SetCursorPosition(curLeft, curTop);
                                    }

                                    if (msg != null)
                                    {
                                        //teamScore[i] = msg.TeamScore;
                                    }
                                }
                            }

                            ++infoNo;

                            if (msg == null)
                            {
                                PlaybackServerLogging.logger.LogInfo("No game information in this file!");
                                goto endParse;
                            }
                            if (msg.GameState == GameState.GameEnd)
                            {
                                PlaybackServerLogging.logger.LogInfo("Game over normally!");
                                for (int i = 0; i < TeamCount; i++)
                                {
                                    finalScore[i] = msg.AllMessage.Teams[i].Score;
                                }
                                goto endParse;
                            }
                        }
                    endParse:
                        PlaybackServerLogging.logger.LogInfo($"Successfully parsed {infoNo} informations!");
                    }
                }
                else
                {
                    long timeInterval = GameServer.SendMessageToClientIntervalInMilliseconds;
                    if (options.PlaybackSpeed != 1.0)
                    {
                        options.PlaybackSpeed = Math.Max(0.25, Math.Min(4.0, options.PlaybackSpeed));
                        timeInterval = (int)Math.Round(timeInterval / options.PlaybackSpeed);
                    }
                    using MessageReader mr = new(options.FileName);
                    teamScore = new long[mr.teamCount];
                    finalScore = new int[mr.teamCount];
                    int infoNo = 0;
                    object cursorLock = new();
                    var msgCurTop = Console.CursorTop;
                    var msgCurLeft = Console.CursorLeft;
                    var frt = new FrameRateTaskExecutor<int>
                    (
                        loopCondition: () => true,
                        loopToDo: () =>
                        {
                            MessageToClient? msg = null;

                            msg = mr.ReadOne();
                            if (msg == null)
                            {
                                PlaybackServerLogging.logger.LogInfo(
                                    "The game doesn't come to an end because of timing up!");
                                IsGaming = false;
                                ReportGame(msg);
                                return false;
                            }
                            ReportGame(msg);
                            lock (cursorLock)
                            {
                                var curTop = Console.CursorTop;
                                var curLeft = Console.CursorLeft;
                                Console.SetCursorPosition(msgCurLeft, msgCurTop);
                                PlaybackServerLogging.logger.LogInfo(
                                    $"Sending messages... Current message number: {infoNo}");
                                Console.SetCursorPosition(curLeft, curTop);
                            }
                            if (msg != null)
                            {
                                foreach (var item in msg.ObjMessage)
                                {
                                    if (item.TeamMessage != null)
                                        teamScore[item.TeamMessage.TeamId] = item.TeamMessage.Score;

                                }
                            }

                            ++infoNo;
                            if (msg == null)
                            {
                                PlaybackServerLogging.logger.LogInfo("No game information in this file!");
                                IsGaming = false;
                                ReportGame(msg);
                                return false;
                            }
                            if (msg.GameState == GameState.GameEnd)
                            {
                                PlaybackServerLogging.logger.LogInfo("Game over normally!");
                                IsGaming = false;
                                for (int i = 0; i < TeamCount; i++)
                                {
                                    finalScore[i] = msg.AllMessage.Teams[i].Score;
                                }
                                ReportGame(msg);
                                return false;
                            }
                            return true;
                        },
                        timeInterval: timeInterval,
                        finallyReturn: () => 0
                    )
                    { AllowTimeExceed = true, MaxTolerantTimeExceedCount = 5 };
                    PlaybackServerLogging.logger.LogInfo("The server is well prepared!");
                    PlaybackServerLogging.logger.LogInfo(
                        "Please MAKE SURE that you have opened all the clients to watch the game!");
                    PlaybackServerLogging.logger.LogInfo(
                        "If ALL clients have opened, press any key to start");
                    Console.ReadKey();

                    new Thread
                        (
                            () =>
                            {
                                var rateCurTop = Console.CursorTop;
                                var rateCurLeft = Console.CursorLeft;
                                lock (cursorLock)
                                {
                                    rateCurTop = Console.CursorTop;
                                    rateCurLeft = Console.CursorLeft;
                                    PlaybackServerLogging.logger.LogInfo(
                                        $"Send message to clients frame rate: {frt.FrameRate}");
                                }
                                while (!frt.Finished)
                                {
                                    lock (cursorLock)
                                    {
                                        var curTop = Console.CursorTop;
                                        var curLeft = Console.CursorLeft;
                                        Console.SetCursorPosition(rateCurLeft, rateCurTop);
                                        PlaybackServerLogging.logger.LogInfo(
                                            $"Send message to clients frame rate: {frt.FrameRate}");
                                        Console.SetCursorPosition(curLeft, curTop);
                                    }
                                    Thread.Sleep(1000);
                                }
                            }
                        )
                    { IsBackground = true }.Start();

                    lock (cursorLock)
                    {
                        msgCurLeft = Console.CursorLeft;
                        msgCurTop = Console.CursorTop;
                        PlaybackServerLogging.logger.LogInfo("Sending messages...");
                    }
                    frt.Start();
                }
            }
            finally
            {
                teamScore ??= [];
            }
        }
    }
}