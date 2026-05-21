using System;
using System.Collections.Generic;
using THUAI9.Unity.Render;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace THUAI9.Unity.UI.Shared
{
    /// <summary>
    /// Shared hover tooltip for Live / Playback / Trial.
    /// It mirrors the original Unity hover inspector and reads the same
    /// WorldObjectInfo metadata populated by RenderManager.
    /// </summary>
    public sealed class WorldHoverInfoPanel : MonoBehaviour
    {
        private const string PanelName = "HUD_HoverInfoPanel";
        private const string ControllerName = "HUD_HoverInfoPanelController";
        private const string FontPath = "Fonts/NotoSansCJKsc-Regular";
        private const int HoverInfoMaxCharsPerLine = 32;
        private const int HoverInfoMaxBodyLines = 11;
        private const float HoverInfoWidth = 360f;
        private const float HoverInfoHeight = 248f;
        private static readonly Vector2 HoverInfoOffset = new Vector2(18f, -18f);

        private Canvas rootCanvas;
        private Camera targetCamera;
        private RectTransform hoverInfoPanel;
        private Text titleText;
        private Text bodyText;
        private WorldObjectInfo lastHoverInfo;
        private int lastHoverFrame;
        private string lastHoverTitle;
        private string lastHoverDetail;

        public bool ShowWorldHoverInfo { get; set; } = true;

        public static WorldHoverInfoPanel GetOrCreate(Canvas canvas, Camera camera = null)
        {
            WorldHoverInfoPanel existing = FindObjectOfType<WorldHoverInfoPanel>();
            if (existing == null)
            {
                GameObject go = GameObject.Find(ControllerName) ?? new GameObject(ControllerName);
                existing = go.GetComponent<WorldHoverInfoPanel>() ?? go.AddComponent<WorldHoverInfoPanel>();
            }

            existing.Configure(canvas, camera);
            return existing;
        }

        public void Configure(Canvas canvas, Camera camera = null)
        {
            if (canvas != null)
            {
                rootCanvas = canvas;
                transform.SetParent(canvas.transform, false);
            }

            if (camera != null)
            {
                targetCamera = camera;
            }

            EnsureHoverInfoPanel();
        }

        private void Update()
        {
            if (!ShowWorldHoverInfo)
            {
                lastHoverInfo = null;
                SetHoverInfoVisible(false);
                return;
            }

            if (rootCanvas == null)
            {
                rootCanvas = FindObjectOfType<Canvas>();
            }

            if (hoverInfoPanel == null || titleText == null || bodyText == null)
            {
                EnsureHoverInfoPanel();
            }

            if (rootCanvas == null || hoverInfoPanel == null)
            {
                return;
            }

            WorldObjectInfo info = FindWorldInfoUnderMouse();
            if (info == null || IsPointerOverAnyUiControl())
            {
                lastHoverInfo = null;
                SetHoverInfoVisible(false);
                return;
            }

            UpdateHoverInfoTextIfNeeded(info);
            PositionHoverInfoPanel();
            SetHoverInfoVisible(true);
        }

        private void EnsureHoverInfoPanel()
        {
            if (rootCanvas == null)
            {
                return;
            }

            Font font = LoadFont();
            GameObject existing = GameObject.Find(PanelName);
            hoverInfoPanel = existing != null ? existing.GetComponent<RectTransform>() : null;
            if (hoverInfoPanel == null)
            {
                GameObject panelObject = new GameObject(PanelName, typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
                hoverInfoPanel = panelObject.GetComponent<RectTransform>();
            }

            hoverInfoPanel.SetParent(rootCanvas.transform, false);
            hoverInfoPanel.SetAsLastSibling();
            hoverInfoPanel.anchorMin = Vector2.zero;
            hoverInfoPanel.anchorMax = Vector2.zero;
            hoverInfoPanel.pivot = new Vector2(0f, 1f);
            hoverInfoPanel.sizeDelta = new Vector2(HoverInfoWidth, HoverInfoHeight);

            Image image = hoverInfoPanel.GetComponent<Image>() ?? hoverInfoPanel.gameObject.AddComponent<Image>();
            image.color = new Color(0.018f, 0.028f, 0.042f, 0.95f);
            image.raycastTarget = false;

            CanvasGroup group = hoverInfoPanel.GetComponent<CanvasGroup>() ?? hoverInfoPanel.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            titleText = FindOrCreateText(hoverInfoPanel, "HUD_HoverInfoTitle", font);
            SetRect(titleText.rectTransform, new Vector2(14f, -12f), new Vector2(HoverInfoWidth - 28f, 32f));
            ConfigureText(titleText, 17, FontStyle.Bold, new Color(0.30f, 0.88f, 0.98f, 1f));

            bodyText = FindOrCreateText(hoverInfoPanel, "HUD_HoverInfoBody", font);
            SetRect(bodyText.rectTransform, new Vector2(14f, -48f), new Vector2(HoverInfoWidth - 28f, HoverInfoHeight - 60f));
            ConfigureText(bodyText, 14, FontStyle.Normal, new Color(0.88f, 0.94f, 0.98f, 1f));

            SetHoverInfoVisible(false);
        }

        private WorldObjectInfo FindWorldInfoUnderMouse()
        {
            Camera camera = targetCamera != null ? targetCamera : Camera.main;
            if (camera == null)
            {
                return null;
            }

            Vector3 worldPosition = camera.ScreenToWorldPoint(Input.mousePosition);
            worldPosition.z = 0f;

            WorldObjectInfo best = null;
            float bestArea = float.MaxValue;
            foreach (WorldObjectInfo info in WorldObjectInfo.ActiveInfos)
            {
                if (info == null || !info.isActiveAndEnabled || !info.TryGetBounds(out Bounds bounds))
                {
                    continue;
                }

                bounds.Expand(0.12f);
                if (!bounds.Contains(worldPosition))
                {
                    continue;
                }

                float area = Mathf.Max(bounds.size.x * bounds.size.y, 0.001f);
                if (area < bestArea)
                {
                    best = info;
                    bestArea = area;
                }
            }

            return best;
        }

        private void UpdateHoverInfoTextIfNeeded(WorldObjectInfo info)
        {
            string title = string.IsNullOrWhiteSpace(info.title) ? info.objectType : info.title;
            string detail = info.detail ?? string.Empty;
            if (ReferenceEquals(info, lastHoverInfo) &&
                info.lastSeenFrame == lastHoverFrame &&
                string.Equals(title, lastHoverTitle, StringComparison.Ordinal) &&
                string.Equals(detail, lastHoverDetail, StringComparison.Ordinal))
            {
                return;
            }

            lastHoverInfo = info;
            lastHoverFrame = info.lastSeenFrame;
            lastHoverTitle = title;
            lastHoverDetail = detail;
            titleText.text = title;
            bodyText.text = BuildHoverBodyText(info);
        }

        private static string BuildHoverBodyText(WorldObjectInfo info)
        {
            List<string> lines = new List<string>();
            string teamLabel = FormatTeamLabel(info.teamId);
            if (!string.IsNullOrEmpty(teamLabel))
            {
                lines.Add($"队伍：{teamLabel}");
            }

            if (info.guid != 0)
            {
                lines.Add($"ID：{info.guid}");
            }

            if (info.gridX >= 0 && info.gridY >= 0)
            {
                lines.Add($"坐标：({info.gridX}, {info.gridY})");
            }

            if (info.lastSeenFrame > 0)
            {
                lines.Add($"最后更新帧：{info.lastSeenFrame}");
            }

            if (!string.IsNullOrWhiteSpace(info.detail))
            {
                string[] detailLines = info.detail.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                for (int i = 0; i < detailLines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(detailLines[i]))
                    {
                        lines.Add(detailLines[i].Trim());
                    }
                }
            }

            return BuildBoundedHoverText(lines);
        }

        private static string FormatTeamLabel(long teamId)
        {
            if (teamId >= 1 && teamId <= 4)
            {
                return $"队伍 {teamId}";
            }

            if (teamId == long.MaxValue)
            {
                return "未归属";
            }

            return string.Empty;
        }

        private static string BuildBoundedHoverText(IEnumerable<string> sourceLines)
        {
            List<string> output = new List<string>();
            foreach (string rawLine in sourceLines)
            {
                foreach (string wrapped in WrapHoverLine(rawLine))
                {
                    if (output.Count >= HoverInfoMaxBodyLines)
                    {
                        output[HoverInfoMaxBodyLines - 1] = "…";
                        return string.Join("\n", output);
                    }

                    output.Add(wrapped);
                }
            }

            return string.Join("\n", output);
        }

        private static IEnumerable<string> WrapHoverLine(string line)
        {
            string safeLine = string.IsNullOrWhiteSpace(line) ? string.Empty : line.Trim();
            if (safeLine.Length <= HoverInfoMaxCharsPerLine)
            {
                yield return safeLine;
                yield break;
            }

            for (int start = 0; start < safeLine.Length; start += HoverInfoMaxCharsPerLine)
            {
                int length = Mathf.Min(HoverInfoMaxCharsPerLine, safeLine.Length - start);
                yield return safeLine.Substring(start, length);
            }
        }

        private void PositionHoverInfoPanel()
        {
            RectTransform canvasRect = rootCanvas != null ? rootCanvas.transform as RectTransform : null;
            if (canvasRect == null || hoverInfoPanel == null)
            {
                return;
            }

            Camera uiCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Input.mousePosition, uiCamera, out Vector2 localPoint))
            {
                return;
            }

            Vector2 canvasSize = canvasRect.rect.size;
            Vector2 bottomLeftPoint = localPoint + new Vector2(canvasSize.x * canvasRect.pivot.x, canvasSize.y * canvasRect.pivot.y);
            Vector2 position = bottomLeftPoint + HoverInfoOffset;
            const float margin = 10f;

            if (position.x + HoverInfoWidth > canvasSize.x - margin)
            {
                position.x = bottomLeftPoint.x - HoverInfoWidth - HoverInfoOffset.x;
            }

            position.x = Mathf.Clamp(position.x, margin, Mathf.Max(margin, canvasSize.x - HoverInfoWidth - margin));
            position.y = Mathf.Clamp(position.y, HoverInfoHeight + margin, Mathf.Max(HoverInfoHeight + margin, canvasSize.y - margin));
            hoverInfoPanel.anchoredPosition = position;
            hoverInfoPanel.SetAsLastSibling();
        }

        private bool IsPointerOverAnyUiControl()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private void SetHoverInfoVisible(bool visible)
        {
            if (hoverInfoPanel != null && hoverInfoPanel.gameObject.activeSelf != visible)
            {
                hoverInfoPanel.gameObject.SetActive(visible);
            }
        }

        private static Text FindOrCreateText(RectTransform parent, string name, Font font)
        {
            Transform existing = parent.Find(name);
            Text text = existing != null ? existing.GetComponent<Text>() : null;
            if (text == null)
            {
                GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
                go.transform.SetParent(parent, false);
                text = go.GetComponent<Text>();
            }

            text.font = font;
            text.raycastTarget = false;
            return text;
        }

        private static void SetRect(RectTransform rect, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = rect.anchorMax = Vector2.up;
            rect.pivot = Vector2.up;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void ConfigureText(Text text, int fontSize, FontStyle fontStyle, Color color)
        {
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = false;
            text.supportRichText = false;
            text.lineSpacing = 1.04f;
            AddShadow(text, new Color(0f, 0f, 0f, 0.62f), new Vector2(1f, -1f));
        }

        private static void AddShadow(Text text, Color color, Vector2 distance)
        {
            Shadow shadow = text.GetComponent<Shadow>() ?? text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = distance;
            shadow.useGraphicAlpha = true;
        }

        private static Font LoadFont()
        {
            Font font = Resources.Load<Font>(FontPath);
            if (font != null)
            {
                return font;
            }

            try
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch
            {
                return null;
            }
        }
    }
}
