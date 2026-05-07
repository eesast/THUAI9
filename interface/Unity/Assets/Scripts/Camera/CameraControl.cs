using THUAI9.Unity.Core;
using THUAI9.Unity.Render;
using UnityEngine;

namespace THUAI9.Unity.CameraControlNS
{
    /// <summary>
    /// 2D 正交相机控制：支持键盘平移、中键拖拽、滚轮缩放，
    /// 并会在首帧地图准备完成后自动适配 THUAI9 当前地图尺寸。
    /// </summary>
    public class CameraControl : MonoBehaviour
    {
        [Header("相机移动速度")]
        public float moveSpeed = 20f;

        [Header("缩放速度")]
        public float zoomSpeed = 5f;

        [Header("最小缩放")]
        public float minZoom = 6f;

        [Header("最大缩放")]
        public float maxZoom = 100f;

        [Header("首帧适配视野比例")]
        [Range(0.25f, 0.9f)]
        public float initialMapCoverage = 0.42f;

        [Header("地图外侧最大平移缓冲格")]
        public float outsideViewPadding = 8f;

        [Header("靠近边缘时最少保留地图格数")]
        public float minVisibleMapTiles = 2f;

        [Header("是否在首帧自动适配地图")]
        public bool autoFitOnFirstFrame = true;

        private Camera _mainCamera;
        private Vector3 _lastMousePosition;
        private bool _hasFittedToMap;

        private void Awake()
        {
            _mainCamera = GetComponent<Camera>();
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }
        }

        private void OnEnable()
        {
            if (RenderManager.TryGetInstance(out RenderManager renderManager))
            {
                renderManager.onFirstFrame += HandleFirstFrameReady;
            }
        }

        private void OnDisable()
        {
            if (RenderManager.TryGetInstance(out RenderManager renderManager))
            {
                renderManager.onFirstFrame -= HandleFirstFrameReady;
            }
        }

        private void Start()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (_mainCamera != null)
            {
                transform.position = Tool.GetWorldCenter(-10f);
                _mainCamera.orthographic = true;
                _mainCamera.orthographicSize = Mathf.Clamp(Tool.GetMapRows() * initialMapCoverage, minZoom, maxZoom);
            }
        }

        private void LateUpdate()
        {
            if (_mainCamera == null)
            {
                return;
            }

            if (autoFitOnFirstFrame && !_hasFittedToMap && CoreParam.map != null)
            {
                FitToMap();
            }

            HandleKeyboardMove();
            HandleMouseDrag();
            HandleZoom();
            ClampPosition();
        }

        private void HandleFirstFrameReady()
        {
            if (autoFitOnFirstFrame)
            {
                FitToMap();
            }
        }

        public void FitToMap()
        {
            if (_mainCamera == null)
            {
                return;
            }

            Rect rect = Tool.GetWorldRect();
            transform.position = new Vector3(rect.center.x, rect.center.y, transform.position.z);

            float coverage = Mathf.Clamp(initialMapCoverage, 0.25f, 0.9f);
            float verticalSize = rect.height * coverage;
            float horizontalSize = rect.width / Mathf.Max(_mainCamera.aspect, 0.01f) * coverage;
            _mainCamera.orthographicSize = Mathf.Clamp(Mathf.Max(verticalSize, horizontalSize), minZoom, maxZoom);
            _hasFittedToMap = true;
        }

        private void HandleKeyboardMove()
        {
            float moveX = 0f;
            float moveY = 0f;
            float currentMoveSpeed = moveSpeed * Mathf.Max(_mainCamera.orthographicSize / 20f, 0.5f);

            if (Input.GetKey(KeyCode.W))
            {
                moveY += currentMoveSpeed * Time.unscaledDeltaTime;
            }
            if (Input.GetKey(KeyCode.S))
            {
                moveY -= currentMoveSpeed * Time.unscaledDeltaTime;
            }
            if (Input.GetKey(KeyCode.A))
            {
                moveX -= currentMoveSpeed * Time.unscaledDeltaTime;
            }
            if (Input.GetKey(KeyCode.D))
            {
                moveX += currentMoveSpeed * Time.unscaledDeltaTime;
            }

            transform.position += new Vector3(moveX, moveY, 0f);
        }

        private void HandleMouseDrag()
        {
            if (Input.GetMouseButtonDown(2))
            {
                _lastMousePosition = Input.mousePosition;
            }

            if (Input.GetMouseButton(2))
            {
                Vector3 currentMousePosition = Input.mousePosition;
                Vector3 previousWorld = _mainCamera.ScreenToWorldPoint(_lastMousePosition);
                Vector3 currentWorld = _mainCamera.ScreenToWorldPoint(currentMousePosition);
                Vector3 delta = previousWorld - currentWorld;
                transform.position += new Vector3(delta.x, delta.y, 0f);
                _lastMousePosition = currentMousePosition;
            }
        }

        private void HandleZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) <= Mathf.Epsilon)
            {
                return;
            }

            _mainCamera.orthographicSize -= scroll * zoomSpeed * Mathf.Max(_mainCamera.orthographicSize / 12f, 1f);
            _mainCamera.orthographicSize = Mathf.Clamp(_mainCamera.orthographicSize, minZoom, maxZoom);
        }

        private void ClampPosition()
        {
            Rect rect = Tool.GetWorldRect();
            float halfHeight = _mainCamera.orthographicSize;
            float halfWidth = _mainCamera.orthographicSize * _mainCamera.aspect;

            float horizontalOutside = Mathf.Min(
                Mathf.Max(outsideViewPadding, 0f),
                Mathf.Max(halfWidth - Mathf.Max(minVisibleMapTiles, 0f), 0f));
            float verticalOutside = Mathf.Min(
                Mathf.Max(outsideViewPadding, 0f),
                Mathf.Max(halfHeight - Mathf.Max(minVisibleMapTiles, 0f), 0f));

            float clampedX = Mathf.Clamp(transform.position.x, rect.xMin - horizontalOutside, rect.xMax + horizontalOutside);
            float clampedY = Mathf.Clamp(transform.position.y, rect.yMin - verticalOutside, rect.yMax + verticalOutside);
            transform.position = new Vector3(clampedX, clampedY, transform.position.z);
        }
    }
}
