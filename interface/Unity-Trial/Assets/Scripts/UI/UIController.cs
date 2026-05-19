using THUAI9.Unity.Core;
using THUAI9.Unity.Player;
using THUAI9.Unity.UI.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace THUAI9.Unity.UI
{
    public class UIController : MonoBehaviour
    {
        private SimpleHud hud;
        private TrialSandboxController trial;
        private void Awake(){ trial = FindObjectOfType<TrialSandboxController>() ?? new GameObject("TrialSandboxController").AddComponent<TrialSandboxController>(); hud = new SimpleHud("THUAI9 云厂竞逐战 - 试玩"); BuildPanel(); BuildHelp(); }
        private void Start(){ trial.StartTrial(); }
        private void Update(){ hud.UpdateCommon(CoreParam.allMessage != null ? CoreParam.allMessage.GameTime : CoreParam.stableLiveGameMilliseconds); hud.StatusText.text = trial != null ? trial.StatusText : "试玩：未初始化"; }
        private void BuildPanel()
        {
            RectTransform panel = hud.AddPanel("HUD_TrialPanel", new Vector2(24f, -108f), new Vector2(560f, 168f));
            hud.Label(panel, "TrialLabel", "本地试玩", new Vector2(18f, -14f), new Vector2(180f, 28f), 18);
            hud.Button(panel, "CreateCharacterButton", "创建/重置", new Vector2(18f, -52f), new Vector2(104f, 34f), new Color(0.18f,0.50f,0.78f,1f), () => trial?.CreateCharacter());
            hud.Button(panel, "HarvestButton", "采集 H", new Vector2(132f, -52f), new Vector2(88f, 34f), new Color(0.18f,0.72f,0.28f,1f), () => trial?.Harvest());
            hud.Button(panel, "OccupyButton", "占领 O", new Vector2(230f, -52f), new Vector2(88f, 34f), new Color(0.50f,0.42f,0.90f,1f), () => trial?.Occupy());
            hud.Button(panel, "ProduceButton", "生产 P", new Vector2(328f, -52f), new Vector2(88f, 34f), new Color(0.88f,0.62f,0.18f,1f), () => trial?.Produce());
            hud.Button(panel, "TechButton", "升级 U", new Vector2(426f, -52f), new Vector2(88f, 34f), new Color(0.80f,0.36f,0.82f,1f), () => trial?.UpgradeTech());
            hud.Button(panel, "MoveUpButton", "↑", new Vector2(76f, -96f), new Vector2(44f, 30f), new Color(0.20f,0.32f,0.48f,1f), () => trial?.MoveBy(-1, 0));
            hud.Button(panel, "MoveLeftButton", "←", new Vector2(28f, -128f), new Vector2(44f, 30f), new Color(0.20f,0.32f,0.48f,1f), () => trial?.MoveBy(0, -1));
            hud.Button(panel, "MoveDownButton", "↓", new Vector2(76f, -128f), new Vector2(44f, 30f), new Color(0.20f,0.32f,0.48f,1f), () => trial?.MoveBy(1, 0));
            hud.Button(panel, "MoveRightButton", "→", new Vector2(124f, -128f), new Vector2(44f, 30f), new Color(0.20f,0.32f,0.48f,1f), () => trial?.MoveBy(0, 1));
            hud.Label(panel, "TrialHint", "WASD/方向键移动；右键地图格移动；H 采集、O 占领、P 生产、U 升级。", new Vector2(190f, -94f), new Vector2(340f, 58f), 14, TextAnchor.UpperLeft);
        }
        private void BuildHelp(){ RectTransform panel = hud.AddPanel("HUD_HelpPanel", new Vector2(24f, -300f), new Vector2(560f, 186f)); hud.Label(panel, "HelpTitle", "操作教程", new Vector2(18f, -12f), new Vector2(520f, 28f), 18); hud.Label(panel, "HelpBody", "1. 创建队伍 1 角色。\n2. WASD / 方向键 / 右键移动，避开障碍。\n3. 靠近资源点后采集，原料和分数会上升。\n4. 靠近算力中心后占领，算力和分数会上升。\n5. 生产/升级会消耗原料，帮助理解正式动作闭环。", new Vector2(18f, -46f), new Vector2(520f, 128f), 14, TextAnchor.UpperLeft); }
    }
}
