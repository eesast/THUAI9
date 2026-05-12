using THUAI9.Unity.Core;
using THUAI9.Unity.Render;
using UnityEngine;
using UnityEngine.EventSystems;

namespace THUAI9.Unity.UI
{
    /// <summary>
    /// Click/hover selection for THUAI9 world objects.
    /// It intentionally depends on WorldObjectInfo metadata and current map tiles,
    /// not on any THUAI7/THUAI8 protocol concepts.
    /// </summary>
    public class WorldSelectionController : MonoBehaviour
    {
        public Camera targetCamera;
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
            hoverHighlight = CreateHighlight("HoverHighlight", new Color(0.18f, 0.88f, 0.96f, 0.22f), 24);
            selectedHighlight = CreateHighlight("SelectionHighlight", new Color(1f, 0.72f, 0.22f, 0.30f), 25);
            SetHighlightVisible(hoverHighlight, false);
            SetHighlightVisible(selectedHighlight, false);
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
                if (hoveredInfo != null && hoveredInfo.TryGetBounds(out Bounds hoverBounds))
                {
                    PositionHighlight(hoverHighlight, hoverBounds);
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
                    if (selectedInfo.TryGetBounds(out Bounds selectedBounds))
                    {
                        PositionHighlight(selectedHighlight, selectedBounds);
                    }
                }
                else if (TryGetMapTileAt(mouseWorld, out Vector2Int tile))
                {
                    selectedTile = tile;
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
                if (selectedInfo.TryGetBounds(out Bounds currentBounds))
                {
                    PositionHighlight(selectedHighlight, currentBounds);
                }
                else
                {
                    ClearSelection();
                    SetHighlightVisible(selectedHighlight, false);
                }
            }
            else if (selectedTile.HasValue)
            {
                if (IsMapTileValid(selectedTile.Value))
                {
                    Bounds tileBounds = new Bounds(Tool.GridToUnity(selectedTile.Value.x, selectedTile.Value.y), Vector3.one);
                    PositionHighlight(selectedHighlight, tileBounds);
                }
                else
                {
                    ClearSelection();
                    SetHighlightVisible(selectedHighlight, false);
                }
            }
            else
            {
                SetHighlightVisible(selectedHighlight, false);
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

        private static bool TryGetMapTileAt(Vector3 worldPosition, out Vector2Int tile)
        {
            int row = Mathf.FloorToInt(Tool.GetMapRows() - worldPosition.y);
            int col = Mathf.FloorToInt(worldPosition.x);
            tile = new Vector2Int(row, col);

            if (CoreParam.map == null || row < 0 || col < 0 || row >= CoreParam.map.Height || col >= CoreParam.map.Width)
            {
                return false;
            }

            if (row >= CoreParam.map.Rows.Count || col >= CoreParam.map.Rows[row].Cols.Count)
            {
                return false;
            }

            return true;
        }

        private static bool IsMapTileValid(Vector2Int tile)
        {
            if (CoreParam.map == null || tile.x < 0 || tile.y < 0 || tile.x >= CoreParam.map.Height || tile.y >= CoreParam.map.Width)
            {
                return false;
            }

            return tile.x < CoreParam.map.Rows.Count && tile.y < CoreParam.map.Rows[tile.x].Cols.Count;
        }

        private void ClearSelection()
        {
            selectedInfo = null;
            selectedTile = null;
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
