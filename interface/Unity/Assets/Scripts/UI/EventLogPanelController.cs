using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Protobuf;
using THUAI9.Unity.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace THUAI9.Unity.UI
{
    public sealed class EventLogPanelController : MonoBehaviour, IScrollHandler
    {
        private const int MaxEntries = 80;
        private const float AutoScrollBottomThreshold = 0.02f;
        private static readonly Vector2 PanelSize = new Vector2(462f, 300f);
        private static readonly Vector2 PanelPosition = new Vector2(24f, 326f);
        private static Font cachedFont;

        private readonly List<EventLogEntry> entries = new List<EventLogEntry>();
        private readonly Dictionary<Tuple<int, int>, FactorySnapshot> factories = new Dictionary<Tuple<int, int>, FactorySnapshot>();
        private readonly Dictionary<Tuple<int, int>, ResourceSnapshot> resources = new Dictionary<Tuple<int, int>, ResourceSnapshot>();
        private readonly Dictionary<Tuple<int, int>, CenterSnapshot> computeCenters = new Dictionary<Tuple<int, int>, CenterSnapshot>();

        private Canvas rootCanvas;
        private ScrollRect scrollRect;
        private RectTransform viewportRect;
        private RectTransform contentRect;
        private Text titleText;
        private Text contentText;
        private int lastProcessedFrame = -1;
        private bool hasPrimedFrame;
        private bool contentDirty = true;
        private bool stickToBottom = true;

        public void Configure(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            rootCanvas = canvas;
            transform.SetParent(canvas.transform, false);
            BuildPanel();
            if (entries.Count == 0)
            {
                AddLog("等待实时观战或回放数据", "INFO");
            }

            RefreshContentIfNeeded();
        }

        private void Awake()
        {
            if (rootCanvas == null)
            {
                rootCanvas = GetComponentInParent<Canvas>();
            }

            BuildPanel();
        }

        private void Update()
        {
            ProcessCurrentFrameIfNeeded();
            RefreshContentIfNeeded();
        }

        private void BuildPanel()
        {
            RectTransform panelRect = GetComponent<RectTransform>();
            if (panelRect == null)
            {
                panelRect = gameObject.AddComponent<RectTransform>();
            }

            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.zero;
            panelRect.pivot = Vector2.zero;
            panelRect.anchoredPosition = PanelPosition;
            panelRect.sizeDelta = PanelSize;

            Image background = GetComponent<Image>();
            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
            }

            background.color = new Color(0.020f, 0.035f, 0.050f, 0.93f);
            background.raycastTarget = true;

            scrollRect = GetComponent<ScrollRect>();
            if (scrollRect == null)
            {
                scrollRect = gameObject.AddComponent<ScrollRect>();
            }

            Font font = GetUiFont();
            RectTransform title = FindOrCreateRect(transform, "Title", typeof(Text));
            SetChildRect(title, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -10f), new Vector2(-28f, 28f), new Vector2(0f, 1f));
            titleText = title.GetComponent<Text>();
            titleText.text = "事件日志";
            titleText.font = font;
            titleText.fontSize = 22;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.color = new Color(0.88f, 0.98f, 1f, 1f);
            titleText.raycastTarget = false;
            EnsureTextShadow(titleText, new Color(0f, 0f, 0f, 0.7f), new Vector2(1.2f, -1.2f));

            viewportRect = FindOrCreateRect(transform, "Viewport", typeof(Image), typeof(RectMask2D));
            SetChildRect(viewportRect, Vector2.zero, Vector2.one, new Vector2(12f, 12f), new Vector2(-24f, -54f), Vector2.zero);
            Image viewportImage = viewportRect.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewportImage.raycastTarget = true;

            contentRect = FindOrCreateRect(viewportRect, "Content", typeof(ContentSizeFitter));
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 1f);

            ContentSizeFitter fitter = contentRect.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RectTransform contentTextRect = FindOrCreateRect(contentRect, "LogText", typeof(Text));
            SetChildRect(contentTextRect, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 1f));
            contentText = contentTextRect.GetComponent<Text>();
            contentText.font = font;
            contentText.fontSize = 15;
            contentText.fontStyle = FontStyle.Normal;
            contentText.alignment = TextAnchor.UpperLeft;
            contentText.horizontalOverflow = HorizontalWrapMode.Wrap;
            contentText.verticalOverflow = VerticalWrapMode.Overflow;
            contentText.resizeTextForBestFit = false;
            contentText.supportRichText = true;
            contentText.lineSpacing = 1.04f;
            contentText.color = new Color(0.86f, 0.94f, 0.98f, 1f);
            contentText.raycastTarget = false;

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.scrollSensitivity = 34f;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (scrollRect == null || contentRect == null || viewportRect == null)
            {
                return;
            }

            float scrollableHeight = Mathf.Max(1f, contentRect.rect.height - viewportRect.rect.height);
            float normalizedDelta = eventData.scrollDelta.y * scrollRect.scrollSensitivity / scrollableHeight;
            scrollRect.verticalNormalizedPosition = Mathf.Clamp01(scrollRect.verticalNormalizedPosition + normalizedDelta);
            stickToBottom = scrollRect.verticalNormalizedPosition <= AutoScrollBottomThreshold;
            eventData.Use();
        }

        private void ProcessCurrentFrameIfNeeded()
        {
            if (CoreParam.frameCount <= 0 || CoreParam.currentFrame == null)
            {
                if (lastProcessedFrame > 0 && CoreParam.frameCount == 0)
                {
                    ResetSemanticState();
                }

                return;
            }

            if (CoreParam.frameCount < lastProcessedFrame)
            {
                ResetSemanticState();
            }

            if (CoreParam.frameCount == lastProcessedFrame)
            {
                return;
            }

            lastProcessedFrame = CoreParam.frameCount;
            ProcessFactories();
            ProcessResources();
            ProcessComputeCenters();

            if (!hasPrimedFrame)
            {
                hasPrimedFrame = true;
                AddLog("已收到首帧游戏消息", "SUCCESS");
            }
        }

        private void ProcessFactories()
        {
            HashSet<Tuple<int, int>> seen = new HashSet<Tuple<int, int>>();
            foreach (KeyValuePair<Tuple<int, int>, MessageOfFactory> pair in CoreParam.factories)
            {
                Tuple<int, int> key = pair.Key;
                MessageOfFactory factory = pair.Value;
                seen.Add(key);
                FactorySnapshot current = new FactorySnapshot(
                    factory.FactoryId,
                    factory.TeamId,
                    key.Item1,
                    key.Item2,
                    factory.Hp);

                if (factories.TryGetValue(key, out FactorySnapshot previous) && hasPrimedFrame)
                {
                    if (previous.Hp > 0 && current.Hp <= 0)
                    {
                        long collapsedTeamId = ResolveKnownFactoryTeamId(previous, current);
                        if (collapsedTeamId > 0)
                        {
                            AddLog($"{GetTeamName(collapsedTeamId)}在 ({current.X},{current.Y}) 的工厂已瘫痪", "WARNING");
                        }
                    }
                    else if (previous.TeamId != current.TeamId)
                    {
                        AddLog($"({current.X},{current.Y}) 的工厂归属变为 {GetTeamName(current.TeamId)}", "INFO");
                    }
                }

                factories[key] = current;
            }

            RemoveMissing(factories, seen, factory =>
            {
                if (hasPrimedFrame && factory.Hp > 0 && IsKnownTeam(factory.TeamId))
                {
                    AddLog($"{GetTeamName(factory.TeamId)}在 ({factory.X},{factory.Y}) 的工厂不再上报", "WARNING");
                }
            });
        }

        private void ProcessResources()
        {
            HashSet<Tuple<int, int>> seen = new HashSet<Tuple<int, int>>();
            foreach (KeyValuePair<Tuple<int, int>, MessageOfResource> pair in CoreParam.resources)
            {
                Tuple<int, int> key = pair.Key;
                MessageOfResource resource = pair.Value;
                seen.Add(key);
                ResourceSnapshot current = new ResourceSnapshot(resource.Id, key.Item1, key.Item2, resource.RemainingAmount);

                if (resources.TryGetValue(key, out ResourceSnapshot previous) && hasPrimedFrame)
                {
                    if (previous.RemainingAmount > 0 && current.RemainingAmount <= 0)
                    {
                        AddLog($"({current.X},{current.Y}) 的资源点已采尽", "INFO");
                    }
                }

                resources[key] = current;
            }

            RemoveMissing(resources, seen, resource =>
            {
                if (hasPrimedFrame && resource.RemainingAmount > 0)
                {
                    AddLog($"({resource.X},{resource.Y}) 的资源点已采尽", "INFO");
                }
            });
        }

        private void ProcessComputeCenters()
        {
            HashSet<Tuple<int, int>> seen = new HashSet<Tuple<int, int>>();
            foreach (KeyValuePair<Tuple<int, int>, MessageOfComputeCenter> pair in CoreParam.computeCenters)
            {
                Tuple<int, int> key = pair.Key;
                MessageOfComputeCenter center = pair.Value;
                seen.Add(key);
                CenterSnapshot current = new CenterSnapshot(center.CenterId, key.Item1, key.Item2, center.OwnerTeamId);

                if (computeCenters.TryGetValue(key, out CenterSnapshot previous) && hasPrimedFrame)
                {
                    if (previous.OwnerTeamId != current.OwnerTeamId)
                    {
                        string stateText = current.OwnerTeamId > 0
                            ? $"{GetTeamName(current.OwnerTeamId)}占领了 ({current.X},{current.Y}) 的算力中心"
                            : $"({current.X},{current.Y}) 的算力中心回到中立";
                        AddLog(stateText, current.OwnerTeamId > 0 ? "SUCCESS" : "WARNING");
                    }
                }

                computeCenters[key] = current;
            }

            RemoveMissing(computeCenters, seen, center =>
            {
                if (hasPrimedFrame)
                {
                    AddLog($"({center.X},{center.Y}) 的算力中心不再上报", "WARNING");
                }
            });
        }

        private static void RemoveMissing<T>(Dictionary<Tuple<int, int>, T> cache, HashSet<Tuple<int, int>> seenKeys, Action<T> onRemove)
        {
            List<Tuple<int, int>> removedKeys = new List<Tuple<int, int>>();
            foreach (Tuple<int, int> key in cache.Keys)
            {
                if (!seenKeys.Contains(key))
                {
                    removedKeys.Add(key);
                }
            }

            for (int i = 0; i < removedKeys.Count; i++)
            {
                Tuple<int, int> key = removedKeys[i];
                T removed = cache[key];
                onRemove(removed);
                cache.Remove(key);
            }
        }

        private void ResetSemanticState()
        {
            factories.Clear();
            resources.Clear();
            computeCenters.Clear();
            lastProcessedFrame = -1;
            hasPrimedFrame = false;
        }

        private void AddLog(string message, string level)
        {
            entries.Add(new EventLogEntry(DateTime.Now, level, message));
            while (entries.Count > MaxEntries)
            {
                entries.RemoveAt(0);
            }

            contentDirty = true;
        }

        private void RefreshContentIfNeeded()
        {
            if (!contentDirty || contentText == null || contentRect == null || scrollRect == null)
            {
                return;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(FormatLogEntry(entries[i]));
            }

            bool shouldStickToBottom = stickToBottom || scrollRect.verticalNormalizedPosition <= AutoScrollBottomThreshold;
            contentText.text = builder.ToString();
            float minHeight = viewportRect != null ? viewportRect.rect.height : 160f;
            float preferredHeight = Mathf.Max(minHeight, contentText.preferredHeight + 8f);
            contentRect.sizeDelta = new Vector2(0f, preferredHeight);
            contentText.rectTransform.sizeDelta = new Vector2(0f, preferredHeight);
            Canvas.ForceUpdateCanvases();
            if (shouldStickToBottom)
            {
                scrollRect.verticalNormalizedPosition = 0f;
                stickToBottom = true;
            }

            contentDirty = false;
        }

        private static string FormatLogEntry(EventLogEntry entry)
        {
            string timeText = entry.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            string level = string.IsNullOrEmpty(entry.Level) ? "INFO" : entry.Level.ToUpperInvariant();
            return $"<color=#8FA4B3>{timeText}</color> {FormatLevel(level)} {EscapeRichText(entry.Message)}";
        }

        private static string FormatLevel(string level)
        {
            string color = level switch
            {
                "SUCCESS" => "#6EEA80",
                "WARNING" => "#FFB54A",
                _ => "#83D8F0"
            };
            return $"<b><color={color}>[{level}]</color></b>";
        }

        private static string EscapeRichText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Replace("<", "＜").Replace(">", "＞");
        }

        private static long ResolveKnownFactoryTeamId(FactorySnapshot previous, FactorySnapshot current)
        {
            if (IsKnownTeam(current.TeamId))
            {
                return current.TeamId;
            }

            return IsKnownTeam(previous.TeamId) ? previous.TeamId : 0;
        }

        private static string GetTeamName(long teamId)
        {
            return teamId switch
            {
                1 => "队伍 1",
                2 => "队伍 2",
                3 => "队伍 3",
                4 => "队伍 4",
                0 => "未归属",
                _ => "未知队伍"
            };
        }

        private static bool IsKnownTeam(long teamId)
        {
            return teamId >= 1 && teamId <= 4;
        }

        private static RectTransform FindOrCreateRect(Transform parent, string name, params Type[] components)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                RectTransform existingRect = existing.GetComponent<RectTransform>();
                return existingRect != null ? existingRect : existing.gameObject.AddComponent<RectTransform>();
            }

            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            for (int i = 0; i < components.Length; i++)
            {
                Type componentType = components[i];
                if (componentType != null && go.GetComponent(componentType) == null)
                {
                    go.AddComponent(componentType);
                }
            }

            return go.GetComponent<RectTransform>();
        }

        private static void SetChildRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 pivot)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static Font GetUiFont()
        {
            if (cachedFont != null)
            {
                return cachedFont;
            }

            cachedFont = Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular");
            if (cachedFont == null)
            {
                try
                {
                    cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                catch
                {
                    cachedFont = null;
                }
            }

            return cachedFont;
        }

        private static void EnsureTextShadow(Text text, Color color, Vector2 distance)
        {
            if (text == null)
            {
                return;
            }

            Shadow shadow = text.GetComponent<Shadow>();
            if (shadow == null)
            {
                shadow = text.gameObject.AddComponent<Shadow>();
            }

            shadow.effectColor = color;
            shadow.effectDistance = distance;
            shadow.useGraphicAlpha = true;
        }

        private readonly struct EventLogEntry
        {
            public EventLogEntry(DateTime timestamp, string level, string message)
            {
                Timestamp = timestamp;
                Level = level;
                Message = message;
            }

            public DateTime Timestamp { get; }
            public string Level { get; }
            public string Message { get; }
        }

        private readonly struct FactorySnapshot
        {
            public FactorySnapshot(long factoryId, long teamId, int x, int y, int hp)
            {
                FactoryId = factoryId;
                TeamId = teamId;
                X = x;
                Y = y;
                Hp = hp;
            }

            public long FactoryId { get; }
            public long TeamId { get; }
            public int X { get; }
            public int Y { get; }
            public int Hp { get; }
        }

        private readonly struct ResourceSnapshot
        {
            public ResourceSnapshot(long id, int x, int y, int remainingAmount)
            {
                Id = id;
                X = x;
                Y = y;
                RemainingAmount = remainingAmount;
            }

            public long Id { get; }
            public int X { get; }
            public int Y { get; }
            public int RemainingAmount { get; }
        }

        private readonly struct CenterSnapshot
        {
            public CenterSnapshot(long centerId, int x, int y, long ownerTeamId)
            {
                CenterId = centerId;
                X = x;
                Y = y;
                OwnerTeamId = ownerTeamId;
            }

            public long CenterId { get; }
            public int X { get; }
            public int Y { get; }
            public long OwnerTeamId { get; }
        }
    }
}
