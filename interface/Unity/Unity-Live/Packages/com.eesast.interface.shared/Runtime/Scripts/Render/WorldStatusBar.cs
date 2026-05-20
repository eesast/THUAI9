using UnityEngine;

namespace THUAI9.Unity.Render
{
    /// <summary>
    /// Lightweight 2D status bar rendered in world space.
    /// Used for current THUAI9 protocol fields only: load, resource remaining, occupy progress, observed HP.
    /// </summary>
    public class WorldStatusBar : MonoBehaviour
    {
        private GameObject background;
        private GameObject fill;
        private SpriteRenderer backgroundRenderer;
        private SpriteRenderer fillRenderer;
        private Vector2 size = new Vector2(0.9f, 0.07f);
        private Vector2 offset = new Vector2(0f, 0.55f);
        private float ratio = 1f;

        public void Configure(Color color, Vector2 barSize, Vector2 barOffset, int sortingOrder)
        {
            EnsureObjects();
            size = barSize;
            offset = barOffset;
            backgroundRenderer.sortingOrder = sortingOrder;
            fillRenderer.sortingOrder = sortingOrder + 1;
            backgroundRenderer.color = new Color(0.02f, 0.03f, 0.04f, 0.86f);
            fillRenderer.color = color;
            ApplyLayout();
        }

        public void SetRatio(float value)
        {
            ratio = Mathf.Clamp01(value);
            ApplyLayout();
        }

        private void LateUpdate()
        {
            ApplyLayout();
        }

        private void EnsureObjects()
        {
            if (background != null && fill != null)
            {
                return;
            }

            background = new GameObject("StatusBar_Background");
            background.transform.SetParent(transform, false);
            backgroundRenderer = background.AddComponent<SpriteRenderer>();
            backgroundRenderer.sprite = GetPixelSprite();

            fill = new GameObject("StatusBar_Fill");
            fill.transform.SetParent(transform, false);
            fillRenderer = fill.AddComponent<SpriteRenderer>();
            fillRenderer.sprite = GetPixelSprite();
        }

        private void ApplyLayout()
        {
            if (background == null || fill == null)
            {
                return;
            }

            // Keep bars screen/world aligned even when the owning unit rotates to show facing.
            transform.rotation = Quaternion.identity;

            background.transform.localPosition = new Vector3(offset.x, offset.y, -0.25f);
            background.transform.rotation = Quaternion.identity;
            background.transform.localScale = new Vector3(size.x, size.y, 1f);

            float fillWidth = Mathf.Max(size.x * ratio, 0.001f);
            fill.transform.localPosition = new Vector3(offset.x - size.x * 0.5f + fillWidth * 0.5f, offset.y, -0.30f);
            fill.transform.rotation = Quaternion.identity;
            fill.transform.localScale = new Vector3(fillWidth, size.y * 0.72f, 1f);
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
    }
}
