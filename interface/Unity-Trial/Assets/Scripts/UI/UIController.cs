using System.Collections.Generic;
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
        private SimpleHud hud;
        private TrialSandboxController trial;
        private WorldSelectionController selector;
        private Text selectionText;
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
            RectTransform panel = hud.AddPanel("HUD_TrialActionPanel", new Vector2(24f, -108f), new Vector2(560f, 242f));
            hud.Label(panel, "TrialActionTitle", "本地试玩 / 上下文操作", new Vector2(18f, -12f), new Vector2(320f, 30f), 18);
            selectionText = hud.Label(
                panel,
                "TrialSelectionText",
                "未选中对象",
                new Vector2(18f, -48f),
                new Vector2(516f, 78f),
                14,
                TextAnchor.UpperLeft);

            AddActionButton(panel, "TrialCreateButton", "创建/重置", "create-character", new Vector2(18f, -136f), new Color(0.18f, 0.50f, 0.78f, 1f));
            AddActionButton(panel, "TrialMoveButton", "移动至选中", "move", new Vector2(136f, -136f), new Color(0.20f, 0.34f, 0.48f, 1f));
            AddActionButton(panel, "TrialHarvestButton", "采集 H", "harvest", new Vector2(254f, -136f), new Color(0.18f, 0.72f, 0.28f, 1f));
            AddActionButton(panel, "TrialOccupyButton", "占领 O", "occupy", new Vector2(372f, -136f), new Color(0.50f, 0.42f, 0.90f, 1f));
            AddActionButton(panel, "TrialProduceButton", "生产 P", "produce", new Vector2(18f, -180f), new Color(0.88f, 0.62f, 0.18f, 1f));
            AddActionButton(panel, "TrialTechButton", "升级 U", "uplevel-tech", new Vector2(136f, -180f), new Color(0.80f, 0.36f, 0.82f, 1f));
            AddActionButton(panel, "TrialAttackButton", "攻击 F", "attack", new Vector2(254f, -180f), new Color(0.80f, 0.24f, 0.20f, 1f));
            AddActionButton(panel, "TrialRecoverButton", "恢复 G", "recover", new Vector2(372f, -180f), new Color(0.20f, 0.62f, 0.70f, 1f));
            AddActionButton(panel, "TrialStopButton", "停止 Space", "end-all-action", new Vector2(18f, -220f), new Color(0.42f, 0.48f, 0.56f, 1f));
            hud.Label(
                panel,
                "TrialActionHint",
                "左键选中对象；右键地图移动当前角色；WASD/方向键也可移动；按钮会按当前选中对象启用。",
                new Vector2(136f, -214f),
                new Vector2(390f, 34f),
                12,
                TextAnchor.UpperLeft);
        }

        private void AddActionButton(RectTransform panel, string name, string label, string action, Vector2 position, Color color)
        {
            Button button = hud.Button(panel, name, label, position, new Vector2(104f, 34f), color, () => trial?.ExecuteSelectedAction(action));
            actionButtons[action] = button;
        }

        private void BuildInitialHelpPanel()
        {
            helpPanel = hud.AddPanel("HUD_TrialInitialHelpPanel", Vector2.zero, new Vector2(720f, 332f));
            helpPanel.anchorMin = helpPanel.anchorMax = new Vector2(0.5f, 0.5f);
            helpPanel.pivot = new Vector2(0.5f, 0.5f);
            helpPanel.anchoredPosition = Vector2.zero;

            hud.Label(helpPanel, "TrialHelpTitle", "THUAI9 本地试玩说明", new Vector2(24f, -20f), new Vector2(620f, 34f), 22);
            hud.Label(
                helpPanel,
                "TrialHelpBody",
                "这是仿 THUAI7 interface_local 的探索式试玩，不是正式 Server 判题器。\n\n" +
                "基础交互：\n" +
                "• 左键选中角色、工厂、资源点、算力中心或地图格。\n" +
                "• 右键地图格移动当前角色；WASD / 方向键也可单格移动。\n" +
                "• 侧边栏按钮会根据当前选中对象执行创建、采集、占领、生产、升级、攻击、恢复。\n" +
                "• 本地只开放队伍 1 / 队伍 2，用于快速理解 THUAI9 核心动作闭环。\n\n" +
                "关闭本说明后不会再自动弹出，请自由探索。",
                new Vector2(24f, -70f),
                new Vector2(666f, 202f),
                15,
                TextAnchor.UpperLeft);
            hud.Button(
                helpPanel,
                "TrialHelpCloseButton",
                "开始试玩",
                new Vector2(548f, -276f),
                new Vector2(136f, 38f),
                new Color(0.16f, 0.54f, 0.82f, 1f),
                () => helpPanel.gameObject.SetActive(false));
            helpPanel.gameObject.SetActive(true);
        }

        private void UpdateActionButtons()
        {
            foreach (KeyValuePair<string, Button> pair in actionButtons)
            {
                pair.Value.interactable = trial.CanExecuteAction(pair.Key, selector.SelectedInfo, selector.SelectedTile);
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

            selector.targetCamera = Camera.main;
            selector.enableHover = true;
            selector.enableClickSelection = true;

            if (hud != null && hud.Canvas != null)
            {
                hoverInfoPanel = WorldHoverInfoPanel.GetOrCreate(hud.Canvas, Camera.main);
                hoverInfoPanel.ShowWorldHoverInfo = true;
            }
        }
    }
}
