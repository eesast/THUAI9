using THUAI9.Unity.Core;
using THUAI9.Unity.Render;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace THUAI9.Unity.UI
{
    /// <summary>
    /// Runtime inspector/sidebar for replay and live debugging.
    ///
    /// The scene may already contain HUD_InspectorPanel/HUD_InspectorTitle/SelectionInfoText.
    /// This controller binds those first and only creates missing pieces at runtime, so it
    /// preserves the existing Unity scene work while adding THUAI7/8-style selected-object
    /// details and source/status telemetry.
    /// </summary>
    public class InspectorPanelController : MonoBehaviour
    {
        public Text titleText;
        public Text bodyText;
        public Text statusText;
        public bool autoCreatePanel = true;
        public bool reflowBoundTexts = true;
        public bool hideWhenNothingSelected = true;
        public float statusRefreshInterval = 0.15f;

        private GameObject panelObject;
        private WorldObjectInfo selectedObject;
        private Vector2Int? selectedTile;
        private string selectedTileText;
        private float nextStatusRefreshTime;

        private void Awake()
        {
            EnsurePanel();
            ClearSelection();
        }

        private void Update()
        {
            if (selectedObject != null)
            {
                RefreshSelectionBody();
            }

            if (Time.unscaledTime >= nextStatusRefreshTime)
            {
                nextStatusRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, statusRefreshInterval);
                RefreshStatus();
            }
        }

        public void ShowObject(WorldObjectInfo info)
        {
            if (info == null)
            {
                ClearSelection();
                return;
            }

            selectedObject = info;
            selectedTile = null;
            selectedTileText = null;
            SetPanelVisible(true);
            RefreshSelectionBody();
        }

        public void ShowTile(Vector2Int tile, string tileText)
        {
            selectedObject = null;
            selectedTile = tile;
            selectedTileText = tileText;
            SetPanelVisible(true);
            RefreshSelectionBody();
        }

        public void ClearSelection()
        {
            selectedObject = null;
            selectedTile = null;
            selectedTileText = null;

            if (titleText != null)
            {
                titleText.text = "对象详情";
            }

            if (bodyText != null)
            {
                bodyText.text = "点击地图上的单位、建筑、资源或地块查看详情。\nEsc 清除选择。";
            }

            RefreshStatus();
            if (hideWhenNothingSelected)
            {
                SetPanelVisible(false);
            }
        }

        private void RefreshSelectionBody()
        {
            if (titleText == null || bodyText == null)
            {
                return;
            }

            if (selectedObject != null)
            {
                titleText.text = string.IsNullOrWhiteSpace(selectedObject.title)
                    ? "对象详情"
                    : selectedObject.title;
                bodyText.text = $"{selectedObject.BuildDisplayText()}\n\n{BuildObjectCheck(selectedObject)}";
                return;
            }

            if (selectedTile.HasValue)
            {
                titleText.text = $"地块详情 ({selectedTile.Value.x}, {selectedTile.Value.y})";
                bodyText.text = $"{selectedTileText}\n\n{BuildTileCheck(selectedTile.Value)}";
                return;
            }

            ClearSelection();
        }

        private static string BuildObjectCheck(WorldObjectInfo info)
        {
            if (info == null)
            {
                return "对象状态：对象已销毁或不在当前帧。";
            }

            string bounds = info.TryGetBounds(out Bounds objectBounds)
                ? $"显示范围：中心 ({objectBounds.center.x:0.##}, {objectBounds.center.y:0.##})，大小 ({objectBounds.size.x:0.##}, {objectBounds.size.y:0.##})"
                : "显示范围：暂无";
            string active = info.isActiveAndEnabled ? "正在显示" : "当前隐藏";
            return $"显示状态：{active}，最后更新帧 {info.lastSeenFrame}\n{bounds}";
        }

        private static string BuildTileCheck(Vector2Int tile)
        {
            if (CoreParam.map == null)
            {
                return "地块状态：当前帧没有地图。";
            }

            bool inBounds = tile.x >= 0 && tile.y >= 0 && tile.x < CoreParam.map.Height && tile.y < CoreParam.map.Width;
            return inBounds
                ? $"地块状态：仍在地图范围内，地图 {CoreParam.map.Height}x{CoreParam.map.Width}"
                : $"地块状态：越界，地图 {CoreParam.map.Height}x{CoreParam.map.Width}";
        }

        private void RefreshStatus()
        {
            if (statusText == null)
            {
                return;
            }

            statusText.text =
                $"{FrameSourceHub.BuildDebugText()}\n" +
                $"对象：单位 {CoreParam.characters.Count}，工厂 {CoreParam.factories.Count}，算力中心 {CoreParam.computeCenters.Count}，资源 {CoreParam.resources.Count}\n" +
                $"可选对象：{WorldObjectInfo.ActiveInfos.Count}  地图：{FormatMapSize()}";
        }

        private static string FormatMapSize()
        {
            return CoreParam.map == null ? "未加载" : $"{CoreParam.map.Height}x{CoreParam.map.Width}";
        }

        private void EnsurePanel()
        {
            titleText ??= FindTextByName("HUD_InspectorTitle") ?? FindTextByName("InspectorTitleText");
            bodyText ??= FindTextByName("SelectionInfoText") ?? FindTextByName("InspectorBodyText");
            statusText ??= FindTextByName("InspectorStatusText");

            Transform panel = FindPanelTransform();
            if (panel == null && autoCreatePanel)
            {
                panel = CreatePanel();
            }
            panelObject = panel != null ? panel.gameObject : null;

            Font font = GetBuiltInUIFont();
            if (titleText == null && panel != null)
            {
                titleText = CreateText(panel, "HUD_InspectorTitle", "对象详情", font, 22, FontStyle.Bold, TextAnchor.UpperLeft);
            }

            if (bodyText == null && panel != null)
            {
                bodyText = CreateText(panel, "SelectionInfoText", string.Empty, font, 17, FontStyle.Normal, TextAnchor.UpperLeft);
            }

            if (statusText == null && panel != null)
            {
                statusText = CreateText(panel, "InspectorStatusText", string.Empty, font, 15, FontStyle.Normal, TextAnchor.UpperLeft);
            }

            ReparentText(titleText, panel);
            ReparentText(bodyText, panel);
            ReparentText(statusText, panel);

            ConfigureText(titleText, new Color(0.98f, 0.94f, 0.72f, 1f));
            ConfigureText(bodyText, new Color(0.88f, 0.94f, 0.98f, 1f));
            ConfigureText(statusText, new Color(0.72f, 0.86f, 0.76f, 1f));

            if (reflowBoundTexts && panel is RectTransform)
            {
                ReflowPanelTexts();
            }
        }

        private Transform FindPanelTransform()
        {
            GameObject panelObject = GameObject.Find("HUD_InspectorPanel")
                ?? GameObject.Find("InspectorSidebarPanel")
                ?? GameObject.Find("InspectorPanel");
            if (panelObject != null)
            {
                return panelObject.transform;
            }

            if (titleText != null)
            {
                return titleText.transform.parent;
            }

            if (bodyText != null)
            {
                return bodyText.transform.parent;
            }

            return null;
        }

        private static Transform CreatePanel()
        {
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject("Canvas");
                canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObject.AddComponent<CanvasScaler>();
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            EnsureEventSystem();

            GameObject panelObject = new GameObject("InspectorSidebarPanel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvas.transform, false);
            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -466f);
            rect.sizeDelta = new Vector2(460f, 230f);

            Image image = panelObject.GetComponent<Image>();
            image.color = new Color(0.055f, 0.085f, 0.115f, 0.86f);
            image.raycastTarget = false;
            return panelObject.transform;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        private void ReflowPanelTexts()
        {
            RectTransform panelRect = FindPanelTransform() as RectTransform;
            if (panelRect != null)
            {
                panelRect.anchorMin = new Vector2(1f, 1f);
                panelRect.anchorMax = new Vector2(1f, 1f);
                panelRect.pivot = new Vector2(1f, 1f);
                panelRect.anchoredPosition = new Vector2(-24f, -466f);
                panelRect.sizeDelta = new Vector2(460f, 230f);
            }

            if (titleText != null)
            {
                titleText.fontSize = Mathf.Max(titleText.fontSize, 22);
                PlaceTopStretch(titleText.rectTransform, 18f, 14f, 18f, 38f);
            }

            if (bodyText != null)
            {
                bodyText.fontSize = Mathf.Max(bodyText.fontSize, 17);
                PlaceTopStretch(bodyText.rectTransform, 18f, 64f, 18f, 74f);
            }

            if (statusText != null)
            {
                statusText.fontSize = Mathf.Max(statusText.fontSize, 15);
                PlaceTopStretch(statusText.rectTransform, 18f, 150f, 18f, 56f);
            }
        }

        private void SetPanelVisible(bool visible)
        {
            if (panelObject != null && panelObject.activeSelf != visible)
            {
                panelObject.SetActive(visible);
            }
        }

        private static void ReparentText(Text text, Transform panel)
        {
            if (text == null || panel == null || text.transform.parent == panel)
            {
                return;
            }

            text.transform.SetParent(panel, false);
        }

        private static Text CreateText(Transform parent, string name, string value, Font font, int fontSize, FontStyle style, TextAnchor anchor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.text = value;
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = false;
            text.lineSpacing = 1.05f;
            return text;
        }

        private static void ConfigureText(Text text, Color color)
        {
            if (text == null)
            {
                return;
            }

            if (text.font == null)
            {
                text.font = GetBuiltInUIFont();
            }

            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = false;
            text.lineSpacing = 1.05f;
        }

        private static void PlaceTopStretch(RectTransform rect, float left, float top, float right, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(-(left + right), height);
        }

        private static Text FindTextByName(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            return go != null ? go.GetComponent<Text>() : null;
        }

        private static Font GetBuiltInUIFont()
        {
            try
            {
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null)
                {
                    return font;
                }
            }
            catch
            {
            }

            try
            {
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch
            {
                return null;
            }
        }
    }
}
