using System.Collections.Generic;
using THUAI9.Unity.CameraControlNS;
using THUAI9.Unity.Core;
using THUAI9.Unity.Player;
using THUAI9.Unity.UI.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace THUAI9.Unity.UI
{
    public class UIController : MonoBehaviour
    {
        private readonly Dictionary<string, Button> actionButtons = new Dictionary<string, Button>();
        private readonly List<ActionButtonSpec> actionButtonSpecs = new List<ActionButtonSpec>();
        private SimpleHud hud;
        private TrialSandboxController trial;
        private WorldSelectionController selector;
        private Text selectionText;
        private Text actionHintText;
        private RectTransform helpPanel;
        private WorldHoverInfoPanel hoverInfoPanel;

        private void Awake()
        {
            trial = FindObjectOfType<TrialSandboxController>() ?? new GameObject("TrialSandboxController").AddComponent<TrialSandboxController>();
            selector = FindObjectOfType<WorldSelectionController>() ?? new GameObject("WorldSelectionController").AddComponent<WorldSelectionController>();
            hud = new SimpleHud("THUAI9 云厂竞逐战 - 试玩", 2);
            BuildActionPanel();
            BuildInitialHelpPanel();
            EnsureWorldHoverInfo();
        }

        private void Start()
        {
            trial.StartTrial();
        }

        private void Update()
        {
            int gameTime = CoreParam.allMessage != null ? CoreParam.allMessage.GameTime : CoreParam.stableLiveGameMilliseconds;
            hud.UpdateCommon(gameTime);
            EnsureWorldHoverInfo();

            if (trial == null || selector == null)
            {
                return;
            }

            trial.SetSelection(selector.SelectedInfo, selector.SelectedTile);
            hud.StatusText.text = trial.StatusText;
            selectionText.text = trial.BuildSelectionText(selector.SelectedInfo, selector.SelectedTile);
            UpdateActionButtons();
            HandleRightClickMove();
        }

        private void BuildActionPanel()
        {
            RectTransform panel = hud.AddPanel("HUD_TrialActionPanel", new Vector2(24f, -108f), new Vector2(620f, 508f));
            hud.Label(panel, "TrialActionTitle", "本地试玩 / 上下文操作", new Vector2(18f, -12f), new Vector2(420f, 30f), 18);
            selectionText = hud.Label(
                panel,
                "TrialSelectionText",
                "未选中对象",
                new Vector2(18f, -48f),
                new Vector2(576f, 96f),
                14,
                TextAnchor.UpperLeft);

            AddActionButton(panel, "TrialResetButton", "重新试玩", "reset-trial", new Color(0.56f, 0.32f, 0.18f, 1f));
            AddActionButton(panel, "TrialMoveButton", "移动/靠近", "move", new Color(0.20f, 0.34f, 0.48f, 1f));
            AddActionButton(panel, "TrialStopButton", "停止", "stop", new Color(0.42f, 0.48f, 0.56f, 1f));
            AddActionButton(panel, "TrialCreateDroneButton", "造无人机", "create-drone", new Color(0.18f, 0.50f, 0.78f, 1f));
            AddActionButton(panel, "TrialCreateRobotButton", "造机器人", "create-robot", new Color(0.18f, 0.50f, 0.78f, 1f));
            AddActionButton(panel, "TrialCreateCarButton", "造无人车", "create-car", new Color(0.18f, 0.50f, 0.78f, 1f));
            AddActionButton(panel, "TrialHarvestButton", "采集", "harvest", new Color(0.18f, 0.72f, 0.28f, 1f));
            AddActionButton(panel, "TrialOccupyButton", "占领算力", "occupy", new Color(0.50f, 0.42f, 0.90f, 1f));
            AddActionButton(panel, "TrialAttackButton", "攻击", "attack", new Color(0.80f, 0.24f, 0.20f, 1f));
            AddActionButton(panel, "TrialRecoverButton", "恢复/修复", "recover", new Color(0.20f, 0.62f, 0.70f, 1f));

            AddGoodsButtons(panel, "produce", "生产", new Color(0.88f, 0.62f, 0.18f, 1f));
            AddGoodsButtons(panel, "load", "装载", new Color(0.20f, 0.58f, 0.42f, 1f));
            AddGoodsButtons(panel, "buy", "买入", new Color(0.34f, 0.45f, 0.72f, 1f));
            AddGoodsButtons(panel, "sell", "卖出", new Color(0.66f, 0.42f, 0.18f, 1f));

            AddActionButton(panel, "TrialTechHpButton", "升级生命", "upgrade-hp", new Color(0.80f, 0.36f, 0.82f, 1f));
            AddActionButton(panel, "TrialTechAttackButton", "升级攻击", "upgrade-attack", new Color(0.80f, 0.36f, 0.82f, 1f));
            AddActionButton(panel, "TrialTechAttackSizeButton", "升级范围", "upgrade-attack-size", new Color(0.80f, 0.36f, 0.82f, 1f));
            AddActionButton(panel, "TrialTechRobustButton", "升级耐久", "upgrade-robust", new Color(0.80f, 0.36f, 0.82f, 1f));
            AddActionButton(panel, "TrialTechMoveSpeedButton", "升级移速", "upgrade-move-speed", new Color(0.80f, 0.36f, 0.82f, 1f));
            AddActionButton(panel, "TrialTechCarryButton", "升级载重", "upgrade-carry", new Color(0.80f, 0.36f, 0.82f, 1f));
            AddActionButton(panel, "TrialTechEfficiencyButton", "升级效率", "upgrade-efficiency", new Color(0.80f, 0.36f, 0.82f, 1f));
            AddActionButton(panel, "TrialTechProductionButton", "升级生产", "upgrade-production", new Color(0.80f, 0.36f, 0.82f, 1f));
            AddActionButton(panel, "TrialTechStorageButton", "升级仓储", "upgrade-storage", new Color(0.80f, 0.36f, 0.82f, 1f));
            AddActionButton(panel, "TrialTechPriceButton", "升级售价", "upgrade-price", new Color(0.80f, 0.36f, 0.82f, 1f));
            AddActionButton(panel, "TrialTechCostButton", "升级降本", "upgrade-cost", new Color(0.80f, 0.36f, 0.82f, 1f));

            actionHintText = hud.Label(
                panel,
                "TrialActionHint",
                "点击对象后只显示该上下文的操作；按钮可点，不合法会在状态栏说明原因。",
                new Vector2(18f, -468f),
                new Vector2(576f, 34f),
                12,
                TextAnchor.UpperLeft);

            hud.StatusText.rectTransform.anchoredPosition = new Vector2(24f, -632f);
        }

        private void AddGoodsButtons(RectTransform panel, string prefix, string verb, Color color)
        {
            AddActionButton(panel, "Trial" + prefix + "SemiconductorButton", verb + "半导体", prefix + "-semiconductor", color);
            AddActionButton(panel, "Trial" + prefix + "MedicineButton", verb + "药品", prefix + "-medicine", color);
            AddActionButton(panel, "Trial" + prefix + "ToysButton", verb + "小商品", prefix + "-toys", color);
            AddActionButton(panel, "Trial" + prefix + "ClothesButton", verb + "服饰", prefix + "-clothes", color);
            AddActionButton(panel, "Trial" + prefix + "FoodButton", verb + "食品", prefix + "-food", color);
        }

        private void AddActionButton(RectTransform panel, string name, string label, string action, Color color)
        {
            Button button = hud.Button(panel, name, label, Vector2.zero, new Vector2(132f, 30f), color, () => trial?.ExecuteSelectedAction(action));
            actionButtons[action] = button;
            actionButtonSpecs.Add(new ActionButtonSpec(action, button));
        }

        private void BuildInitialHelpPanel()
        {
            helpPanel = hud.AddPanel("HUD_TrialInitialHelpPanel", Vector2.zero, new Vector2(760f, 390f));
            helpPanel.anchorMin = helpPanel.anchorMax = new Vector2(0.5f, 0.5f);
            helpPanel.pivot = new Vector2(0.5f, 0.5f);
            helpPanel.anchoredPosition = Vector2.zero;

            hud.Label(helpPanel, "TrialHelpTitle", "THUAI9 本地试玩说明", new Vector2(24f, -20f), new Vector2(620f, 34f), 22);
            hud.Label(
                helpPanel,
                "TrialHelpBody",
                "" +
                "基础交互：\n" +
                "• 左键选中角色、工厂、资源点、算力中心或地图格。\n" +
                "• 初始只有队伍 1 / 队伍 2 的工厂，没有自动生成角色。\n" +
                "• 本地试玩中两支队伍都允许手动操控，便于同时体验攻防与交易。\n" +
                "• 先左键选中队伍工厂，再创建无人机、机器人或无人车。\n" +
                "• 创建后左键选中角色，WASD / 方向键每次移动一格，不会移动地图视野。\n" +
                "• 点击资源、算力中心、市场或目标格后，侧边栏只显示当前上下文操作。\n" +
                "• 采集、占领、交易、靠近目标会自动寻路；点击“停止”可中途打断。\n" +
                "• 中键拖拽移动视野，滚轮缩放；按钮可点，不合法会说明原因。\n\n" +
                "关闭本说明后不会再自动弹出，请自由探索。",
                new Vector2(24f, -70f),
                new Vector2(706f, 258f),
                15,
                TextAnchor.UpperLeft);
            hud.Button(
                helpPanel,
                "TrialHelpCloseButton",
                "开始试玩",
                new Vector2(588f, -334f),
                new Vector2(136f, 38f),
                new Color(0.16f, 0.54f, 0.82f, 1f),
                () => helpPanel.gameObject.SetActive(false));
            helpPanel.gameObject.SetActive(true);
        }

        private void UpdateActionButtons()
        {
            if (actionHintText != null)
            {
                actionHintText.text = trial.GetActionHint(selector.SelectedInfo, selector.SelectedTile);
            }

            HashSet<string> visibleActions = new HashSet<string>(trial.GetVisibleActions(selector.SelectedInfo, selector.SelectedTile));
            int visibleIndex = 0;
            foreach (ActionButtonSpec spec in actionButtonSpecs)
            {
                Button button = spec.Button;
                bool visible = visibleActions.Contains(spec.Action);
                button.gameObject.SetActive(visible);
                button.interactable = visible;
                if (!visible)
                {
                    continue;
                }

                RectTransform rect = button.GetComponent<RectTransform>();
                int column = visibleIndex % 4;
                int row = visibleIndex / 4;
                rect.anchoredPosition = new Vector2(18f + column * 144f, -154f - row * 34f);
                visibleIndex++;
            }
        }

        private void HandleRightClickMove()
        {
            if (!Input.GetMouseButtonDown(1) || selector.IsPointerOverUi)
            {
                return;
            }

            if (selector.TryGetMouseTile(out Vector2Int tile))
            {
                trial.MoveSelectedOrPlayerToTile(tile.x, tile.y);
            }
        }

        private void EnsureWorldHoverInfo()
        {
            if (selector == null)
            {
                selector = FindObjectOfType<WorldSelectionController>() ??
                    new GameObject("WorldSelectionController").AddComponent<WorldSelectionController>();
            }

            Camera mainCamera = Camera.main;
            selector.targetCamera = mainCamera;
            selector.enableHover = true;
            selector.enableClickSelection = true;

            CameraControl cameraControl = mainCamera != null ? mainCamera.GetComponent<CameraControl>() : null;
            if (cameraControl != null)
            {
                cameraControl.enableKeyboardMove = false;
            }

            if (hud != null && hud.Canvas != null)
            {
                hoverInfoPanel = WorldHoverInfoPanel.GetOrCreate(hud.Canvas, mainCamera);
                hoverInfoPanel.ShowWorldHoverInfo = true;
            }
        }

        private readonly struct ActionButtonSpec
        {
            public ActionButtonSpec(string action, Button button)
            {
                Action = action;
                Button = button;
            }

            public string Action { get; }
            public Button Button { get; }
        }
    }
}
