using System;
using System.Collections.Generic;
using System.Text;
using Protobuf;
using THUAI9.Unity.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace THUAI9.Unity.Analysis
{
    /// <summary>
    /// Lightweight in-match statistics panel for replay and live spectator modes.
    /// It records rendered snapshots and surfaces trends/events that are useful to
    /// players and commentators without adding chart dependencies.
    /// </summary>
    public class MatchStatisticsController : MonoBehaviour
    {
        private const int TeamCount = 4;
        private const int MaxEvents = 80;
        private const int MaxHistory = 6000;

        private static MatchStatisticsController instance;

        private readonly List<FrameSnapshot> history = new();
        private readonly List<string> events = new();
        private readonly Dictionary<long, CharacterState> previousCharacterStates = new();
        private readonly Dictionary<long, int> previousCharacterHp = new();
        private readonly Dictionary<string, int> previousBuildingHp = new();
        private readonly Dictionary<string, int> previousResourceRemaining = new();
        private readonly int[] previousScores = new int[TeamCount];

        private Text summaryText;
        private Text eventText;
        private GameObject panelObject;
        private Font uiFont;
        private int lastAnalyzedRenderedCount;
        private bool hasPreviousFrame;
        private float nextUiRefresh;

        public IReadOnlyList<FrameSnapshot> History => history;
        public IReadOnlyList<string> Events => events;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
            {
                return;
            }

            GameObject go = GameObject.Find("MatchStatisticsController") ?? new GameObject("MatchStatisticsController");
            instance = go.GetComponent<MatchStatisticsController>() ?? go.AddComponent<MatchStatisticsController>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            uiFont = GetUIFont();
            BuildPanel();
        }

        private void Update()
        {
            int rendered = FrameSourceHub.RenderedFrameCount;
            if (rendered != lastAnalyzedRenderedCount && CoreParam.currentFrame != null)
            {
                AnalyzeCurrentFrame(rendered);
                lastAnalyzedRenderedCount = rendered;
            }

            if (Time.unscaledTime >= nextUiRefresh)
            {
                RefreshUi();
                nextUiRefresh = Time.unscaledTime + 0.35f;
            }
        }

        private void AnalyzeCurrentFrame(int renderedCount)
        {
            FrameSnapshot snapshot = BuildSnapshot(renderedCount);
            history.Add(snapshot);
            while (history.Count > MaxHistory)
            {
                history.RemoveAt(0);
            }

            DetectScoreEvents(snapshot);
            DetectCharacterEvents();
            DetectBuildingEvents();
            DetectResourceEvents();
            hasPreviousFrame = true;
        }

        private FrameSnapshot BuildSnapshot(int renderedCount)
        {
            var snapshot = new FrameSnapshot
            {
                renderedFrameCount = renderedCount,
                frameIndex = CoreParam.frameCount,
                playbackFrameIndex = CoreParam.playbackCurrentFrameIndex,
                elapsedMilliseconds = CoreParam.playbackElapsedMilliseconds >= 0
                    ? CoreParam.playbackElapsedMilliseconds
                    : (CoreParam.allMessage != null ? Mathf.Max(CoreParam.allMessage.GameTime, 0) : 0),
                sourceKind = FrameSourceHub.ActiveKind.ToString(),
                resourceSiteCount = CoreParam.resources.Count
            };

            snapshot.teams = new TeamSnapshot[TeamCount];
            for (int i = 0; i < TeamCount; i++)
            {
                var team = new TeamSnapshot { teamId = i + 1 };
                if (CoreParam.allMessage != null && i < CoreParam.allMessage.Teams.Count)
                {
                    MessageOfAll.Types.TeamInfo info = CoreParam.allMessage.Teams[i];
                    team.score = info.Score;
                    team.material = info.Material;
                    team.computePower = info.ComputePower;
                    team.factoryHp = info.FactoryHp;
                    team.techSummary = FormatTechLevels(info.TechLevels);
                }

                snapshot.teams[i] = team;
            }

            foreach (MessageOfCharacter character in CoreParam.characters.Values)
            {
                int index = ClampTeamIndex(character.TeamId);
                if (index >= 0)
                {
                    snapshot.teams[index].unitCount++;
                }
            }

            foreach (MessageOfFactory factory in CoreParam.factories.Values)
            {
                int index = ClampTeamIndex(factory.TeamId);
                if (index >= 0)
                {
                    snapshot.teams[index].buildingCount++;
                }
            }

            foreach (MessageOfComputeCenter center in CoreParam.computeCenters.Values)
            {
                int index = ClampTeamIndex(center.OwnerTeamId);
                if (index >= 0)
                {
                    snapshot.teams[index].buildingCount++;
                }
            }

            foreach (MessageOfResource resource in CoreParam.resources.Values)
            {
                snapshot.remainingResource += Mathf.Max(resource.RemainingAmount, 0);
                if (resource.RemainingAmount <= 0 || resource.ResourceState == ResourceState.Harvested)
                {
                    snapshot.depletedResourceSites++;
                }
            }

            return snapshot;
        }

        private void DetectScoreEvents(FrameSnapshot snapshot)
        {
            for (int i = 0; i < TeamCount; i++)
            {
                int score = snapshot.teams[i].score;
                if (hasPreviousFrame && score != previousScores[i])
                {
                    int delta = score - previousScores[i];
                    AddEvent(snapshot, $"队伍 {i + 1} 分数 {(delta >= 0 ? "+" : string.Empty)}{delta}，当前 {score}");
                }

                previousScores[i] = score;
            }
        }

        private void DetectCharacterEvents()
        {
            foreach (MessageOfCharacter character in CoreParam.characters.Values)
            {
                previousCharacterHp.TryGetValue(character.Guid, out int previousHp);
                previousCharacterStates.TryGetValue(character.Guid, out CharacterState previousState);
                bool nowDead = character.Hp <= 0 || character.CharacterActiveState == CharacterState.Deceased;
                bool wasAlive = previousHp > 0 && previousState != CharacterState.Deceased;
                if (hasPreviousFrame && wasAlive && nowDead)
                {
                    AddEvent($"单位阵亡：队伍 {character.TeamId} / 玩家 {character.PlayerId} / {character.CharacterType}");
                }

                previousCharacterHp[character.Guid] = character.Hp;
                previousCharacterStates[character.Guid] = character.CharacterActiveState;
            }
        }

        private void DetectBuildingEvents()
        {
            var current = new Dictionary<string, int>();
            foreach (KeyValuePair<Tuple<int, int>, MessageOfFactory> kvp in CoreParam.factories)
            {
                string key = $"Factory:{kvp.Key.Item1}:{kvp.Key.Item2}";
                current[key] = kvp.Value.Hp;
                if (hasPreviousFrame && !previousBuildingHp.ContainsKey(key))
                {
                    AddEvent($"建筑出现：队伍 {kvp.Value.TeamId} 工厂 ({kvp.Key.Item1},{kvp.Key.Item2})");
                }
                else if (hasPreviousFrame && previousBuildingHp.TryGetValue(key, out int previousHp) && previousHp > 0 && kvp.Value.Hp <= 0)
                {
                    AddEvent($"建筑摧毁：队伍 {kvp.Value.TeamId} 工厂 ({kvp.Key.Item1},{kvp.Key.Item2})");
                }
            }

            foreach (KeyValuePair<Tuple<int, int>, MessageOfComputeCenter> kvp in CoreParam.computeCenters)
            {
                string key = $"Compute:{kvp.Key.Item1}:{kvp.Key.Item2}";
                current[key] = kvp.Value.OccupyProgress;
                if (hasPreviousFrame && !previousBuildingHp.ContainsKey(key))
                {
                    AddEvent($"建筑出现：算力中心 #{kvp.Value.CenterId} ({kvp.Key.Item1},{kvp.Key.Item2})");
                }
            }

            foreach (string previousKey in previousBuildingHp.Keys)
            {
                if (hasPreviousFrame && !current.ContainsKey(previousKey))
                {
                    AddEvent($"建筑消失：{previousKey}");
                }
            }

            previousBuildingHp.Clear();
            foreach (var kvp in current)
            {
                previousBuildingHp[kvp.Key] = kvp.Value;
            }
        }

        private void DetectResourceEvents()
        {
            var current = new Dictionary<string, int>();
            foreach (KeyValuePair<Tuple<int, int>, MessageOfResource> kvp in CoreParam.resources)
            {
                string key = $"Resource:{kvp.Key.Item1}:{kvp.Key.Item2}";
                current[key] = kvp.Value.RemainingAmount;
                if (hasPreviousFrame
                    && previousResourceRemaining.TryGetValue(key, out int previous)
                    && previous > 0
                    && kvp.Value.RemainingAmount <= 0)
                {
                    AddEvent($"资源耗尽：{kvp.Value.Id} ({kvp.Key.Item1},{kvp.Key.Item2})");
                }
            }

            previousResourceRemaining.Clear();
            foreach (var kvp in current)
            {
                previousResourceRemaining[kvp.Key] = kvp.Value;
            }
        }

        private void AddEvent(FrameSnapshot snapshot, string message)
        {
            AddEvent($"[{FormatTime(snapshot.elapsedMilliseconds)} F{snapshot.frameIndex}] {message}");
        }

        private void AddEvent(string message)
        {
            events.Add(message);
            while (events.Count > MaxEvents)
            {
                events.RemoveAt(0);
            }
        }

        private void RefreshUi()
        {
            if (summaryText == null || eventText == null)
            {
                return;
            }

            if (history.Count == 0)
            {
                if (panelObject != null)
                {
                    panelObject.SetActive(false);
                }
                return;
            }

            if (panelObject != null && !panelObject.activeSelf)
            {
                panelObject.SetActive(true);
            }

            FrameSnapshot latest = history[history.Count - 1];
            var builder = new StringBuilder();
            builder.AppendLine($"局内统计：{TranslateSourceKind(latest.sourceKind)}，样本 {history.Count}");
            builder.AppendLine($"时间 {FormatTime(latest.elapsedMilliseconds)}｜渲染帧 {latest.frameIndex}｜回放索引 {latest.playbackFrameIndex + 1}");
            for (int i = 0; i < latest.teams.Length; i++)
            {
                TeamSnapshot team = latest.teams[i];
                builder.Append("队伍 ").Append(team.teamId)
                    .Append("：分数 ").Append(team.score)
                    .Append("，原料 ").Append(team.material)
                    .Append("，算力 ").Append(team.computePower)
                    .Append("，单位 ").Append(team.unitCount)
                    .Append("，建筑 ").Append(team.buildingCount)
                    .Append("，趋势 ").Append(BuildScoreSparkline(i))
                    .AppendLine();
            }
            builder.Append("资源剩余 ").Append(latest.remainingResource)
                .Append("，资源点 ").Append(latest.resourceSiteCount)
                .Append("，已耗尽 ").Append(latest.depletedResourceSites);
            summaryText.text = builder.ToString();

            var eventBuilder = new StringBuilder();
            eventBuilder.AppendLine("事件摘要");
            int start = Mathf.Max(0, events.Count - 4);
            for (int i = start; i < events.Count; i++)
            {
                eventBuilder.AppendLine(events[i]);
            }
            if (events.Count == 0)
            {
                eventBuilder.AppendLine("暂无分数变化、单位死亡、建筑或资源事件。");
            }
            eventText.text = eventBuilder.ToString();
        }

        private static string FormatTechLevels(IEnumerable<KeyValuePair<string, int>> techLevels)
        {
            if (techLevels == null)
            {
                return "暂无";
            }

            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in techLevels)
            {
                if (kv.Value <= 0)
                {
                    continue;
                }

                parts.Add($"{ShortTechName(kv.Key)} {kv.Value}级");
                if (parts.Count >= 3)
                {
                    break;
                }
            }

            return parts.Count > 0 ? string.Join("、", parts) : "暂无";
        }

        private static string ShortTechName(string key)
        {
            return key switch
            {
                "Robust" => "生命耐久",
                "Warrior" => "攻击能力",
                "MoveSpeed" => "移动速度",
                "Carry" => "携带容量",
                "Efficiency" => "采集效率",
                "Production" => "生产效率",
                "Storage" => "仓储容量",
                "Price" => "出售价格",
                "Cost" => "生产成本",
                "Market" => "市场能力",
                _ => string.IsNullOrWhiteSpace(key) ? "?" : key
            };
        }

        private string BuildScoreSparkline(int teamIndex)
        {
            if (history.Count < 2)
            {
                return "无变化";
            }

            const string blocks = "▁▂▃▄▅▆▇█";
            int sampleCount = Mathf.Min(12, history.Count);
            int start = history.Count - sampleCount;
            int min = int.MaxValue;
            int max = int.MinValue;
            for (int i = start; i < history.Count; i++)
            {
                int value = history[i].teams[teamIndex].score;
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
            }

            if (min == max)
            {
                return new string('━', sampleCount);
            }

            var builder = new StringBuilder();
            for (int i = start; i < history.Count; i++)
            {
                int value = history[i].teams[teamIndex].score;
                int bucket = Mathf.Clamp(Mathf.RoundToInt((value - min) / (float)(max - min) * (blocks.Length - 1)), 0, blocks.Length - 1);
                builder.Append(blocks[bucket]);
            }
            return builder.ToString();
        }

        private void BuildPanel()
        {
            Canvas canvas = EnsureCanvas();
            GameObject panel = GameObject.Find("HUD_StatsPanel") ?? new GameObject("HUD_StatsPanel", typeof(RectTransform), typeof(Image));
            panelObject = panel;
            panel.transform.SetParent(canvas.transform, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(470f, -108f);
            rect.sizeDelta = new Vector2(430f, 260f);

            Image image = panel.GetComponent<Image>();
            image.color = new Color(0.026f, 0.043f, 0.065f, 0.90f);
            image.raycastTarget = false;

            panel.transform.SetAsFirstSibling();

            summaryText = FindOrCreateText(panel.transform, "MatchStatsSummaryText", "局内统计\n等待首帧", 16, new Color(0.86f, 0.94f, 0.98f, 1f));
            SetRect(summaryText.rectTransform, 16f, -14f, 398f, 158f);
            eventText = FindOrCreateText(panel.transform, "MatchStatsEventText", "事件摘要\n暂无", 15, new Color(0.74f, 0.92f, 0.82f, 1f));
            SetRect(eventText.rectTransform, 16f, -180f, 398f, 66f);
            panelObject.SetActive(false);
        }

        private Canvas EnsureCanvas()
        {
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas != null)
            {
                return canvas;
            }

            GameObject canvasObject = new GameObject("Canvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            if (EventSystem.current == null)
            {
                GameObject eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<StandaloneInputModule>();
            }
            return canvas;
        }

        private Text FindOrCreateText(Transform parent, string name, string text, int fontSize, Color color)
        {
            GameObject go = GameObject.Find(name) ?? new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text label = go.GetComponent<Text>();
            label.text = text;
            label.font = uiFont;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = false;
            label.lineSpacing = 1.05f;
            label.raycastTarget = false;
            return label;
        }

        private static void SetRect(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, top);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static int ClampTeamIndex(long teamId)
        {
            return teamId >= 1 && teamId <= TeamCount ? (int)teamId - 1 : -1;
        }

        private static string FormatTime(int totalMilliseconds)
        {
            totalMilliseconds = Mathf.Max(totalMilliseconds, 0);
            int minutes = totalMilliseconds / 60000;
            int seconds = totalMilliseconds / 1000 % 60;
            return $"{minutes:D2}:{seconds:D2}";
        }

        private static string TranslateSourceKind(string sourceKind)
        {
            return sourceKind switch
            {
                "Playback" => "回放",
                "Live" => "实时观战",
                "None" => "未开始",
                _ => string.IsNullOrWhiteSpace(sourceKind) ? "未开始" : sourceKind
            };
        }

        private static Font GetUIFont()
        {
            Font font = Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular");
            if (font != null) return font;
            try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { return null; }
        }

        [Serializable]
        public sealed class FrameSnapshot
        {
            public int renderedFrameCount;
            public int frameIndex;
            public int playbackFrameIndex;
            public int elapsedMilliseconds;
            public string sourceKind;
            public int remainingResource;
            public int resourceSiteCount;
            public int depletedResourceSites;
            public TeamSnapshot[] teams;
        }

        [Serializable]
        public sealed class TeamSnapshot
        {
            public int teamId;
            public int score;
            public int material;
            public int computePower;
            public int factoryHp;
            public string techSummary = "暂无";
            public int unitCount;
            public int buildingCount;
        }
    }
}
