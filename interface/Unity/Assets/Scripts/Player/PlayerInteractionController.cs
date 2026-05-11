using System;
using THUAI9.Unity.Render;
using THUAI9.Unity.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace THUAI9.Unity.Player
{
    /// <summary>
    /// Mouse/keyboard bindings for the minimal local player loop.
    /// Left-click selection remains owned by WorldSelectionController.
    /// </summary>
    public class PlayerInteractionController : MonoBehaviour
    {
        private const float MoveKeyRepeatSeconds = 0.15f;

        private static PlayerInteractionController instance;
        private PlayerControlClient playerClient;
        private WorldSelectionController selectionController;
        private Camera targetCamera;
        private float nextMoveKeyTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
            {
                return;
            }

            GameObject go = GameObject.Find("PlayerInteractionController") ?? new GameObject("PlayerInteractionController");
            instance = go.GetComponent<PlayerInteractionController>() ?? go.AddComponent<PlayerInteractionController>();
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
            RefreshReferences();
        }

        private void Update()
        {
            RefreshReferences();
            if (playerClient == null || !playerClient.IsPlayerMode)
            {
                return;
            }

            HandleMouseActions();
            HandleKeyboardActions();
        }

        private void HandleMouseActions()
        {
            if (!Input.GetMouseButtonDown(1) || IsPointerOverUI() || targetCamera == null)
            {
                return;
            }

            WorldObjectInfo selected = selectionController != null ? selectionController.SelectedInfo : null;
            WorldObjectInfo hovered = selectionController != null ? selectionController.HoveredInfo : null;
            if (hovered != null
                && string.Equals(hovered.objectType, "Character", StringComparison.OrdinalIgnoreCase)
                && hovered.teamId != playerClient.teamId)
            {
                playerClient.Attack(hovered);
                return;
            }

            Vector3 world = targetCamera.ScreenToWorldPoint(Input.mousePosition);
            world.z = 0f;
            playerClient.MoveTowardWorld(world, selected);
        }

        private void HandleKeyboardActions()
        {
            if (Time.unscaledTime >= nextMoveKeyTime)
            {
                bool sentMove = false;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
                {
                    playerClient.MoveAngle(Mathf.PI);
                    sentMove = true;
                }
                else if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
                {
                    playerClient.MoveAngle(0f);
                    sentMove = true;
                }
                else if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
                {
                    playerClient.MoveAngle(-Mathf.PI * 0.5f);
                    sentMove = true;
                }
                else if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
                {
                    playerClient.MoveAngle(Mathf.PI * 0.5f);
                    sentMove = true;
                }

                if (sentMove)
                {
                    nextMoveKeyTime = Time.unscaledTime + MoveKeyRepeatSeconds;
                }
            }

            WorldObjectInfo selected = selectionController != null ? selectionController.SelectedInfo : null;
            if (Input.GetKeyDown(KeyCode.C)) playerClient.CreateCharacter();
            if (Input.GetKeyDown(KeyCode.H)) playerClient.Harvest(selected);
            if (Input.GetKeyDown(KeyCode.O)) playerClient.Occupy(selected);
            if (Input.GetKeyDown(KeyCode.P)) playerClient.ProduceDefaultGoods();
            if (Input.GetKeyDown(KeyCode.U)) playerClient.UplevelDefaultTech();
            if (Input.GetKeyDown(KeyCode.R)) playerClient.Recover();
            if (Input.GetKeyDown(KeyCode.E)) playerClient.EndAllAction();
            if (Input.GetKeyDown(KeyCode.F)) playerClient.Attack(selected);
        }

        private void RefreshReferences()
        {
            playerClient ??= PlayerControlClient.GetOrCreate();
            selectionController ??= FindObjectOfType<WorldSelectionController>();
            targetCamera ??= Camera.main;
        }

        private static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
