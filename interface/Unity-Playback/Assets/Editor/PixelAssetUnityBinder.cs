using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using THUAI9.Unity.Generated;
using THUAI9.Unity.Render;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PixelAssetUnityBinder
{
    private const string AssetRoot = "Assets/PixelAssets";
    private const string ArtRoot = AssetRoot + "/Art/Pixel";
    private const string PrefabRoot = AssetRoot + "/Prefabs";
    private const string StaticPrefabRoot = PrefabRoot + "/Static";
    private const string AnimatedPrefabRoot = PrefabRoot + "/Animated";
    private const string GalleryPrefabPath = PrefabRoot + "/Gallery/PixelAssetGallery.prefab";
    private const string AnimationRoot = AssetRoot + "/Animations";
    private const string ControllerRoot = AssetRoot + "/AnimatorControllers";
    private const string GalleryScenePath = "Assets/Scenes/AssetGallery.unity";
    private const string ModeScenePath = "Assets/Scenes/Playback.unity";
    private const string RegistryPath = AssetRoot + "/Generated/PixelAssetRegistry.asset";
    private const int PixelsPerUnit = 32;
    private const float AnimationFps = 8f;

    [MenuItem("Tools/Generated Assets/Rebuild Pixel Runtime Registry")]
    public static void RebuildAllFromMenu()
    {
        RunAll();
    }

    public static void RunAll()
    {
        if (!AssetDatabase.IsValidFolder(AssetRoot))
        {
            Debug.LogError("[Assets] Missing asset root: " + AssetRoot);
            return;
        }

        EnsureFolder(AssetRoot + "/Generated");

        AssetDatabase.StartAssetEditing();
        try
        {
            ConfigureTextureImporters();
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.Refresh();

        PixelAssetRegistry registry = CreatePixelAssetRegistry();
        AddScenesToBuildSettings();
        AssignRegistryToModeScene(registry);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int spriteRefs = registry != null ? registry.sprites.Count : 0;
        int prefabRefs = registry != null ? registry.prefabs.Count : 0;
        Debug.Log($"[Assets] Done. Runtime registry sprites: {spriteRefs}, prefabs: {prefabRefs}. mode scene wired: {ModeScenePath}");
    }

    private static void ConfigureTextureImporters()
    {
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { AssetRoot });
        foreach (string guid in textureGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                continue;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);

            importer.SaveAndReimport();
        }
    }

    private static int CreateStaticPrefabs()
    {
        int count = 0;
        foreach (string spritePath in EnumerateSpritePaths())
        {
            if (IsAnimationFrame(spritePath) || IsPreviewImage(spritePath))
            {
                continue;
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (sprite == null)
            {
                continue;
            }

            string relative = StripPrefix(spritePath, ArtRoot + "/");
            string category = NormalizeCategory(Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? "Misc");
            string prefabPath = StaticPrefabRoot + "/" + category + "/" + Path.GetFileNameWithoutExtension(spritePath) + ".prefab";
            EnsureFolder(Path.GetDirectoryName(prefabPath)?.Replace('\\', '/') ?? StaticPrefabRoot);

            GameObject root = new GameObject(Path.GetFileNameWithoutExtension(spritePath));
            try
            {
                SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = 0;

                SpriteAssetInfo info = root.AddComponent<SpriteAssetInfo>();
                info.sourceAssetPath = spritePath;
                info.category = category;
                info.isAnimated = false;
                info.frameCount = 1;
                info.pixelSize = GetTextureSize(spritePath);
                info.recommendedPixelsPerUnit = PixelsPerUnit;

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (success)
                {
                    count++;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        return count;
    }

    private static int CreateAnimatedPrefabs()
    {
        int count = 0;
        Dictionary<string, List<string>> groups = new Dictionary<string, List<string>>();
        foreach (string spritePath in EnumerateSpritePaths())
        {
            if (!IsAnimationFrame(spritePath))
            {
                continue;
            }

            string dir = Path.GetDirectoryName(spritePath)?.Replace('\\', '/') ?? ArtRoot;
            if (!groups.TryGetValue(dir, out List<string> paths))
            {
                paths = new List<string>();
                groups[dir] = paths;
            }
            paths.Add(spritePath);
        }

        foreach (KeyValuePair<string, List<string>> entry in groups.OrderBy(g => g.Key))
        {
            List<string> framePaths = entry.Value.OrderBy(NaturalFrameSortKey).ToList();
            if (framePaths.Count < 2)
            {
                continue;
            }

            List<Sprite> sprites = framePaths.Select(path => AssetDatabase.LoadAssetAtPath<Sprite>(path)).Where(sprite => sprite != null).ToList();
            if (sprites.Count < 2)
            {
                continue;
            }

            string relativeDir = StripPrefix(entry.Key, ArtRoot + "/");
            string category = NormalizeCategory(Path.GetDirectoryName(relativeDir)?.Replace('\\', '/') ?? "Animated");
            string animationName = SanitizeFileName(Path.GetFileName(entry.Key));
            string prefabPath = AnimatedPrefabRoot + "/" + category + "/" + animationName + ".prefab";
            string clipPath = AnimationRoot + "/" + category + "/" + animationName + ".anim";
            string controllerPath = ControllerRoot + "/" + category + "/" + animationName + ".controller";
            EnsureFolder(Path.GetDirectoryName(prefabPath)?.Replace('\\', '/') ?? AnimatedPrefabRoot);
            EnsureFolder(Path.GetDirectoryName(clipPath)?.Replace('\\', '/') ?? AnimationRoot);
            EnsureFolder(Path.GetDirectoryName(controllerPath)?.Replace('\\', '/') ?? ControllerRoot);

            AnimationClip clip = CreateAnimationClip(sprites, clipPath);
            AnimatorController controller = CreateAnimatorController(animationName, clip, controllerPath);

            GameObject root = new GameObject(animationName);
            try
            {
                SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
                renderer.sprite = sprites[0];
                Animator animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;

                SpriteAssetInfo info = root.AddComponent<SpriteAssetInfo>();
                info.sourceAssetPath = entry.Key;
                info.category = category;
                info.isAnimated = true;
                info.frameCount = sprites.Count;
                info.pixelSize = GetTextureSize(framePaths[0]);
                info.recommendedPixelsPerUnit = PixelsPerUnit;

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (success)
                {
                    count++;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        return count;
    }

    private static AnimationClip CreateAnimationClip(List<Sprite> sprites, string clipPath)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, clipPath);
        }

        clip.ClearCurves();
        clip.frameRate = AnimationFps;
        clip.wrapMode = WrapMode.Loop;

        ObjectReferenceKeyframe[] keyframes = new ObjectReferenceKeyframe[sprites.Count];
        for (int i = 0; i < sprites.Count; i++)
        {
            keyframes[i] = new ObjectReferenceKeyframe
            {
                time = i / AnimationFps,
                value = sprites[i]
            };
        }

        EditorCurveBinding binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = string.Empty,
            propertyName = "m_Sprite"
        };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static AnimatorController CreateAnimatorController(string animationName, AnimationClip clip, string controllerPath)
    {
        AssetDatabase.DeleteAsset(controllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState state = stateMachine.AddState("Play_" + animationName);
        state.motion = clip;
        stateMachine.defaultState = state;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static PixelAssetRegistry CreatePixelAssetRegistry()
    {
        PixelAssetRegistry registry = AssetDatabase.LoadAssetAtPath<PixelAssetRegistry>(RegistryPath);
        if (registry == null)
        {
            registry = ScriptableObject.CreateInstance<PixelAssetRegistry>();
            AssetDatabase.CreateAsset(registry, RegistryPath);
        }

        registry.sprites.Clear();
        registry.prefabs.Clear();
        HashSet<string> spriteKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> prefabKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string guid in AssetDatabase.FindAssets("", new[] { ArtRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                continue;
            }

            string stem = Path.GetFileNameWithoutExtension(path);
            string relative = StripPrefix(path, ArtRoot + "/");
            relative = relative.Substring(0, relative.Length - Path.GetExtension(relative).Length).Replace('\\', '/');
            AddSpriteRegistryEntry(registry, spriteKeys, stem, sprite);
            AddSpriteRegistryEntry(registry, spriteKeys, relative, sprite);
        }

        // The runtime viewer now uses direct sprite registry lookups plus
        // Resources/Runtime unit frames. Generated prefab/gallery assets were
        // removed because they duplicated old source-pack content and were not
        // used by the mode scene.

        registry.ClearRuntimeCache();
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();
        return registry;
    }

    private static void AddSpriteRegistryEntry(PixelAssetRegistry registry, HashSet<string> keys, string key, Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(key) || sprite == null || !keys.Add(key))
        {
            return;
        }
        registry.sprites.Add(new NamedSprite { key = key, sprite = sprite });
    }

    private static void AddPrefabRegistryEntry(PixelAssetRegistry registry, HashSet<string> keys, string key, GameObject prefab)
    {
        if (string.IsNullOrWhiteSpace(key) || prefab == null || !keys.Add(key))
        {
            return;
        }
        registry.prefabs.Add(new NamedPrefab { key = key, prefab = prefab });
    }

    private static void AssignRegistryToModeScene(PixelAssetRegistry registry)
    {
        if (registry == null || !File.Exists(ModeScenePath))
        {
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(ModeScenePath, OpenSceneMode.Single);
        RenderManager renderManager = UnityEngine.Object.FindObjectOfType<RenderManager>();
        if (renderManager == null)
        {
            Debug.LogWarning("[Assets] mode scene has no RenderManager to wire pixel asset registry.");
            return;
        }

        Undo.RecordObject(renderManager, "Wire pixel asset registry");
        renderManager.pixelAssets = registry;
        renderManager.usePixelAssets = true;
        renderManager.useAnimatedUnitPrefabs = true;
        EditorUtility.SetDirty(renderManager);

        PixelDemoBootstrap demoBootstrap = UnityEngine.Object.FindObjectOfType<PixelDemoBootstrap>();
        if (demoBootstrap == null)
        {
            GameObject demoObject = new GameObject("Pixel Demo Bootstrap");
            demoBootstrap = demoObject.AddComponent<PixelDemoBootstrap>();
        }
        Undo.RecordObject(demoBootstrap, "Wire pixel demo bootstrap");
        demoBootstrap.pixelAssets = registry;
        demoBootstrap.showWhenFrameSourceMissing = false;
        demoBootstrap.columns = 50;
        demoBootstrap.rows = 50;
        EditorUtility.SetDirty(demoBootstrap);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static int CreateGallerySceneAndPrefab()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { StaticPrefabRoot, AnimatedPrefabRoot });
        List<string> prefabPaths = prefabGuids.Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path)
            .ToList();

        EditorSceneManager.SaveOpenScenes();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "AssetGallery";

        GameObject root = new GameObject("PixelAssetGallery");
        GameObject cameraObject = new GameObject("Main Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.055f, 0.063f, 0.082f, 1f);
        cameraObject.tag = "MainCamera";

        GameObject lightObject = new GameObject("Gallery Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.75f;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        int columns = 18;
        float cellX = 2.6f;
        float cellY = 2.35f;
        int index = 0;
        foreach (string prefabPath in prefabPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                continue;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
            {
                continue;
            }

            instance.name = Path.GetFileNameWithoutExtension(prefabPath);
            instance.transform.SetParent(root.transform, false);
            int col = index % columns;
            int row = index / columns;
            instance.transform.localPosition = new Vector3(col * cellX, -row * cellY, 0f);

            AddLabel(instance.transform, ShortLabel(instance.name), new Vector3(0f, -0.82f, 0f));
            index++;
        }

        int rows = Mathf.Max(1, Mathf.CeilToInt(index / (float)columns));
        float width = (Mathf.Min(columns, Math.Max(index, 1)) - 1) * cellX;
        float height = (rows - 1) * cellY;
        root.transform.position = new Vector3(-width / 2f, height / 2f, 0f);
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        camera.orthographicSize = Mathf.Max(8f, height / 2f + 3.5f);

        AddTitle("Pixel Asset Gallery - Prefabs generated from pixel art pack", new Vector3(-width / 2f, height / 2f + 1.65f, 0f));

        EnsureFolder(Path.GetDirectoryName(GalleryPrefabPath)?.Replace('\\', '/') ?? PrefabRoot);
        PrefabUtility.SaveAsPrefabAsset(root, GalleryPrefabPath);
        EditorSceneManager.SaveScene(scene, GalleryScenePath);
        return index;
    }

    private static void AddLabel(Transform parent, string label, Vector3 localPosition)
    {
        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(parent, false);
        labelObject.transform.localPosition = localPosition;
        TextMesh text = labelObject.AddComponent<TextMesh>();
        text.text = label;
        text.characterSize = 0.085f;
        text.fontSize = 24;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = new Color(0.78f, 0.86f, 0.92f, 1f);
    }

    private static void AddTitle(string title, Vector3 position)
    {
        GameObject titleObject = new GameObject("Gallery Title");
        titleObject.transform.position = position;
        TextMesh text = titleObject.AddComponent<TextMesh>();
        text.text = title;
        text.characterSize = 0.18f;
        text.fontSize = 32;
        text.anchor = TextAnchor.MiddleLeft;
        text.alignment = TextAlignment.Left;
        text.color = new Color(0.70f, 0.93f, 1f, 1f);
    }

    private static void AddScenesToBuildSettings()
    {
        HashSet<string> desired = new HashSet<string>
        {
            ModeScenePath
        };

        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes
            .Where(scene => !string.IsNullOrEmpty(scene.path) && desired.Contains(scene.path))
            .ToList();

        foreach (string path in desired)
        {
            if (File.Exists(path) && scenes.All(scene => scene.path != path))
            {
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static IEnumerable<string> EnumerateSpritePaths()
    {
        if (!AssetDatabase.IsValidFolder(ArtRoot))
        {
            return Enumerable.Empty<string>();
        }

        return AssetDatabase.FindAssets("t:Texture2D", new[] { ArtRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path);
    }

    private static bool IsAnimationFrame(string path)
    {
        string lower = path.ToLowerInvariant();
        return lower.Contains("/frames/") || lower.Contains("/animation/") || lower.Contains("_frame_");
    }

    private static bool IsPreviewImage(string path)
    {
        return path.ToLowerInvariant().Contains("/preview/");
    }

    private static Vector2Int GetTextureSize(string path)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture == null)
        {
            return Vector2Int.zero;
        }
        return new Vector2Int(texture.width, texture.height);
    }

    private static string NaturalFrameSortKey(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int marker = name.LastIndexOf("_frame_", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return name;
        }
        string suffix = name.Substring(marker + "_frame_".Length);
        if (int.TryParse(suffix, out int number))
        {
            return name.Substring(0, marker) + number.ToString("D6");
        }
        return name;
    }

    private static string StripPrefix(string value, string prefix)
    {
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return value.Substring(prefix.Length);
        }
        return value;
    }

    private static string NormalizeCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return "Misc";
        }
        return string.Join("/", category.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Select(SanitizeFileName));
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unnamed";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return sanitized.Replace(' ', '_');
    }

    private static string ShortLabel(string value)
    {
        if (value.Length <= 24)
        {
            return value;
        }
        return value.Substring(0, 21) + "...";
    }

    private static void EnsureFolder(string folderPath)
    {
        folderPath = folderPath.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}

