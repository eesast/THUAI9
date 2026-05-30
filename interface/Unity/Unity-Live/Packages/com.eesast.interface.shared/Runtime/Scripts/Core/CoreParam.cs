using System;
using System.Collections.Generic;
using Protobuf;
using UnityEngine;

namespace THUAI9.Unity.Core
{
    public class FrameQueue<T>
    {
        private readonly Queue<T> queue = new Queue<T>();
        private readonly object lockObj = new object();

        public void Add(T item)
        {
            lock (lockObj)
            {
                queue.Enqueue(item);
            }
        }

        public T GetValue()
        {
            lock (lockObj)
            {
                if (queue.Count > 0)
                {
                    return queue.Dequeue();
                }

                return default;
            }
        }

        public int GetSize()
        {
            lock (lockObj)
            {
                return queue.Count;
            }
        }

        public void Clear()
        {
            lock (lockObj)
            {
                queue.Clear();
            }
        }
    }

    public static class CoreParam
    {
        public static FrameQueue<MessageToClient> frameQueue = new FrameQueue<MessageToClient>();

        public static MessageToClient firstFrame;
        public static MessageToClient currentFrame;
        public static MessageOfAll allMessage;
        public static MessageOfMap map;
        public static GameState gameState = GameState.NullGameState;
        public static GameMode gameMode = GameMode.NullGameMode;
        public static GlobalAIEvent latestAIEvent;
        public static AIWorldEffect latestAIEffect;

        public static int frameCount;
        public static int playbackCurrentFrameIndex = -1;
        public static int playbackElapsedMilliseconds;
        public static int stableLiveGameMilliseconds;
        public static bool initialized;

        // Server GameEnd frames can report an overflowed timer after the match timer stops.
        // Keep the UI clock on the last sane live value instead of rendering 500+ minutes.
        public const int OfficialMatchDurationMilliseconds = 10 * 60 * 1000;
        public const int GameTimeGuardSlackMilliseconds = 5 * 60 * 1000;
        public const int MaximumDisplayGameMilliseconds =
            OfficialMatchDurationMilliseconds + GameTimeGuardSlackMilliseconds;
        private const int MaximumSingleLiveTimeJumpMilliseconds = MaximumDisplayGameMilliseconds;
        private static bool hasStableLiveGameTime;
        private const int MaxTeamDisplayNameLength = 18;
        private static readonly Dictionary<long, string> teamDisplayNames = new Dictionary<long, string>();

        public static Dictionary<long, MessageOfCharacter> characters = new Dictionary<long, MessageOfCharacter>();
        public static Dictionary<long, MessageOfTeam> teams = new Dictionary<long, MessageOfTeam>();
        public static Dictionary<Tuple<int, int>, MessageOfFactory> factories = new Dictionary<Tuple<int, int>, MessageOfFactory>();
        public static Dictionary<Tuple<int, int>, MessageOfComputeCenter> computeCenters = new Dictionary<Tuple<int, int>, MessageOfComputeCenter>();
        public static Dictionary<Tuple<int, int>, MessageOfMarket> markets = new Dictionary<Tuple<int, int>, MessageOfMarket>();
        public static Dictionary<Tuple<int, int>, MessageOfResource> resources = new Dictionary<Tuple<int, int>, MessageOfResource>();
        public static Dictionary<Tuple<int, int>, MessageOfBarrier> barriers = new Dictionary<Tuple<int, int>, MessageOfBarrier>();
        public static Dictionary<Tuple<int, int>, MessageOfBush> bushes = new Dictionary<Tuple<int, int>, MessageOfBush>();
        public static List<GlobalAIEvent> aiEvents = new List<GlobalAIEvent>();
        public static List<AIWorldEffect> aiEffects = new List<AIWorldEffect>();

        public static Dictionary<long, GameObject> charactersG = new Dictionary<long, GameObject>();
        public static Dictionary<Tuple<int, int>, GameObject> factoriesG = new Dictionary<Tuple<int, int>, GameObject>();
        public static Dictionary<Tuple<int, int>, GameObject> computeCentersG = new Dictionary<Tuple<int, int>, GameObject>();
        public static Dictionary<Tuple<int, int>, GameObject> marketsG = new Dictionary<Tuple<int, int>, GameObject>();
        public static Dictionary<Tuple<int, int>, GameObject> resourcesG = new Dictionary<Tuple<int, int>, GameObject>();
        public static Dictionary<Tuple<int, int>, GameObject> barriersG = new Dictionary<Tuple<int, int>, GameObject>();
        public static Dictionary<Tuple<int, int>, GameObject> bushesG = new Dictionary<Tuple<int, int>, GameObject>();
        public static Dictionary<Tuple<int, int>, GameObject> mapTilesG = new Dictionary<Tuple<int, int>, GameObject>();

