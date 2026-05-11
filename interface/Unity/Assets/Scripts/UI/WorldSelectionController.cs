using Protobuf;
using THUAI9.Unity.Core;
using THUAI9.Unity.Render;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace THUAI9.Unity.UI
{
    /// <summary>
    /// Click/hover inspector for THUAI9 world objects.
    /// It intentionally depends on WorldObjectInfo metadata and current map tiles,
    /// not on any THUAI7/THUAI8 protocol concepts.
    /// </summary>
    public class WorldSelectionController : MonoBehaviour
    {
        public Camera targetCamera;
        public Text selectionText;
        public InspectorPanelController inspectorPanel;
        public bool enableHover = true;
        public bool enableClickSelection = true;

        private WorldObjectInfo hoveredInfo;
        private WorldObjectInfo selectedInfo;
        private Vector2Int? selectedTile;
        private GameObject hoverHighlight;
        private GameObject selectedHighlight;

        public WorldObjectInfo HoveredInfo => hoveredInfo;
        public WorldObjectInfo SelectedInfo => selectedInfo;
        public Vector2Int? SelectedTile => selectedTile;

        private void Awake()
        {
            targetCamera ??= Camera.main;
            selectionText ??= FindTextByName("SelectionInfoText");
            inspectorPanel ??= FindObjectOfType<InspectorPanelController>();
            if (inspectorPanel == null)
            {
                inspectorPanel = new GameObject("InspectorPanelController").AddComponent<InspectorPanelController>();
            }

            hoverHighlight = CreateHighlight("HoverHighlight", new Color(0.18f, 0.88f, 0.96f, 0.22f), 24);
            selectedHighlight = CreateHighlight("SelectionHighlight", new Color(1f, 0.72f, 0.22f, 0.30f), 25);
            SetHighlightVisible(hoverHighlight, false);
            SetHighlightVisible(selectedHighlight, false);
            UpdateSelectionText(null);
        }

        private void Update()
        {
            targetCamera ??= Camera.main;
            if (targetCamera == null)
            {
                return;
            }

            Vector3 mouseWorld = targetCamera.ScreenToWorldPoint(Input.mousePosition);
            mouseWorld.z = 0f;

            if (enableHover)
            {
                hoveredInfo = FindWorldInfoAt(mouseWorld);
                if (hoveredInfo != null && hoveredInfo.TryGetBounds(out Bounds bounds))
                {
                    PositionHighlight(hoverHighlight, bounds);
                }
                else
                {
                    SetHighlightVisible(hoverHighlight, false);
                }
            }

            if (enableClickSelection && Input.GetMouseButtonDown(0) && !IsPointerOverUI())
            {
                selectedInfo = FindWorldInfoAt(mouseWorld);
                selectedTile = null;

                if (selectedInfo != null)
                {
                    ShowSelectedObject(selectedInfo);
                    if (selectedInfo.TryGetBounds(out Bounds bounds))
                    {
                        PositionHighlight(selectedHighlight, bounds);
                    }
                }
                else if (TryGetMapTileAt(mouseWorld, out Vector2Int tile, out string tileText))
                {
                    selectedTile = tile;
                    ShowSelectedTile(tile, tileText);
                    Bounds tileBounds = new Bounds(Tool.GridToUnity(tile.x, tile.y), Vector3.one);
                    PositionHighlight(selectedHighlight, tileBounds);
                }
                else
                {
                    ClearSelection();
                    SetHighlightVisible(selectedHighlight, false);
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                selectedInfo = null;
                selectedTile = null;
                ClearSelection();
                SetHighlightVisible(selectedHighlight, false);
            }

            if (selectedInfo != null)
            {
                ShowSelectedObject(selectedInfo);
                if (selectedInfo.TryGetBounds(out Bounds bounds))
                {
                    PositionHighlight(selectedHighlight, bounds);
                }
            }
            else if (selectedTile.HasValue && TryGetMapTileAt(Tool.GridToUnity(selectedTile.Value.x, selectedTile.Value.y), out _, out string tileText))
            {
                ShowSelectedTile(selectedTile.Value, tileText);
            }
        }

        private static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private WorldObjectInfo FindWorldInfoAt(Vector3 worldPosition)
        {
            WorldObjectInfo best = null;
            float bestArea = float.MaxValue;

            foreach (WorldObjectInfo info in WorldObjectInfo.ActiveInfos)
            {
                if (info == null || !info.isActiveAndEnabled || !info.TryGetBounds(out Bounds bounds))
                {
                    continue;
                }

                bounds.Expand(0.12f);
                if (!bounds.Contains(worldPosition))
                {
                    continue;
                }

                float area = Mathf.Max(bounds.size.x * bounds.size.y, 0.001f);
                if (area < bestArea)
                {
                    best = info;
                    bestArea = area;
                }
            }

            return best;
        }

        private static bool TryGetMapTileAt(Vector3 worldPosition, out Vector2Int tile, out string displayText)
        {
            int row = Mathf.FloorToInt(Tool.GetMapRows() - worldPosition.y);
            int col = Mathf.FloorToInt(worldPosition.x);
            tile = new Vector2Int(row, col);
            displayText = string.Empty;

            if (CoreParam.map == null || row < 0 || col < 0 || row >= CoreParam.map.Height || col >= CoreParam.map.Width)
            {
                return false;
            }

            if (row >= CoreParam.map.Rows.Count || col >= CoreParam.map.Rows[row].Cols.Count)
            {
                return false;
            }

            PlaceType placeType = CoreParam.map.Rows[row].Cols[col];
            displayText = $"地图格\n坐标：({row}, {col})\n地形：{TranslatePlaceType(placeType)}";
            return true;
        }

        private void UpdateSelectionText(string value)
        {
            if (selectionText == null)
            {
                return;
            }

            selectionText.text = string.IsNullOrWhiteSpace(value)
                ? "选中对象\n点击地图上的单位、建筑、资源或地块查看详情\nEsc 清除选择"
                : value;
        }

        private void ShowSelectedObject(WorldObjectInfo info)
        {
            UpdateSelectionText(info != null ? info.BuildDisplayText() : null);
            inspectorPanel?.ShowObject(info);
        }

        private void ShowSelectedTile(Vector2Int tile, string tileText)
        {
            UpdateSelectionText(tileText);
            inspectorPanel?.ShowTile(tile, tileText);
        }

        private void ClearSelection()
        {
            UpdateSelectionText(null);
            inspectorPanel?.ClearSelection();
        }

        private static GameObject CreateHighlight(string name, Color color, int sortingOrder)
        {
            GameObject go = new GameObject(name);
            SpriteRenderer spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = GetPixelSprite();
            spriteRenderer.color = color;
            spriteRenderer.sortingOrder = sortingOrder;
            Object.DontDestroyOnLoad(go);
            return go;
        }

        private static void PositionHighlight(GameObject highlight, Bounds bounds)
        {
            if (highlight == null)
            {
                return;
            }

            highlight.SetActive(true);
            highlight.transform.position = new Vector3(bounds.center.x, bounds.center.y, -0.55f);
            highlight.transform.localScale = new Vector3(Mathf.Max(bounds.size.x + 0.18f, 0.45f), Mathf.Max(bounds.size.y + 0.18f, 0.45f), 1f);
        }

        private static void SetHighlightVisible(GameObject highlight, bool visible)
        {
            if (highlight != null)
            {
                highlight.SetActive(visible);
            }
        }

        private static Text FindTextByName(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            return go != null ? go.GetComponent<Text>() : null;
        }

        private static string TranslatePlaceType(PlaceType placeType)
        {
            return placeType switch
            {
                PlaceType.Factory => "工厂出生点",
                PlaceType.Space => "空地",
                PlaceType.Barrier => "障碍",
                PlaceType.Bush => "草丛",
                PlaceType.Resource => "资源区",
                PlaceType.ComputeCenter => "算力中心",
                PlaceType.Market => "市场",
                _ => "未知"
            };
        }

        private static Sprite pixelSprite;

        private static Sprite GetPixelSprite()
        {
            if (pixelSprite != null)
            {
                return pixelSprite;
            }

            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            pixelSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return pixelSprite;
        }

        private void OnDestroy()
        {
            if (hoverHighlight != null)
            {
                Destroy(hoverHighlight);
            }

            if (selectedHighlight != null)
            {
                Destroy(selectedHighlight);
            }
        }
    }
}
