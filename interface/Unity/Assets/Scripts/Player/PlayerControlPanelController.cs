using System;
using System.Collections.Generic;
using Protobuf;
using THUAI9.Unity.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace THUAI9.Unity.Player
{
    /// <summary>
    /// Optional local player/test controls.
    /// The panel is hidden by default because normal spectators only need replay/live
    /// controls; a small toggle keeps the THUAI8-style local play workflow available.
    /// </summary>
    public class PlayerControlPanelController : MonoBehaviour
    {
        private static PlayerControlPanelController instance;

        public bool showPanelOnStart = false;

        private PlayerControlClient playerClient;
        private GameObject panelObject;
        private Button toggleButton;
        private InputField addressInput;
        private InputField teamInput;
        private InputField registerInput;
        private InputField characterInput;
        private Dropdown characterTypeDropdown;
        private Dropdown goodsDropdown;
        private Dropdown techDropdown;
        private Text statusText;
        private Font uiFont;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
            {
                return;
            }

            GameObject go = GameObject.Find("PlayerControlPanelController") ?? new GameObject("PlayerControlPanelController");
            instance = go.GetComponent<PlayerControlPanelController>() ?? go.AddComponent<PlayerControlPanelController>();
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
            playerClient = PlayerControlClient.GetOrCreate();
            BuildPanel();
        }

        private void Update()
        {
            playerClient ??= PlayerControlClient.GetOrCreate();
            if (statusText != null && playerClient != null)
            {
                statusText.text =
                    $"模式：{playerClient.ModeText}\n" +
                    $"{playerClient.StatusText}\n" +
                    $"最近动作：{playerClient.LastActionText}\n" +
                    $"动作发送/成功/失败：{playerClient.SentActionCount}/{playerClient.SuccessfulActionCount}/{playerClient.FailedActionCount}  玩家流帧：{playerClient.ReceivedPlayerStreamFrames}";
            }
        }

        private void BuildPanel()
        {
            Canvas canvas = EnsureCanvas();
            toggleButton = FindOrCreateButton(canvas.transform, "HUD_PlayerPanelToggle", "本地试玩", new Color(0.11f, 0.22f, 0.30f, 0.94f));
            SetBottomRightRect(toggleButton.GetComponent<RectTransform>(), 24f, 356f, 150f, 34f);
            toggleButton.onClick.RemoveListener(OnToggleClicked);
            toggleButton.onClick.AddListener(OnToggleClicked);

            panelObject = GameObject.Find("HUD_PlayerPanel") ?? new GameObject("HUD_PlayerPanel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvas.transform, false);

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-24f, 24f);
            rect.sizeDelta = new Vector2(500f, 320f);

            Image image = panelObject.GetComponent<Image>() ?? panelObject.AddComponent<Image>();
            image.color = new Color(0.026f, 0.043f, 0.065f, 0.92f);
            image.raycastTarget = true;

            Text title = FindOrCreateText(panelObject.transform, "HUD_PlayerPanelTitle", "试玩接入面板", 18, FontStyle.Bold, new Color(0.30f, 0.88f, 0.98f, 1f));
            SetRect(title.rectTransform, 14f, -12f, 220f, 26f, true);

            addressInput = FindOrCreateInput(panelObject.transform, "PlayerServerAddressInput", playerClient != null ? playerClient.serverAddress : "127.0.0.1:8888", "server:port");
            SetRect(addressInput.GetComponent<RectTransform>(), 14f, -46f, 164f, 28f, true);
            teamInput = FindOrCreateInput(panelObject.transform, "PlayerTeamInput", playerClient != null ? playerClient.teamId.ToString() : "1", "队伍");
            SetRect(teamInput.GetComponent<RectTransform>(), 184f, -46f, 54f, 28f, true);
            registerInput = FindOrCreateInput(panelObject.transform, "PlayerRegisterIdInput", playerClient != null ? playerClient.registerPlayerId.ToString() : "0", "注册ID");
            SetRect(registerInput.GetComponent<RectTransform>(), 244f, -46f, 74f, 28f, true);
            characterInput = FindOrCreateInput(panelObject.transform, "PlayerCharacterIdInput", playerClient != null ? playerClient.characterPlayerId.ToString() : "1", "单位ID");
            SetRect(characterInput.GetComponent<RectTransform>(), 324f, -46f, 54f, 28f, true);

            Button connectButton = FindOrCreateButton(panelObject.transform, "ConnectPlayerButton", "玩家接入", new Color(0.12f, 0.48f, 0.32f, 1f));
            SetRect(connectButton.GetComponent<RectTransform>(), 14f, -82f, 88f, 30f, true);
            connectButton.onClick.RemoveListener(OnConnectPlayerClicked);
            connectButton.onClick.AddListener(OnConnectPlayerClicked);

            Button spectatorButton = FindOrCreateButton(panelObject.transform, "SpectatorModeButton", "观战模式", new Color(0.18f, 0.36f, 0.58f, 1f));
            SetRect(spectatorButton.GetComponent<RectTransform>(), 108f, -82f, 88f, 30f, true);
            spectatorButton.onClick.RemoveListener(OnSpectatorModeClicked);
            spectatorButton.onClick.AddListener(OnSpectatorModeClicked);

            Button createButton = FindOrCreateButton(panelObject.transform, "CreatePlayerCharacterButton", "创建单位", new Color(0.20f, 0.48f, 0.72f, 1f));
            SetRect(createButton.GetComponent<RectTransform>(), 202f, -82f, 88f, 30f, true);
            createButton.onClick.RemoveListener(OnCreateClicked);
            createButton.onClick.AddListener(OnCreateClicked);

            Button endActionButton = FindOrCreateButton(panelObject.transform, "EndPlayerActionButton", "停止动作", new Color(0.48f, 0.18f, 0.18f, 1f));
            SetRect(endActionButton.GetComponent<RectTransform>(), 296f, -82f, 88f, 30f, true);
            endActionButton.onClick.RemoveListener(OnEndActionClicked);
            endActionButton.onClick.AddListener(OnEndActionClicked);

            characterTypeDropdown = FindOrCreateDropdown(panelObject.transform, "PlayerCharacterTypeDropdown", new[] { "Robot", "Drone", "AutonomousCar" });
            SetRect(characterTypeDropdown.GetComponent<RectTransform>(), 14f, -118f, 124f, 28f, true);
            goodsDropdown = FindOrCreateDropdown(panelObject.transform, "PlayerGoodsDropdown", new[] { "Semiconductor", "Medicine", "Toys", "Clothes", "Food" });
            SetRect(goodsDropdown.GetComponent<RectTransform>(), 144f, -118f, 124f, 28f, true);
            techDropdown = FindOrCreateDropdown(panelObject.transform, "PlayerTechDropdown", new[]
            {
                "IncreaseHp",
                "IncreaseAttackPower",
                "IncreaseAttackSize",
                "IncreaseRobust",
                "IncreaseMoveSpeed",
                "IncreaseCarryCapacity",
                "IncreaseEfficiency",
                "IncreaseProduction",
                "IncreaseStorage",
                "IncreasePrice",
                "DecreaseCost"
            });
            SetRect(techDropdown.GetComponent<RectTransform>(), 274f, -118f, 124f, 28f, true);

            Button produceButton = FindOrCreateButton(panelObject.transform, "ProducePlayerGoodsButton", "生产(P)", new Color(0.42f, 0.34f, 0.12f, 1f));
            SetRect(produceButton.GetComponent<RectTransform>(), 14f, -152f, 82f, 28f, true);
            produceButton.onClick.RemoveListener(OnProduceClicked);
            produceButton.onClick.AddListener(OnProduceClicked);

            Button techButton = FindOrCreateButton(panelObject.transform, "UplevelPlayerTechButton", "科技升级(U)", new Color(0.36f, 0.24f, 0.58f, 1f));
            SetRect(techButton.GetComponent<RectTransform>(), 102f, -152f, 108f, 28f, true);
            techButton.onClick.RemoveListener(OnTechClicked);
            techButton.onClick.AddListener(OnTechClicked);

            Button harvestButton = FindOrCreateButton(panelObject.transform, "HarvestPlayerButton", "采集(H)", new Color(0.16f, 0.42f, 0.38f, 1f));
            SetRect(harvestButton.GetComponent<RectTransform>(), 216f, -152f, 82f, 28f, true);
            harvestButton.onClick.RemoveListener(OnHarvestClicked);
            harvestButton.onClick.AddListener(OnHarvestClicked);

            Button occupyButton = FindOrCreateButton(panelObject.transform, "OccupyPlayerButton", "占领(O)", new Color(0.42f, 0.28f, 0.16f, 1f));
            SetRect(occupyButton.GetComponent<RectTransform>(), 304f, -152f, 82f, 28f, true);
            occupyButton.onClick.RemoveListener(OnOccupyClicked);
            occupyButton.onClick.AddListener(OnOccupyClicked);

            Text helpText = FindOrCreateText(
                panelObject.transform,
                "PlayerHelpText",
                "用途：本地试玩或连入服务器手动操作；普通观战和回放不需要打开。Register ID 可填 0 或 1-6；101/9101 不会作为参赛队伍开局。左键选中己方单位，右键地面移动或敌方攻击；WASD 移动视角。",
                12,
                FontStyle.Normal,
                new Color(0.74f, 0.86f, 0.92f, 1f));
            helpText.alignment = TextAnchor.UpperLeft;
            helpText.horizontalOverflow = HorizontalWrapMode.Wrap;
            SetRect(helpText.rectTransform, 14f, -184f, 458f, 54f, true);

            statusText = FindOrCreateText(panelObject.transform, "PlayerStatusText", "模式：观战/回放\n玩家：未接入", 12, FontStyle.Normal, new Color(0.74f, 0.92f, 0.82f, 1f));
            statusText.alignment = TextAnchor.UpperLeft;
            statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            SetRect(statusText.rectTransform, 14f, -244f, 458f, 58f, true);

            SetPanelVisible(showPanelOnStart);
            UpdateToggleLabel();
        }

        private void OnToggleClicked()
        {
            SetPanelVisible(panelObject == null || !panelObject.activeSelf);
            UpdateToggleLabel();
        }

        private void SetPanelVisible(bool visible)
        {
            if (panelObject != null)
            {
                panelObject.SetActive(visible);
            }
        }

        private void UpdateToggleLabel()
        {
            if (toggleButton == null)
            {
                return;
            }

            Text label = toggleButton.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = panelObject != null && panelObject.activeSelf ? "收起试玩" : "本地试玩";
            }
        }

        private void OnConnectPlayerClicked()
        {
            ApplyUiSettings();
            playerClient?.StartPlayerMode();
            ClearCurrentUiSelection();
        }

        private void OnSpectatorModeClicked()
        {
            ApplyUiSettings();
            playerClient?.StartSpectatorMode();
            ClearCurrentUiSelection();
        }

        private void OnCreateClicked()
        {
            ApplyUiSettings();
            if (characterTypeDropdown != null && characterTypeDropdown.options.Count > characterTypeDropdown.value)
            {
                playerClient?.CreateCharacter(characterTypeDropdown.options[characterTypeDropdown.value].text);
            }
            else
            {
                playerClient?.CreateCharacter();
            }
            ClearCurrentUiSelection();
        }

        private void OnProduceClicked()
        {
            ApplyUiSettings();
            playerClient?.Produce(ParseEnum(goodsDropdown, GoodsType.Semiconductor), 1);
            ClearCurrentUiSelection();
        }

        private void OnTechClicked()
        {
            ApplyUiSettings();
            playerClient?.UplevelTech(ParseEnum(techDropdown, TechType.IncreaseMoveSpeed));
            ClearCurrentUiSelection();
        }

        private void OnHarvestClicked()
        {
            playerClient?.Harvest(FindObjectOfType<WorldSelectionController>()?.SelectedInfo);
            ClearCurrentUiSelection();
        }

        private void OnOccupyClicked()
        {
            playerClient?.Occupy(FindObjectOfType<WorldSelectionController>()?.SelectedInfo);
            ClearCurrentUiSelection();
        }

        private void OnEndActionClicked()
        {
            playerClient?.EndAllAction();
            ClearCurrentUiSelection();
        }

        private void ApplyUiSettings()
        {
            if (playerClient == null)
            {
                return;
            }

            long.TryParse(teamInput != null ? teamInput.text : "1", out long parsedTeam);
            long.TryParse(registerInput != null ? registerInput.text : "0", out long parsedRegister);
            long.TryParse(characterInput != null ? characterInput.text : "1", out long parsedCharacter);
            playerClient.ApplyConnectionSettings(
                addressInput != null ? addressInput.text : playerClient.serverAddress,
                parsedTeam <= 0 ? 1 : parsedTeam,
                parsedRegister < 0 ? 0 : parsedRegister,
                parsedCharacter <= 0 ? 1 : parsedCharacter,
                parsedTeam <= 0 ? 1 : (int)parsedTeam);
        }

        private static TEnum ParseEnum<TEnum>(Dropdown dropdown, TEnum fallback) where TEnum : struct
        {
            if (dropdown != null && dropdown.options.Count > dropdown.value && Enum.TryParse(dropdown.options[dropdown.value].text, out TEnum parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private Canvas EnsureCanvas()
        {
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas != null)
            {
                if (canvas.GetComponent<GraphicRaycaster>() == null)
                {
                    canvas.gameObject.AddComponent<GraphicRaycaster>();
                }
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

        private Text FindOrCreateText(Transform parent, string name, string text, int fontSize, FontStyle fontStyle, Color color)
        {
            GameObject go = FindChildOrGlobal(parent, name) ?? new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text label = go.GetComponent<Text>() ?? go.AddComponent<Text>();
            label.text = text;
            label.font = uiFont;
            label.fontSize = fontSize;
            label.fontStyle = fontStyle;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        private InputField FindOrCreateInput(Transform parent, string name, string value, string placeholder)
        {
            GameObject go = FindChildOrGlobal(parent, name) ?? new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            image.color = new Color(0.080f, 0.115f, 0.155f, 0.98f);
            InputField input = go.GetComponent<InputField>() ?? go.AddComponent<InputField>();
            Text text = EnsureChildText(go.transform, name + "Text", value, new Color(0.92f, 0.97f, 1f, 1f));
            Text placeholderText = EnsureChildText(go.transform, name + "Placeholder", placeholder, new Color(0.55f, 0.66f, 0.74f, 0.85f));
            input.textComponent = text;
            input.placeholder = placeholderText;
            if (string.IsNullOrWhiteSpace(input.text))
            {
                input.text = value;
            }
            input.lineType = InputField.LineType.SingleLine;
            Stretch(text.rectTransform, 6f, 2f, 6f, 2f);
            Stretch(placeholderText.rectTransform, 6f, 2f, 6f, 2f);
            return input;
        }

        private Button FindOrCreateButton(Transform parent, string name, string label, Color color)
        {
            GameObject go = FindChildOrGlobal(parent, name) ?? new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            image.color = color;
            Button button = go.GetComponent<Button>() ?? go.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.25f);
            button.colors = colors;
            Text text = EnsureChildText(go.transform, name + "Text", label, Color.white);
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
            Stretch(text.rectTransform, 0f, 0f, 0f, 0f);
            return button;
        }

        private Dropdown FindOrCreateDropdown(Transform parent, string name, string[] options)
        {
            Dropdown dropdown;
            GameObject existing = FindChildOrGlobal(parent, name);
            if (existing != null)
            {
                existing.transform.SetParent(parent, false);
                dropdown = existing.GetComponent<Dropdown>() ?? existing.AddComponent<Dropdown>();
            }
            else
            {
                GameObject go = DefaultControls.CreateDropdown(new DefaultControls.Resources());
                go.name = name;
                go.transform.SetParent(parent, false);
                dropdown = go.GetComponent<Dropdown>();
            }

            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(options));
            if (dropdown.captionText != null)
            {
                dropdown.captionText.font = uiFont;
                dropdown.captionText.fontSize = 12;
                dropdown.captionText.color = new Color(0.92f, 0.97f, 1f, 1f);
            }
            if (dropdown.itemText != null)
            {
                dropdown.itemText.font = uiFont;
                dropdown.itemText.fontSize = 12;
            }
            Image image = dropdown.GetComponent<Image>();
            if (image != null) image.color = new Color(0.080f, 0.115f, 0.155f, 0.98f);
            return dropdown;
        }

        private Text EnsureChildText(Transform parent, string name, string text, Color color)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text label = go.GetComponent<Text>() ?? go.AddComponent<Text>();
            label.text = text;
            label.font = uiFont;
            label.fontSize = 12;
            label.color = color;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            return label;
        }

        private static GameObject FindChildOrGlobal(Transform parent, string name)
        {
            Transform child = parent != null ? parent.Find(name) : null;
            return child != null ? child.gameObject : GameObject.Find(name);
        }

        private static void SetRect(RectTransform rect, float left, float topOrBottom, float width, float height, bool anchorTop)
        {
            rect.anchorMin = anchorTop ? new Vector2(0f, 1f) : new Vector2(0f, 0f);
            rect.anchorMax = anchorTop ? new Vector2(0f, 1f) : new Vector2(0f, 0f);
            rect.pivot = anchorTop ? new Vector2(0f, 1f) : new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(left, topOrBottom);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetBottomRightRect(RectTransform rect, float right, float bottom, float width, float height)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-right, bottom);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static Font GetUIFont()
        {
            Font font = Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular");
            if (font != null) return font;
            try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { return null; }
        }

        private static void ClearCurrentUiSelection()
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }
}
