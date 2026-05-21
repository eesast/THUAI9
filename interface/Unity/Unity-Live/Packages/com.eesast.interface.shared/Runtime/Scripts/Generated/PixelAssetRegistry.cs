using System;
using System.Collections.Generic;
using UnityEngine;

namespace THUAI9.Unity.Generated
{
    [Serializable]
    public sealed class NamedSprite
    {
        public string key;
        public Sprite sprite;
    }

    [Serializable]
    public sealed class NamedPrefab
    {
        public string key;
        public GameObject prefab;
    }

    /// <summary>
    /// Generated runtime lookup table for pixel art assets.
    /// It avoids AssetDatabase/Editor-only loading during play mode.
    /// </summary>
    [CreateAssetMenu(fileName = "PixelAssetRegistry", menuName = "Pixel Assets/Registry")]
    public sealed class PixelAssetRegistry : ScriptableObject
    {
        public List<NamedSprite> sprites = new List<NamedSprite>();
        public List<NamedPrefab> prefabs = new List<NamedPrefab>();

        private Dictionary<string, Sprite> _spriteMap;
        private Dictionary<string, GameObject> _prefabMap;

        public Sprite GetSprite(string key)
        {
            EnsureCache();
            return !string.IsNullOrEmpty(key) && _spriteMap.TryGetValue(key, out Sprite sprite) ? sprite : null;
        }

        public GameObject GetPrefab(string key)
        {
            EnsureCache();
            return !string.IsNullOrEmpty(key) && _prefabMap.TryGetValue(key, out GameObject prefab) ? prefab : null;
        }

        public bool HasSprite(string key) => GetSprite(key) != null;
        public bool HasPrefab(string key) => GetPrefab(key) != null;

        public void ClearRuntimeCache()
        {
            _spriteMap = null;
            _prefabMap = null;
        }

        private void EnsureCache()
        {
            if (_spriteMap != null && _prefabMap != null)
            {
                return;
            }

            _spriteMap = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            foreach (NamedSprite entry in sprites)
            {
                if (entry?.sprite == null || string.IsNullOrWhiteSpace(entry.key))
                {
                    continue;
                }
                _spriteMap[entry.key] = entry.sprite;
            }

            _prefabMap = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            foreach (NamedPrefab entry in prefabs)
            {
                if (entry?.prefab == null || string.IsNullOrWhiteSpace(entry.key))
                {
                    continue;
                }
                _prefabMap[entry.key] = entry.prefab;
            }
        }
    }
}
