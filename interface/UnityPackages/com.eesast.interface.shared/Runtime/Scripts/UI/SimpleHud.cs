using System;
using Protobuf;
using THUAI9.Unity.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace THUAI9.Unity.UI.Shared
{
    public sealed class SimpleHud
    {
        private const string FontPath = "Fonts/NotoSansCJKsc-Regular";
        private readonly Text[] teamTexts = new Text[4];
        private readonly Font font;
        public Canvas Canvas { get; }
        public Text StatusText { get; private set; }
        public Text TimeText { get; private set; }

        public SimpleHud(string title)
        {
            EnsureEventSystem();
            Canvas = EnsureCanvas();
            font = LoadFont();
            HideLegacyModeObjects();
            BuildCommon(title);
        }

        public RectTransform AddPanel(string name, Vector2 anchored, Vector2 size)
        {
            GameObject go = GameObject.Find(name) ?? new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(Canvas.transform, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.026f, 0.043f, 0.065f, 0.92f);
            return rt;
        }

        public Text Label(Transform parent, string name, string text, Vector2 anchored, Vector2 size, int fontSize = 14, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            GameObject go = GameObject.Find(name) ?? new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = size;
            Text label = go.GetComponent<Text>();
            label.text = text;
            label.font = font;
            label.fontSize = fontSize;
            label.alignment = anchor;
            label.color = new Color(0.88f, 0.94f, 0.98f, 1f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        public Button Button(Transform parent, string name, string text, Vector2 anchored, Vector2 size, Color color, UnityEngine.Events.UnityAction action)
        {
            GameObject go = GameObject.Find(name) ?? new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = color;
            Button button = go.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
            Label(go.transform, name + "Text", text, Vector2.zero, size, 14, TextAnchor.MiddleCenter);
            return button;
        }

        public InputField Input(Transform parent, string name, string value, string placeholder, Vector2 anchored, Vector2 size)
        {
            GameObject go = GameObject.Find(name) ?? new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.04f, 0.08f, 0.12f, 1f);
            InputField input = go.GetComponent<InputField>();
            Text text = Label(go.transform, name + "Text", value, new Vector2(8f, -2f), new Vector2(size.x - 16f, size.y - 4f), 14, TextAnchor.MiddleLeft);
            Text ph = Label(go.transform, name + "Placeholder", placeholder, new Vector2(8f, -2f), new Vector2(size.x - 16f, size.y - 4f), 14, TextAnchor.MiddleLeft);
            ph.color = new Color(0.45f, 0.55f, 0.62f, 1f);
            input.textComponent = text;
            input.placeholder = ph;
            input.text = value;
            return input;
        }

        public Slider Slider(Transform parent, string name, Vector2 anchored, Vector2 size)
        {
            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                go = DefaultControls.CreateSlider(new DefaultControls.Resources());
                go.name = name;
            }

            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = size;
            Slider slider = go.GetComponent<Slider>();
            slider.targetGraphic = slider.targetGraphic ?? go.GetComponentInChildren<Graphic>();
            slider.wholeNumbers = true;
            SetChildGraphicColor(go.transform, "Background", new Color(0.12f, 0.18f, 0.24f, 1f));
            SetChildGraphicColor(go.transform, "Fill", new Color(0.24f, 0.74f, 0.92f, 1f));
            SetChildGraphicColor(go.transform, "Handle", new Color(0.86f, 0.96f, 1f, 1f));
            return slider;
        }

        public Dropdown Dropdown(Transform parent, string name, Vector2 anchored, Vector2 size, params string[] options)
        {
            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                go = DefaultControls.CreateDropdown(new DefaultControls.Resources());
                go.name = name;
            }

            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.18f, 1f);
            Dropdown dropdown = go.GetComponent<Dropdown>();
            StyleDropdownVisuals(go, dropdown);
            Text caption = FindChildText(go.transform, "Label")
                ?? Label(go.transform, name + "Label", options.Length > 0 ? options[0] : string.Empty, Vector2.zero, size, 14, TextAnchor.MiddleCenter);
            ConfigureText(caption, 14, TextAnchor.MiddleCenter);
            dropdown.captionText = caption;
            Text itemLabel = FindChildText(go.transform, "Item Label");
            if (itemLabel != null)
            {
                ConfigureText(itemLabel, 14, TextAnchor.MiddleLeft);
            }

            dropdown.ClearOptions();
            dropdown.AddOptions(new System.Collections.Generic.List<string>(options));
            dropdown.value = Mathf.Clamp(dropdown.value, 0, Mathf.Max(0, options.Length - 1));
            dropdown.RefreshShownValue();
            return dropdown;
        }

        public void UpdateCommon(int milliseconds)
        {
            TimeSpan time = TimeSpan.FromMilliseconds(Mathf.Max(0, CoreParam.ClampDisplayGameMilliseconds(milliseconds)));
            TimeText.text = string.Format("{0:00}:{1:00}", (int)time.TotalMinutes, time.Seconds);
            for (int i = 0; i < teamTexts.Length; i++)
            {
                long teamId = i + 1;
                CoreParam.teams.TryGetValue(teamId, out MessageOfTeam team);
                teamTexts[i].text = string.Format("队伍 {0}\n得分：{1}\n原料：{2}    算力：{3}", teamId, team != null ? team.Score.ToString() : "0", team != null ? team.Material.ToString() : "--", team != null ? team.ComputePower.ToString() : "--");
            }
        }

        private void BuildCommon(string title)
        {
            RectTransform top = AddPanel("HUD_TopBar", Vector2.zero, new Vector2(0f, 76f));
            top.anchorMin = new Vector2(0f, 1f);
            top.anchorMax = new Vector2(1f, 1f);
            top.pivot = new Vector2(0.5f, 1f);
            Label(top, "HUD_TitleText", title, new Vector2(24f, -14f), new Vector2(640f, 48f), 26, TextAnchor.MiddleLeft);
            TimeText = Label(top, "GameTimeText", "00:00", new Vector2(850f, -14f), new Vector2(220f, 48f), 30, TextAnchor.MiddleCenter);
            StatusText = Label(Canvas.transform, "StatusText", string.Empty, new Vector2(24f, -520f), new Vector2(620f, 120f), 16, TextAnchor.UpperLeft);
            for (int i = 0; i < teamTexts.Length; i++)
            {
                RectTransform card = AddPanel("TeamStatusCard" + (i + 1), new Vector2(-320f, -108f - i * 112f), new Vector2(292f, 98f));
                card.anchorMin = card.anchorMax = new Vector2(1f, 1f);
                card.pivot = new Vector2(1f, 1f);
                teamTexts[i] = Label(card, "TeamScoreText" + (i + 1), "队伍 " + (i + 1), new Vector2(14f, -10f), new Vector2(264f, 78f), 15, TextAnchor.UpperLeft);
            }
        }

        private static Canvas EnsureCanvas()
        {
            Canvas found = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (found != null)
            {
                ConfigureCanvas(found);
                return found;
            }

            GameObject go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = go.GetComponent<Canvas>();
            ConfigureCanvas(canvas);
            return canvas;
        }

        private static void ConfigureCanvas(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>() ?? canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        private static void HideLegacyModeObjects()
        {
            string[] names =
            {
                "PlayButton",
                "PauseButton",
                "StopButton",
                "SpeedDropdown",
                "ReplayPathInput",
                "BrowseReplayButton",
                "LoadReplayButton",
                "ReplayProgressSlider",
                "RecentReplayDropdown",
                "ProgressSlider",
                "ServerAddressInput",
                "ConnectLiveButton",
                "DisconnectLiveButton",
                "StartTrialButton",
                "HUD_SourcePanel",
                "HUD_ControlPanel",
                "HUD_PlayerPanel",
                "HUD_PlayerPanelToggle",
                "ScorePanel"
            };

            foreach (string name in names)
            {
                GameObject go = GameObject.Find(name);
                if (go != null)
                {
                    go.SetActive(false);
                }
            }
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static Font LoadFont()
        {
            Font font = Resources.Load<Font>(FontPath);
            if (font != null) return font;
            try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { return null; }
        }

        private void ConfigureText(Text text, int fontSize, TextAnchor anchor)
        {
            if (text == null) return;
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = new Color(0.88f, 0.94f, 0.98f, 1f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void StyleDropdownVisuals(GameObject dropdownObject, Dropdown dropdown)
        {
            if (dropdownObject == null || dropdown == null) return;

            ColorBlock colors = dropdown.colors;
            colors.normalColor = new Color(0.08f, 0.13f, 0.18f, 1f);
            colors.highlightedColor = new Color(0.16f, 0.30f, 0.42f, 1f);
            colors.pressedColor = new Color(0.20f, 0.40f, 0.56f, 1f);
            colors.selectedColor = new Color(0.13f, 0.24f, 0.34f, 1f);
            colors.disabledColor = new Color(0.05f, 0.07f, 0.09f, 0.75f);
            dropdown.colors = colors;

            SetChildGraphicColor(dropdownObject.transform, "Template", new Color(0.04f, 0.07f, 0.10f, 0.98f));
            SetChildGraphicColor(dropdownObject.transform, "Viewport", new Color(0.04f, 0.07f, 0.10f, 0.98f));
            SetChildGraphicColor(dropdownObject.transform, "Item Background", new Color(0.07f, 0.12f, 0.17f, 1f));
            SetChildGraphicColor(dropdownObject.transform, "Item Checkmark", new Color(0.24f, 0.74f, 0.92f, 1f));
            SetChildGraphicColor(dropdownObject.transform, "Scrollbar", new Color(0.05f, 0.09f, 0.13f, 1f));
            SetChildGraphicColor(dropdownObject.transform, "Sliding Area", new Color(0.05f, 0.09f, 0.13f, 1f));
            SetChildGraphicColor(dropdownObject.transform, "Handle", new Color(0.24f, 0.74f, 0.92f, 1f));

            Text[] texts = dropdownObject.GetComponentsInChildren<Text>(true);
            foreach (Text text in texts)
            {
                ConfigureText(text, 14, text.name == "Item Label" ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
                text.color = new Color(0.90f, 0.96f, 1f, 1f);
            }

            Toggle[] toggles = dropdownObject.GetComponentsInChildren<Toggle>(true);
            foreach (Toggle toggle in toggles)
            {
                ColorBlock toggleColors = toggle.colors;
                toggleColors.normalColor = new Color(0.07f, 0.12f, 0.17f, 1f);
                toggleColors.highlightedColor = new Color(0.16f, 0.30f, 0.42f, 1f);
                toggleColors.pressedColor = new Color(0.20f, 0.40f, 0.56f, 1f);
                toggleColors.selectedColor = new Color(0.13f, 0.24f, 0.34f, 1f);
                toggle.colors = toggleColors;
            }
        }

        private static Text FindChildText(Transform root, string childName)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child.name == childName)
                {
                    return child.GetComponent<Text>();
                }
            }

            return null;
        }

        private static void SetChildGraphicColor(Transform root, string childName, Color color)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child.name == childName && child.TryGetComponent(out Graphic graphic))
                {
                    graphic.color = color;
                }
            }
        }
    }
}
