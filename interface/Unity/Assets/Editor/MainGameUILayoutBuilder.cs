using System.IO;
using THUAI9.Unity.CameraControlNS;
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

        RectTransform topBar = EnsurePanel(canvasRt, "HUD_TopBar", PanelColor);
        SetStretchTop(topBar, 86f, 0f);

        RectTransform scorePanel = EnsurePanel(canvasRt, "HUD_ScorePanel", PanelSoftColor);
        SetTopRight(scorePanel, new Vector2(-24f, -108f), new Vector2(380f, 220f));

        RectTransform eventPanel = EnsurePanel(canvasRt, "HUD_EventPanel", PanelSoftColor);
        SetChildRect(eventPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -108f), new Vector2(430f, 178f), new Vector2(0f, 1f));

        RectTransform inspectorPanel = EnsurePanel(canvasRt, "HUD_InspectorPanel", PanelSoftColor);
        SetChildRect(inspectorPanel, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, -138f), new Vector2(380f, 230f), new Vector2(1f, 0.5f));

        RectTransform controlPanel = EnsurePanel(canvasRt, "HUD_ControlPanel", PanelColor);
        SetBottomCenter(controlPanel, new Vector2(0f, 18f), new Vector2(860f, 120f));

        RectTransform sourcePanel = EnsurePanel(canvasRt, "HUD_SourcePanel", PanelColor);
        SetBottomCenter(sourcePanel, new Vector2(0f, 146f), new Vector2(1040f, 92f));

        controlPanel.SetAsFirstSibling();
        sourcePanel.SetAsFirstSibling();
        inspectorPanel.SetAsFirstSibling();
        eventPanel.SetAsFirstSibling();
        scorePanel.SetAsFirstSibling();
        topBar.SetAsFirstSibling();

        Text title = EnsureText(topBar, "HUD_TitleText", "THUAI9  云厂竞逐战", 30, FontStyle.Bold, Gold, TextAnchor.MiddleLeft);
        SetChildRect(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(360f, 62f), new Vector2(0f, 0.5f));

        Text statusText = MoveText("StatusText", canvasRt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(410f, -43f), new Vector2(520f, 42f), new Vector2(0f, 0.5f), 17, TextAnchor.MiddleLeft);
        if (statusText != null && string.IsNullOrWhiteSpace(statusText.text)) statusText.text = "状态：等待回放 / 预览模式";

        Text gameTimeText = MoveText("GameTimeText", canvasRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -43f), new Vector2(260f, 54f), new Vector2(0.5f, 0.5f), 28, TextAnchor.MiddleCenter);
        if (gameTimeText != null) gameTimeText.text = "00:00";

        Text gameStateText = EnsureText(topBar, "GameStateText", "对局：等待首帧", 16, FontStyle.Normal, TextColor, TextAnchor.MiddleRight);
        SetChildRect(gameStateText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-28f, 0f), new Vector2(470f, 42f), new Vector2(1f, 0.5f));

        Text scoreTitle = EnsureText(scorePanel, "HUD_ScoreTitle", "队伍状态", 20, FontStyle.Bold, Cyan, TextAnchor.MiddleLeft);
        SetChildRect(scoreTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -16f), new Vector2(-36f, 32f), new Vector2(0f, 1f));
        for (int i = 0; i < 4; i++)
        {
            Text score = MoveText($"TeamScoreText{i + 1}", canvasRt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-42f, -166f - i * 38f), new Vector2(340f, 32f), new Vector2(1f, 1f), 15, TextAnchor.MiddleLeft);
            if (score != null && string.IsNullOrWhiteSpace(score.text)) score.text = $"队伍 {i + 1}   分数 --   算力 --";
        }

        Text eventTitle = EnsureText(eventPanel, "HUD_EventTitle", "工业呼吸 / AI 事件", 18, FontStyle.Bold, Cyan, TextAnchor.MiddleLeft);
        SetChildRect(eventTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -12f), new Vector2(-36f, 28f), new Vector2(0f, 1f));
        Text aiEventText = EnsureText(eventPanel, "AIEventText", "AI事件：暂无", 15, FontStyle.Normal, TextColor, TextAnchor.UpperLeft);
        SetChildRect(aiEventText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -44f), new Vector2(-36f, 76f), new Vector2(0f, 1f));
        Text aiEffectText = EnsureText(eventPanel, "AIEffectText", "世界修正：暂无", 14, FontStyle.Normal, TextColor, TextAnchor.UpperLeft);
        SetChildRect(aiEffectText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -120f), new Vector2(-36f, 48f), new Vector2(0f, 1f));

        Text inspectorTitle = EnsureText(inspectorPanel, "HUD_InspectorTitle", "对象检查器", 18, FontStyle.Bold, Gold, TextAnchor.MiddleLeft);
        SetChildRect(inspectorTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -12f), new Vector2(-36f, 28f), new Vector2(0f, 1f));
        Text selectionInfo = EnsureText(inspectorPanel, "SelectionInfoText", "选中对象\n点击地图上的单位、建筑、资源或地块查看详情\nEsc 清除选择", 14, FontStyle.Normal, TextColor, TextAnchor.UpperLeft);
        SetChildRect(selectionInfo.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(18f, -44f), new Vector2(-36f, 20f), new Vector2(0f, 1f));

        LayoutControls(canvasRt);
        LayoutSourceControls(sourcePanel);
        WireRuntimeControllers(canvas, gameStateText, aiEventText, aiEffectText, selectionInfo);
        ConfigureCamera();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("[UI] Rebuilt MainGame HUD layout with replay controls, event panel and object inspector.");
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

        Text frameInfo = MoveText("FrameInfoText", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -55f), new Vector2(220f, 28f), new Vector2(0.5f, 1f), 16, TextAnchor.MiddleCenter);
        if (frameInfo != null && string.IsNullOrWhiteSpace(frameInfo.text)) frameInfo.text = "帧：0/0";

        RectTransform prev = MoveRect("PreviousFrameButton", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-330f, 22f), new Vector2(110f, 38f), new Vector2(0.5f, 0f));
        RectTransform play = MoveRect("PlayButton", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-175f, 22f), new Vector2(104f, 42f), new Vector2(0.5f, 0f));
        RectTransform pause = MoveRect("PauseButton", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-48f, 22f), new Vector2(104f, 42f), new Vector2(0.5f, 0f));
        RectTransform stop = MoveRect("StopButton", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(79f, 22f), new Vector2(104f, 42f), new Vector2(0.5f, 0f));
        RectTransform next = MoveRect("NextFrameButton", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(330f, 22f), new Vector2(110f, 38f), new Vector2(0.5f, 0f));
        RectTransform speed = MoveRect("SpeedDropdown", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(210f, 22f), new Vector2(120f, 38f), new Vector2(0.5f, 0f));

        StyleButton(prev, "上一帧", new Color(0.20f, 0.27f, 0.34f, 1f));
        StyleButton(play, "播放", new Color(0.18f, 0.72f, 0.28f, 1f));
        StyleButton(pause, "暂停", new Color(0.88f, 0.72f, 0.18f, 1f));
        StyleButton(stop, "停止", new Color(0.82f, 0.18f, 0.18f, 1f));
        StyleButton(next, "下一帧", new Color(0.20f, 0.27f, 0.34f, 1f));
        StyleDropdown(speed);
    }

    private static void LayoutSourceControls(RectTransform panel)
    {
        DeleteChildIfExists(panel, "HUD_SourceHintText");

        Text replayLabel = EnsureText(panel, "HUD_ReplayPathLabel", "Replay", 15, FontStyle.Bold, Cyan, TextAnchor.MiddleLeft);
        SetChildRect(replayLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -22f), new Vector2(70f, 28f), new Vector2(0f, 1f));

        InputField replayInput = EnsureInputField(panel, "ReplayPathInput", "Assets/Playback/test/official_bot_match.thuaipb", "Replay file path");
        SetChildRect(replayInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -18f), new Vector2(520f, 30f), new Vector2(0f, 1f));

        Button loadButton = EnsureButton(panel, "LoadReplayButton", "Load", new Color(0.18f, 0.36f, 0.58f, 1f));
        SetChildRect(loadButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(626f, -18f), new Vector2(82f, 30f), new Vector2(0f, 1f));

        Text liveLabel = EnsureText(panel, "HUD_LiveAddressLabel", "Live", 15, FontStyle.Bold, Cyan, TextAnchor.MiddleLeft);
        SetChildRect(liveLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -56f), new Vector2(70f, 28f), new Vector2(0f, 1f));

        InputField liveInput = EnsureInputField(panel, "ServerAddressInput", "127.0.0.1:8888", "server:port");
        SetChildRect(liveInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -52f), new Vector2(260f, 30f), new Vector2(0f, 1f));

        Button connectButton = EnsureButton(panel, "ConnectLiveButton", "Connect", new Color(0.12f, 0.48f, 0.32f, 1f));
        SetChildRect(connectButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(366f, -52f), new Vector2(92f, 30f), new Vector2(0f, 1f));

        Button disconnectButton = EnsureButton(panel, "DisconnectLiveButton", "Disconnect", new Color(0.48f, 0.18f, 0.18f, 1f));
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

    private static void WireRuntimeControllers(Canvas canvas, Text gameStateText, Text aiEventText, Text aiEffectText, Text selectionInfo)
    {
        UIController ui = canvas.GetComponent<UIController>() ?? canvas.gameObject.AddComponent<UIController>();
        ui.gameStateText = gameStateText;
        ui.aiEventText = aiEventText;
        ui.aiEffectText = aiEffectText;
        ui.selectionInfoText = selectionInfo;
        ui.playbackPathInput = GameObject.Find("ReplayPathInput")?.GetComponent<InputField>();
        ui.loadPlaybackButton = GameObject.Find("LoadReplayButton")?.GetComponent<Button>();
        ui.serverAddressInput = GameObject.Find("ServerAddressInput")?.GetComponent<InputField>();
        ui.connectLiveButton = GameObject.Find("ConnectLiveButton")?.GetComponent<Button>();
        ui.disconnectLiveButton = GameObject.Find("DisconnectLiveButton")?.GetComponent<Button>();

        WorldSelectionController selectionController = canvas.GetComponent<WorldSelectionController>() ?? canvas.gameObject.AddComponent<WorldSelectionController>();
        selectionController.selectionText = selectionInfo;
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

    private static Text EnsureText(RectTransform parent, string name, string value, int fontSize, FontStyle style, Color color, TextAnchor anchor)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        if (existing == null) go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>() ?? go.AddComponent<Text>();
        if (string.IsNullOrWhiteSpace(text.text)) text.text = value;
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
        image.color = new Color(0.88f, 0.93f, 0.96f, 1f);

        InputField input = go.GetComponent<InputField>() ?? go.AddComponent<InputField>();
        RectTransform rt = go.GetComponent<RectTransform>();

        Text text = EnsureText(rt, $"{name}Text", value, 14, FontStyle.Normal, Color.black, TextAnchor.MiddleLeft);
        Stretch(text.rectTransform, new Vector2(8f, 3f), new Vector2(8f, 3f));
        text.supportRichText = false;

        Text placeholderText = EnsureText(rt, $"{name}Placeholder", placeholder, 14, FontStyle.Normal, new Color(0.25f, 0.30f, 0.34f, 0.72f), TextAnchor.MiddleLeft);
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
        Image image = rt.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
        image.color = new Color(0.90f, 0.94f, 0.96f, 1f);
        Text label = rt.Find("Label")?.GetComponent<Text>();
        if (label != null) ApplyTextStyle(label, 16, TextAnchor.MiddleCenter, Color.black, FontStyle.Normal);
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
