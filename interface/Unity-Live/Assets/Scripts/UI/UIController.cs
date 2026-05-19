using Protobuf;
using THUAI9.Unity.Core;
using THUAI9.Unity.Live;
using THUAI9.Unity.UI.Shared;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace THUAI9.Unity.UI
{
    public class UIController : MonoBehaviour
    {
        private const string ControllerObjectName = "LiveUIController";

        private static UIController instance;

        private SimpleHud hud;
        private InputField addressInput;
        private LiveSpectatorClient liveClient;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
            {
                return;
            }

            UIController existing = FindObjectOfType<UIController>();
            if (existing != null)
            {
                instance = existing;
                return;
            }

            GameObject go = new GameObject(ControllerObjectName);
            instance = go.AddComponent<UIController>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            gameObject.name = ControllerObjectName;
            liveClient = FindObjectOfType<LiveSpectatorClient>() ?? new GameObject("LiveSpectatorClient").AddComponent<LiveSpectatorClient>();
            hud = new SimpleHud("THUAI9 云厂竞逐战 - 直播");
            BuildPanel();
        }

        private void Update()
        {
            if (hud == null)
            {
                return;
            }

            hud.UpdateCommon(CoreParam.stableLiveGameMilliseconds);
            hud.StatusText.text = BuildStatusText();
        }

        private void BuildPanel()
        {
            RectTransform panel = hud.AddPanel("HUD_LivePanel", new Vector2(24f, -108f), new Vector2(560f, 120f));
            hud.Label(panel, "LiveLabel", "直播源", new Vector2(18f, -16f), new Vector2(72f, 30f), 16);
            addressInput = hud.Input(panel, "ServerAddressInput", liveClient != null ? liveClient.ServerAddress : "127.0.0.1:8888", "127.0.0.1:8888；WebGL 正式由网站推帧", new Vector2(92f, -16f), new Vector2(286f, 30f));
            hud.Button(panel, "ConnectLiveButton", "连接/等待", new Vector2(390f, -16f), new Vector2(88f, 32f), new Color(0.18f, 0.50f, 0.78f, 1f), () => { ClearSelection(); liveClient?.StartLive(addressInput != null ? addressInput.text : null); });
            hud.Button(panel, "DisconnectLiveButton", "断开", new Vector2(486f, -16f), new Vector2(56f, 32f), new Color(0.75f, 0.18f, 0.18f, 1f), () => { ClearSelection(); liveClient?.StopLive(); });
            hud.Label(panel, "LiveHintText", "网站调用 window.THUAI9Unity.connectLiveWebSocket(wsUrl) 或 submitLiveFrame* 推送 MessageToClient。", new Vector2(18f, -60f), new Vector2(520f, 44f), 14, TextAnchor.UpperLeft);
        }

        private string BuildStatusText()
        {
            if (liveClient == null)
            {
                return "直播：等待初始化";
            }

            return liveClient.StatusText + "\n" +
                $"收帧/出队/渲染：{liveClient.ReceivedFrameCount}/{liveClient.DequeuedFrameCount}/{liveClient.RenderedFrameCount}  队列：{liveClient.QueuedFrameCount}\n" +
                $"对象：{liveClient.LastReceivedObjectCount}  队伍：{liveClient.LastReceivedTeamCount}  角色：{liveClient.LastReceivedCharacterCount}  工厂：{liveClient.LastReceivedFactoryCount}  资源：{liveClient.LastReceivedResourceCount}";
        }

        private static void ClearSelection()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