        public static void Reset()
        {
            frameQueue.Clear();

            characters.Clear();
            teams.Clear();
            factories.Clear();
            computeCenters.Clear();
            markets.Clear();
            resources.Clear();
            barriers.Clear();
            bushes.Clear();

            DestroyAll(charactersG);
            DestroyAll(factoriesG);
            DestroyAll(computeCentersG);
            DestroyAll(marketsG);
            DestroyAll(resourcesG);
            DestroyAll(barriersG);
            DestroyAll(bushesG);
            DestroyAll(mapTilesG);

            firstFrame = null;
            currentFrame = null;
            allMessage = null;
            map = null;
            gameState = GameState.NullGameState;
            gameMode = GameMode.NullGameMode;
            latestAIEvent = null;
            latestAIEffect = null;
            aiEvents.Clear();
            aiEffects.Clear();
            frameCount = 0;
            playbackCurrentFrameIndex = -1;
            playbackElapsedMilliseconds = 0;
            stableLiveGameMilliseconds = 0;
            hasStableLiveGameTime = false;
            initialized = false;
        }

        public static void SetAllMessage(MessageOfAll message, GameState state)
        {
            allMessage = message;
            UpdateStableLiveGameTime(message, state);
        }

        private static void UpdateStableLiveGameTime(MessageOfAll message, GameState state)
        {
            if (message == null)
            {
                return;
            }

            int rawTime = message.GameTime;
            bool rawLooksValid = rawTime >= 0 && rawTime <= MaximumDisplayGameMilliseconds;
            if (!rawLooksValid)
            {
                return;
            }

            if (!hasStableLiveGameTime)
            {
                stableLiveGameMilliseconds = rawTime;
                hasStableLiveGameTime = true;
                return;
            }

            if (state == GameState.GameEnd && rawTime > stableLiveGameMilliseconds + MaximumSingleLiveTimeJumpMilliseconds)
            {
                return;
            }

            if (rawTime > stableLiveGameMilliseconds + MaximumSingleLiveTimeJumpMilliseconds)
            {
                return;
            }

            stableLiveGameMilliseconds = Math.Max(rawTime, stableLiveGameMilliseconds);
        }

        public static int ClampDisplayGameMilliseconds(int totalMilliseconds)
        {
            return Math.Max(0, Math.Min(totalMilliseconds, MaximumDisplayGameMilliseconds));
        }

        public static void ClearTeamDisplayNames()
        {
            teamDisplayNames.Clear();
        }

        public static void SetTeamDisplayName(long teamId, string displayName)
        {
            if (teamId <= 0)
            {
                return;
            }

            string normalized = NormalizeTeamDisplayName(displayName);
            if (string.IsNullOrEmpty(normalized))
            {
                teamDisplayNames.Remove(teamId);
                return;
            }

            teamDisplayNames[teamId] = normalized;
        }

        public static string GetTeamDisplayLabel(long teamId)
        {
            string fallback = $"队伍 {teamId}";
            return teamDisplayNames.TryGetValue(teamId, out string displayName) && !string.IsNullOrWhiteSpace(displayName)
                ? $"{fallback}：{displayName}"
                : fallback;
        }

        private static string NormalizeTeamDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return string.Empty;
            }

            string normalized = displayName
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Replace('<', '＜')
                .Replace('>', '＞')
                .Trim();

            while (normalized.IndexOf("  ", StringComparison.Ordinal) >= 0)
            {
                normalized = normalized.Replace("  ", " ");
            }

            return normalized.Length > MaxTeamDisplayNameLength
                ? normalized.Substring(0, MaxTeamDisplayNameLength) + "…"
                : normalized;
        }

        private static void DestroyAll<TKey>(Dictionary<TKey, GameObject> objects)
        {
            foreach (var kvp in objects)
            {
                if (kvp.Value != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(kvp.Value);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(kvp.Value);
                    }
                }
            }

            objects.Clear();
        }
    }
}
