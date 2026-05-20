using UnityEngine;

namespace THUAI9.Unity.Generated
{
    /// <summary>
    /// Lightweight metadata attached to generated sprite prefabs so UI/render code can identify source assets.
    /// </summary>
    public sealed class SpriteAssetInfo : MonoBehaviour
    {
        [Header("Generated Asset Metadata")]
        public string sourceAssetPath;
        public string category;
        public bool isAnimated;
        public int frameCount = 1;
        public Vector2Int pixelSize;
        public int recommendedPixelsPerUnit = 32;
    }
}
