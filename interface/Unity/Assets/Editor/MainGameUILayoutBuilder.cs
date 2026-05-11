using System.Collections.Generic;
using System.IO;
using THUAI9.Unity.CameraControlNS;
using THUAI9.Unity.Generated;
using THUAI9.Unity.Live;
using THUAI9.Unity.Playback;
using THUAI9.Unity.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class MainGameUILayoutBuilder
{
    private const string MainGameScenePath = "Assets/Scenes/MainGame.unity";

    private static readonly Color PanelColor = new Color(0.035f, 0.055f, 0.080f, 0.92f);
    private static readonly Color PanelSoftColor = new Color(0.055f, 0.085f, 0.115f, 0.86f);
    private static readonly Color Cyan = new Color(0.18f, 0.88f, 0.96f, 1f);
    private static readonly Color Gold = new Color(1.00f, 0.72f, 0.22f, 1f);
    private static readonly Color TextColor = new Color(0.88f, 0.94f, 0.98f, 1f);

    [MenuItem("Tools/UI/Rebuild MainGame HUD Layout")]
    public static void RebuildMainGameHud()
    {
        if (!File.Exists(MainGameScenePath))
        {
            Debug.LogError("[UI] Missing scene: " + MainGameScenePath);
            return;
        }

        EditorSceneManager.OpenScene(MainGameScenePath, OpenSceneMode.Single);
        Canvas canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[UI] MainGame scene has no Canvas.");
            return;
        }

        ConfigureCanvas(canvas);
        RectTransform canvasRt = canvas.GetComponent<RectTransform>();
        DeleteChildIfExists(canvasRt, "HUD_StatsPanel");
        DeleteChildIfExists(canvasRt, "HUD_InspectorPanel");
        DeleteChildIfExists(canvasRt, "InspectorSidebarPanel");
        DeleteChildIfExists(canvasRt, "InspectorPanel");
        DeleteObjectIfExists("FrameInfoText");
        DeleteObjectIfExists("PreviousFrameButton");
        DeleteObjectIfExists("NextFrameButton");
        RemoveLegacyInspectorComponents(canvas);

        RectTransform topBar = EnsurePanel(canvasRt, "HUD_TopBar", PanelColor);
        SetStretchTop(topBar, 86f, 0f);

        RectTransform scorePanel = EnsurePanel(canvasRt, "HUD_ScorePanel", PanelSoftColor);
        SetTopRight(scorePanel, new Vector2(-24f, -108f), new Vector2(460f, 420f));

        RectTransform eventPanel = EnsurePanel(canvasRt, "HUD_EventPanel", PanelSoftColor);
        SetChildRect(eventPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -108f), new Vector2(430f, 178f), new Vector2(0f, 1f));


        RectTransform controlPanel = EnsurePanel(canvasRt, "HUD_ControlPanel", PanelColor);
        SetBottomCenter(controlPanel, new Vector2(0f, 44f), new Vector2(860f, 120f));

        RectTransform sourcePanel = EnsurePanel(canvasRt, "HUD_SourcePanel", PanelColor);
        SetBottomCenter(sourcePanel, new Vector2(0f, 146f), new Vector2(740f, 98f));

        Button playerToggle = EnsureButton(canvasRt, "HUD_PlayerPanelToggle", "打开试玩面板", new Color(0.11f, 0.22f, 0.30f, 0.94f));
        SetChildRect(playerToggle.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 258f), new Vector2(128f, 32f), new Vector2(0f, 0f));

        controlPanel.SetAsFirstSibling();
        sourcePanel.SetAsFirstSibling();
        playerToggle.transform.SetAsFirstSibling();
        eventPanel.SetAsFirstSibling();
        scorePanel.SetAsFirstSibling();
        topBar.SetAsFirstSibling();

        Text title = EnsureText(topBar, "HUD_TitleText", "THUAI9  云厂竞逐战", 26, FontStyle.Bold, Gold, TextAnchor.MiddleLeft);
        SetChildRect(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(330f, 62f), new Vector2(0f, 0.5f));

        Text statusText = MoveText("StatusText", canvasRt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(350f, -43f), new Vector2(520f, 42f), new Vector2(0f, 0.5f), 15, TextAnchor.MiddleLeft);
        if (statusText != null) statusText.text = "状态：等待回放 / 预览模式";

        Text gameTimeText = MoveText("GameTimeText", canvasRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -43f), new Vector2(260f, 54f), new Vector2(0.5f, 0.5f), 28, TextAnchor.MiddleCenter);
        if (gameTimeText != null) gameTimeText.text = "00:00";

        Text gameStateText = EnsureText(topBar, "GameStateText", "对局：等待首帧", 18, FontStyle.Normal, TextColor, TextAnchor.MiddleRight);
        SetChildRect(gameStateText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-28f, 0f), new Vector2(470f, 42f), new Vector2(1f, 0.5f));

        Text scoreTitle = EnsureText(scorePanel, "HUD_ScoreTitle", "队伍状态", 22, FontStyle.Bold, Cyan, TextAnchor.MiddleLeft);
        SetChildRect(scoreTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -16f), new Vector2(-36f, 32f), new Vector2(0f, 1f));
        for (int i = 0; i < 4; i++)
        {
            Text score = MoveText($"TeamScoreText{i + 1}", canvasRt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-52f, -156f - i * 90f), new Vector2(376f, 82f), new Vector2(1f, 1f), 14, TextAnchor.MiddleLeft);
            if (score != null)
            {
                score.text = $"队伍 {i + 1}：等待首帧\n工厂生命 --，科技等级：暂无\n成员 uuid：等待角色创建";
                score.fontStyle = FontStyle.Bold;
                score.fontSize = 14;
                score.color = GetTeamAccentColor(i);
                score.alignment = TextAnchor.MiddleLeft;
                score.horizontalOverflow = HorizontalWrapMode.Wrap;
                score.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        Text eventTitle = EnsureText(eventPanel, "HUD_EventTitle", "工业呼吸 / AI 事件", 20, FontStyle.Bold, Cyan, TextAnchor.MiddleLeft);
        SetChildRect(eventTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -12f), new Vector2(-36f, 28f), new Vector2(0f, 1f));
        Text aiEventText = EnsureText(eventPanel, "AIEventText", "AI事件：暂无", 16, FontStyle.Normal, TextColor, TextAnchor.UpperLeft);
        SetChildRect(aiEventText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -48f), new Vector2(-36f, 76f), new Vector2(0f, 1f));
        Text aiEffectText = EnsureText(eventPanel, "AIEffectText", "世界修正：暂无", 16, FontStyle.Normal, TextColor, TextAnchor.UpperLeft);
        SetChildRect(aiEffectText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -124f), new Vector2(-36f, 46f), new Vector2(0f, 1f));

        LayoutControls(canvasRt);
        LayoutSourceControls(sourcePanel);
        WireRuntimeControllers(canvas, gameStateText, aiEventText, aiEffectText);
        ConfigureCamera();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("[UI] Rebuilt MainGame HUD layout with replay controls and event panel.");
    }

    private static void ConfigureCanvas(Canvas canvas)
    {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>() ?? canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    private static void LayoutControls(RectTransform panel)
    {
        RectTransform slider = MoveRect("ReplayProgressSlider", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(760f, 22f), new Vector2(0.5f, 1f));
        if (slider != null) StyleSlider(slider);

        DeleteObjectIfExists("FrameInfoText");

        DeleteObjectIfExists("PreviousFrameButton");
        DeleteObjectIfExists("NextFrameButton");
        RectTransform play = MoveRect("PlayButton", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-170f, 68f), new Vector2(108f, 42f), new Vector2(0.5f, 0f));
        RectTransform pause = MoveRect("PauseButton", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-43f, 68f), new Vector2(108f, 42f), new Vector2(0.5f, 0f));
        RectTransform stop = MoveRect("StopButton", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(84f, 68f), new Vector2(108f, 42f), new Vector2(0.5f, 0f));
        RectTransform speed = MoveRect("SpeedDropdown", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(222f, 68f), new Vector2(138f, 42f), new Vector2(0.5f, 0f));

        StyleButton(play, "播放", new Color(0.18f, 0.72f, 0.28f, 1f));
        StyleButton(pause, "暂停", new Color(0.88f, 0.72f, 0.18f, 1f));
        StyleButton(stop, "停止", new Color(0.82f, 0.18f, 0.18f, 1f));
        StyleDropdown(speed);
    }

    private static void LayoutSourceControls(RectTransform panel)
    {
        DeleteChildIfExists(panel, "HUD_SourceHintText");
        DeleteChildIfExists(panel, "HUD_RecentReplayLabel");
        DeleteChildIfExists(panel, "RecentReplayDropdown");

        Text replayLabel = EnsureText(panel, "HUD_ReplayPathLabel", "回放", 15, FontStyle.Bold, Cyan, TextAnchor.MiddleLeft);
        SetChildRect(replayLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -16f), new Vector2(64f, 30f), new Vector2(0f, 1f));

        InputField replayInput = EnsureInputField(panel, "ReplayPathInput", string.Empty, "选择 .thuaipb 或输入路径");
        replayInput.text = string.Empty;
        SetChildRect(replayInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -16f), new Vector2(420f, 30f), new Vector2(0f, 1f));

        Button browseButton = EnsureButton(panel, "BrowseReplayButton", "选择文件", new Color(0.20f, 0.48f, 0.72f, 1f));
        SetChildRect(browseButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(526f, -16f), new Vector2(94f, 30f), new Vector2(0f, 1f));

        Button loadButton = EnsureButton(panel, "LoadReplayButton", "加载", new Color(0.18f, 0.36f, 0.58f, 1f));
        SetChildRect(loadButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(630f, -16f), new Vector2(82f, 30f), new Vector2(0f, 1f));

        Text hint = EnsureText(panel, "ReplayHintText", string.Empty, 13, FontStyle.Normal, new Color(0.74f, 0.86f, 0.92f, 1f), TextAnchor.UpperLeft);
        hint.text = string.Empty;
        SetChildRect(hint.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(602f, -52f), new Vector2(116f, 34f), new Vector2(0f, 1f));

        Text liveLabel = EnsureText(panel, "HUD_LiveAddressLabel", "Live", 15, FontStyle.Bold, Cyan, TextAnchor.MiddleLeft);
        SetChildRect(liveLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -52f), new Vector2(64f, 30f), new Vector2(0f, 1f));

        InputField liveInput = EnsureInputField(panel, "ServerAddressInput", "127.0.0.1:8888", "server:port");
        SetChildRect(liveInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -52f), new Vector2(260f, 30f), new Vector2(0f, 1f));

        Button connectButton = EnsureButton(panel, "ConnectLiveButton", "连接", new Color(0.12f, 0.48f, 0.32f, 1f));
        SetChildRect(connectButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(366f, -52f), new Vector2(92f, 30f), new Vector2(0f, 1f));

        Button disconnectButton = EnsureButton(panel, "DisconnectLiveButton", "断开", new Color(0.48f, 0.18f, 0.18f, 1f));
        SetChildRect(disconnectButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(470f, -52f), new Vector2(118f, 30f), new Vector2(0f, 1f));
    }

    private static void ConfigureCamera()
    {
        Camera camera = Camera.main;
        if (camera == null) return;
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.015f, 0.020f, 0.032f, 1f);
        camera.transform.position = new Vector3(25f, 25f, -10f);
        camera.orthographicSize = 27.5f;
    }

    private static void WireRuntimeControllers(Canvas canvas, Text gameStateText, Text aiEventText, Text aiEffectText)
    {
        UIController ui = canvas.GetComponent<UIController>() ?? canvas.gameObject.AddComponent<UIController>();
        ui.autoBindSceneReferences = true;
        ui.gameStateText = gameStateText;
        ui.aiEventText = aiEventText;
        ui.aiEffectText = aiEffectText;
        ui.playbackPathInput = GameObject.Find("ReplayPathInput")?.GetComponent<InputField>();
        ui.browsePlaybackButton = GameObject.Find("BrowseReplayButton")?.GetComponent<Button>();
        ui.loadPlaybackButton = GameObject.Find("LoadReplayButton")?.GetComponent<Button>();
        ui.recentReplayDropdown = GameObject.Find("RecentReplayDropdown")?.GetComponent<Dropdown>();
        ui.replayHintText = GameObject.Find("ReplayHintText")?.GetComponent<Text>();
        ui.serverAddressInput = GameObject.Find("ServerAddressInput")?.GetComponent<InputField>();
        ui.connectLiveButton = GameObject.Find("ConnectLiveButton")?.GetComponent<Button>();
        ui.disconnectLiveButton = GameObject.Find("DisconnectLiveButton")?.GetComponent<Button>();

        WorldSelectionController selectionController = canvas.GetComponent<WorldSelectionController>() ?? canvas.gameObject.AddComponent<WorldSelectionController>();
        selectionController.targetCamera = Camera.main;
        PlaybackController playback = Object.FindObjectOfType<PlaybackController>();
        LiveSpectatorClient liveClient = Object.FindObjectOfType<LiveSpectatorClient>();
        if (liveClient == null)
        {
            liveClient = new GameObject("LiveSpectatorClient").AddComponent<LiveSpectatorClient>();
        }

        if (playback != null)
        {
            PlaybackInputHotkeys hotkeys = playback.GetComponent<PlaybackInputHotkeys>() ?? playback.gameObject.AddComponent<PlaybackInputHotkeys>();
            hotkeys.playbackController = playback;
            hotkeys.cameraControl = Object.FindObjectOfType<CameraControl>();
            hotkeys.liveClient = liveClient;
        }

        PixelDemoBootstrap demoBootstrap = Object.FindObjectOfType<PixelDemoBootstrap>();
        if (demoBootstrap != null)
        {
            demoBootstrap.showWhenPlaybackMissing = false;
            EditorUtility.SetDirty(demoBootstrap);
        }
    }

    private static void RemoveLegacyInspectorComponents(Canvas canvas)
    {
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(canvas.gameObject);
        foreach (MonoBehaviour component in canvas.GetComponents<MonoBehaviour>())
        {
            if (component != null && component.GetType().Name == "InspectorPanelController")
            {
                Object.DestroyImmediate(component);
            }
        }
    }

    private static RectTransform EnsurePanel(RectTransform parent, string name, Color color)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        if (existing == null) go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return go.GetComponent<RectTransform>();
    }

    private static void DeleteChildIfExists(RectTransform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }
    }

    private static void DeleteObjectIfExists(string name)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }
    }

    private static Text EnsureText(RectTransform parent, string name, string value, int fontSize, FontStyle style, Color color, TextAnchor anchor)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        if (existing == null) go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>() ?? go.AddComponent<Text>();
        text.text = value;
        ApplyTextStyle(text, fontSize, anchor, color, style);
        return text;
    }

    private static Button EnsureButton(RectTransform parent, string name, string label, Color color)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        if (existing == null) go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        image.color = color;
        Button button = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.25f);
        button.colors = colors;

        Text text = EnsureText(go.GetComponent<RectTransform>(), $"{name}Text", label, 15, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
        text.raycastTarget = false;
        return button;
    }

    private static InputField EnsureInputField(RectTransform parent, string name, string value, string placeholder)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
        if (existing == null) go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        image.color = new Color(0.08f, 0.115f, 0.155f, 0.98f);

        InputField input = go.GetComponent<InputField>() ?? go.AddComponent<InputField>();
        RectTransform rt = go.GetComponent<RectTransform>();

        Text text = EnsureText(rt, $"{name}Text", value, 14, FontStyle.Normal, new Color(0.92f, 0.97f, 1f, 1f), TextAnchor.MiddleLeft);
        Stretch(text.rectTransform, new Vector2(8f, 3f), new Vector2(8f, 3f));
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;

        Text placeholderText = EnsureText(rt, $"{name}Placeholder", placeholder, 14, FontStyle.Normal, new Color(0.55f, 0.66f, 0.74f, 0.85f), TextAnchor.MiddleLeft);
        Stretch(placeholderText.rectTransform, new Vector2(8f, 3f), new Vector2(8f, 3f));
        placeholderText.supportRichText = false;

        input.textComponent = text;
        input.placeholder = placeholderText;
        if (string.IsNullOrWhiteSpace(input.text))
        {
            input.text = value;
        }
        input.lineType = InputField.LineType.SingleLine;
        return input;
    }

    private static Dropdown EnsureDropdown(RectTransform parent, string name)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : DefaultControls.CreateDropdown(new DefaultControls.Resources());
        go.name = name;
        if (existing == null) go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        image.color = new Color(0.08f, 0.115f, 0.155f, 0.98f);

        Dropdown dropdown = go.GetComponent<Dropdown>() ?? go.AddComponent<Dropdown>();
        dropdown.ClearOptions();
        dropdown.AddOptions(new System.Collections.Generic.List<string> { "最近回放：暂无" });
        dropdown.interactable = false;

        if (dropdown.captionText != null)
        {
            ApplyTextStyle(dropdown.captionText, 14, TextAnchor.MiddleLeft, new Color(0.92f, 0.97f, 1f, 1f), FontStyle.Normal);
            dropdown.captionText.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        if (dropdown.itemText != null)
        {
            ApplyTextStyle(dropdown.itemText, 14, TextAnchor.MiddleLeft, Color.black, FontStyle.Normal);
        }

        return dropdown;
    }

    private static Text MoveText(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 pivot, int fontSize, TextAnchor alignment)
    {
        RectTransform rt = MoveRect(name, parent, anchorMin, anchorMax, anchoredPosition, sizeDelta, pivot);
        if (rt == null) return null;
        Text text = rt.GetComponent<Text>() ?? rt.gameObject.AddComponent<Text>();
        ApplyTextStyle(text, fontSize, alignment, TextColor, FontStyle.Normal);
        return text;
    }

    private static RectTransform MoveRect(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 pivot)
    {
        GameObject go = GameObject.Find(name);
        if (go == null) return null;
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt == null) return null;
        if (rt.parent == null)
        {
            rt.SetParent(parent, false);
        }
        SetChildRect(rt, anchorMin, anchorMax, anchoredPosition, sizeDelta, pivot);
        return rt;
    }

    private static void ApplyTextStyle(Text text, int fontSize, TextAnchor alignment, Color color, FontStyle style)
    {
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.fontStyle = style;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = false;
        text.lineSpacing = 1.05f;
    }

    private static void StyleButton(RectTransform rt, string label, Color color)
    {
        if (rt == null) return;
        Image image = rt.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
        image.color = color;
        Button button = rt.GetComponent<Button>() ?? rt.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.25f);
        button.colors = colors;
        Text text = rt.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.text = label;
            ApplyTextStyle(text, 18, TextAnchor.MiddleCenter, Color.black, FontStyle.Bold);
            text.raycastTarget = false;
        }
    }

    private static void StyleDropdown(RectTransform rt)
    {
        if (rt == null) return;
        Dropdown dropdown = rt.GetComponent<Dropdown>();
        if (dropdown != null)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string> { "0.5x", "1x", "2x", "4x" });
            dropdown.SetValueWithoutNotify(1);
            dropdown.interactable = true;
            dropdown.RefreshShownValue();
        }

        Image image = rt.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
        image.color = new Color(0.075f, 0.105f, 0.145f, 0.98f);
        Text label = rt.Find("Label")?.GetComponent<Text>();
        if (label != null)
        {
            ApplyTextStyle(label, 18, TextAnchor.MiddleCenter, TextColor, FontStyle.Bold);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        RectTransform template = rt.Find("Template") as RectTransform;
        if (template != null)
        {
            template.sizeDelta = new Vector2(rt.sizeDelta.x, 232f);
            Image templateImage = template.GetComponent<Image>();
            if (templateImage != null) templateImage.color = new Color(0.035f, 0.052f, 0.075f, 0.98f);
        }

        Text itemLabel = rt.Find("Template/Viewport/Content/Item/Item Label")?.GetComponent<Text>();
        if (itemLabel != null)
        {
            ApplyTextStyle(itemLabel, 18, TextAnchor.MiddleLeft, TextColor, FontStyle.Bold);
            itemLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        RectTransform item = rt.Find("Template/Viewport/Content/Item") as RectTransform;
        if (item != null) item.sizeDelta = new Vector2(0f, 36f);
    }

    private static void StyleSlider(RectTransform rt)
    {
        if (rt == null) return;
        Slider slider = rt.GetComponent<Slider>();
        if (slider == null) return;
        if (slider.fillRect != null)
        {
            Image fill = slider.fillRect.GetComponent<Image>();
            if (fill != null) fill.color = Cyan;
        }
        if (slider.targetGraphic != null) slider.targetGraphic.color = Gold;
    }

    private static Color GetTeamAccentColor(int teamIndex)
    {
        return teamIndex switch
        {
            0 => new Color(1.00f, 0.06f, 0.06f, 1f),
            1 => new Color(0.08f, 1.00f, 0.12f, 1f),
            2 => new Color(0.06f, 0.34f, 1.00f, 1f),
            _ => new Color(1.00f, 0.95f, 0.06f, 1f)
        };
    }

    private static void SetStretchTop(RectTransform rt, float height, float y)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -y);
        rt.sizeDelta = new Vector2(0f, height);
    }

    private static void SetTopRight(RectTransform rt, Vector2 anchoredPosition, Vector2 size)
    {
        SetChildRect(rt, new Vector2(1f, 1f), new Vector2(1f, 1f), anchoredPosition, size, new Vector2(1f, 1f));
    }

    private static void SetBottomCenter(RectTransform rt, Vector2 anchoredPosition, Vector2 size)
    {
        SetChildRect(rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), anchoredPosition, size, new Vector2(0.5f, 0f));
    }

    private static void SetBottomLeft(RectTransform rt, Vector2 anchoredPosition, Vector2 size)
    {
        SetChildRect(rt, new Vector2(0f, 0f), new Vector2(0f, 0f), anchoredPosition, size, new Vector2(0f, 0f));
    }

    private static void SetChildRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 pivot)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;
    }

    private static void Stretch(RectTransform rt, Vector2 minOffset, Vector2 maxOffset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = minOffset;
        rt.offsetMax = -maxOffset;
    }
}
