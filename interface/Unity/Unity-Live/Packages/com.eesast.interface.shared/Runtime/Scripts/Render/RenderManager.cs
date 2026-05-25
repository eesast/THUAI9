using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Protobuf;
using THUAI9.Unity.Core;
using THUAI9.Unity.Generated;
using UnityEngine;
using UnityEngine.UI;

namespace THUAI9.Unity.Render
{
    public class RenderManager : SingletonMono<RenderManager>
    {
        [Header("对局时间")]
        public Text gameTimeText;

        [Header("队伍信息")]
        public Text[] teamScoreTexts = new Text[4];

        [Header("调试信息")]
        public Text fpsText;
        public bool autoBindSceneReferences = true;
        public bool showWorldLabels = true;

        [Header("Pixel Assets")]
        public PixelAssetRegistry pixelAssets;
        public bool usePixelAssets = true;
        public bool useAnimatedUnitPrefabs = true;
        public bool useCompactRuntimeUnitSprites = true;

        public delegate void RenderManagerCallback();
        public RenderManagerCallback onRender;
        public RenderManagerCallback onFirstFrame;

        private const int DefaultFrameIntervalMs = 25;
        private const int MaxQueueSize = 100;
        private const int MaxLiveCatchUpFramesPerUpdate = 4;

        private readonly Dictionary<PlaceType, Color> _mapColors = new()
        {
            { PlaceType.Space, new Color(0.93f, 0.93f, 0.93f, 1f) },
            { PlaceType.Factory, new Color(0.55f, 0.55f, 0.55f, 1f) },
            { PlaceType.Barrier, new Color(0.20f, 0.20f, 0.20f, 1f) },
            { PlaceType.Bush, new Color(0.41f, 0.71f, 0.36f, 1f) },
            { PlaceType.Resource, new Color(0.32f, 0.66f, 0.92f, 1f) },
            { PlaceType.ComputeCenter, new Color(0.72f, 0.47f, 0.90f, 1f) },
            { PlaceType.Market, new Color(0.98f, 0.71f, 0.27f, 1f) }
        };

        private Coroutine _updateCoroutine;
        private bool _usingFallbackGroundMap;
        private int _frameLoopTickCount;
        private string _lastRenderError = string.Empty;
        private readonly Dictionary<long, Vector2> _previousCharacterPositions = new();
        private readonly Dictionary<long, int> _previousCharacterLoads = new();
        private readonly Dictionary<long, long> _previousCharacterAttackCooldowns = new();
        private readonly Dictionary<long, string> _lastRuntimeUnitDirections = new();

        public bool IsFrameLoopRunning => _updateCoroutine != null;
        public int FrameLoopTickCount => _frameLoopTickCount;
        public string LastRenderError => _lastRenderError;

        private enum RuntimeUnitAction
        {
            Idle,
            Move,
            Harvest,
            Attack
        }

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this)
            {
                return;
            }

            if (autoBindSceneReferences)
            {
                AutoBindSceneReferences();
            }

            if (pixelAssets != null)
            {
                pixelAssets.ClearRuntimeCache();
            }

            FrameSourceHub.BindMainThread();
            SubscribeFrameSourceEvents();
        }

        private void OnEnable()
        {
            FrameSourceHub.BindMainThread();
            SubscribeFrameSourceEvents();
            EnsureUpdateCoroutine();
        }

        private void Start()
        {
            EnsureUpdateCoroutine();
        }

        private void Update()
        {
            EnsureUpdateCoroutine();

            // Live spectator frames arrive from a background gRPC stream.  The
            // coroutine below is the normal consumer, but this Update-side pump is
            // an explicit watchdog for editor/runtime cases where a coroutine was
            // interrupted during Play Mode transitions: if the queue is growing,
            // render a bounded number of frames on the main thread immediately.
            if (FrameSourceHub.ActiveKind == FrameSourceHub.SourceKind.Live && FrameSourceHub.QueueSize > 0)
            {
                int catchUpFrames = Mathf.Clamp(FrameSourceHub.QueueSize / 25 + 1, 1, MaxLiveCatchUpFramesPerUpdate);
                PumpQueuedFrames(catchUpFrames);
            }
        }

        private IEnumerator UpdateFrameLoop()
        {
            while (isActiveAndEnabled)
            {
                _frameLoopTickCount++;
                int waitMilliseconds = GetFrameInterval();
                UpdateFpsText(waitMilliseconds);

                PumpQueuedFrames(1);

                yield return new WaitForSecondsRealtime(waitMilliseconds / 1000f);
            }

            _updateCoroutine = null;
        }

        private void EnsureUpdateCoroutine()
        {
            if (_updateCoroutine == null && isActiveAndEnabled)
            {
                _updateCoroutine = StartCoroutine(UpdateFrameLoop());
            }
        }

        private void SubscribeFrameSourceEvents()
        {
            FrameSourceHub.ImmediateFrameSubmitted -= OnImmediateFrameSubmitted;
            FrameSourceHub.ImmediateFrameSubmitted += OnImmediateFrameSubmitted;
            FrameSourceHub.PumpRequested -= OnFramePumpRequested;
            FrameSourceHub.PumpRequested += OnFramePumpRequested;
        }

        public int PumpQueuedFrames(int maxFrames)
        {
            int rendered = 0;
            int frameBudget = Mathf.Max(maxFrames, 0);
            while (rendered < frameBudget)
            {
                MessageToClient frame = GetNextFrame();
                if (frame == null)
                {
                    break;
                }

                try
                {
                    RenderFrame(frame);
                    rendered++;
                }
                catch (Exception ex)
                {
                    _lastRenderError = ex.Message;
                    Debug.LogException(ex, this);
                    break;
                }
            }

            return rendered;
        }

        private void OnFramePumpRequested()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            int catchUpFrames = Mathf.Clamp(FrameSourceHub.QueueSize / 25 + 1, 1, MaxLiveCatchUpFramesPerUpdate);
            PumpQueuedFrames(catchUpFrames);
            if (FrameSourceHub.ActiveKind == FrameSourceHub.SourceKind.Live && FrameSourceHub.QueueSize > 0)
            {
                FrameSourceHub.RequestPump();
            }
        }

        public void RenderFrame(MessageToClient frame)
        {
            if (frame == null)
            {
                return;
            }

            bool isFirstVisualFrame = !CoreParam.initialized;
            if (isFirstVisualFrame)
            {
                ResetRuntimeAnimationState();
            }

            CoreParam.currentFrame = frame;
            DealFrame(frame);
            ShowFrame();
            FrameSourceHub.MarkRendered(frame);

            if (isFirstVisualFrame)
            {
                try { onFirstFrame?.Invoke(); } catch { }
            }
            else
            {
                try { onRender?.Invoke(); } catch { }
            }
        }

        private void AutoBindSceneReferences()
        {
            gameTimeText ??= FindTextByName("GameTimeText");
            fpsText ??= FindTextByName("FPSText");

            if (teamScoreTexts == null || teamScoreTexts.Length != 4)
            {
                teamScoreTexts = new Text[4];
            }
            for (int i = 0; i < teamScoreTexts.Length; i++)
            {
                teamScoreTexts[i] ??= FindTextByName($"TeamScoreText{i + 1}");
            }
        }

        private int GetFrameInterval()
        {
            int queueSize = FrameSourceHub.QueueSize;
            if (queueSize < 50)
            {
                return DefaultFrameIntervalMs;
            }

            FrameSourceHub.TrimQueueTo(MaxQueueSize);

            return Mathf.Max(1, 1000 / Mathf.Max(FrameSourceHub.QueueSize, 1));
        }

        private void UpdateFpsText(int frameIntervalMs)
        {
            if (fpsText != null)
            {
                fpsText.text = $"FPS: {1000 / Mathf.Max(frameIntervalMs, 1)}";
            }
        }

        private MessageToClient GetNextFrame()
        {
            return FrameSourceHub.TryDequeueFrame(out MessageToClient frame) ? frame : null;
        }

        private void OnImmediateFrameSubmitted(MessageToClient frame)
        {
            RenderFrame(frame);
        }

        private void DealFrame(MessageToClient info)
        {
            CoreParam.characters.Clear();
            CoreParam.teams.Clear();
            CoreParam.factories.Clear();
            CoreParam.computeCenters.Clear();
            CoreParam.markets.Clear();
            CoreParam.resources.Clear();
            CoreParam.barriers.Clear();
            CoreParam.bushes.Clear();
            CoreParam.gameState = info.GameState;
            // The current THUAI9 client frame only carries GameState and AllMessage.
            // GameMode / AI event fields still exist as UI/Core placeholders, but the
            // generated MessageToClient no longer exposes them.
            CoreParam.gameMode = GameMode.NullGameMode;
            CoreParam.latestAIEvent = null;
            CoreParam.latestAIEffect = null;

            foreach (MessageOfObj obj in info.ObjMessage)
            {
                DealObj(obj);
            }

            if (info.AllMessage != null)
            {
                CoreParam.SetAllMessage(info.AllMessage, info.GameState);
            }

            CoreParam.frameCount++;
        }

        private void DealObj(MessageOfObj obj)
        {
            switch (obj.MessageOfObjCase)
            {
                case MessageOfObj.MessageOfObjOneofCase.MapMessage:
                    CoreParam.map = obj.MapMessage;
                    break;
                case MessageOfObj.MessageOfObjOneofCase.CharacterMessage:
                    CoreParam.characters[obj.CharacterMessage.Guid] = obj.CharacterMessage;
                    break;
                case MessageOfObj.MessageOfObjOneofCase.TeamMessage:
                    CoreParam.teams[obj.TeamMessage.TeamId] = obj.TeamMessage;
                    break;
                case MessageOfObj.MessageOfObjOneofCase.FactoryMessage:
                    CoreParam.factories[MakeGridKey(obj.FactoryMessage.X, obj.FactoryMessage.Y)] = obj.FactoryMessage;
                    break;
                case MessageOfObj.MessageOfObjOneofCase.ComputeCenterMessage:
                    CoreParam.computeCenters[MakeGridKey(obj.ComputeCenterMessage.X, obj.ComputeCenterMessage.Y)] = obj.ComputeCenterMessage;
                    break;
                case MessageOfObj.MessageOfObjOneofCase.MarketMessage:
                    CoreParam.markets[MakeGridKey(obj.MarketMessage.X, obj.MarketMessage.Y)] = obj.MarketMessage;
                    break;
                case MessageOfObj.MessageOfObjOneofCase.ResourceMessage:
                    CoreParam.resources[MakeGridKey(obj.ResourceMessage.X, obj.ResourceMessage.Y)] = obj.ResourceMessage;
                    break;
                case MessageOfObj.MessageOfObjOneofCase.BarrierMessage:
                    CoreParam.barriers[MakeGridKey(obj.BarrierMessage.X, obj.BarrierMessage.Y)] = obj.BarrierMessage;
                    break;
                case MessageOfObj.MessageOfObjOneofCase.BushMessage:
                    CoreParam.bushes[MakeGridKey(obj.BushMessage.X, obj.BushMessage.Y)] = obj.BushMessage;
                    break;
            }
        }

        private static Tuple<int, int> MakeGridKey(int gameX, int gameY)
        {
            Vector2Int grid = Tool.GameToGrid(gameX, gameY);
            return new Tuple<int, int>(grid.x, grid.y);
        }

        private void ShowFrame()
        {
            UpdateUI();
            ShowMap();
            ShowCharacters();
            ShowFactories();
            ShowSnapshotObjects(CoreParam.computeCenters, CoreParam.computeCentersG, CreateComputeCenter, UpdateComputeCenter);
            ShowSnapshotObjects(CoreParam.markets, CoreParam.marketsG, CreateMarket, UpdateMarket);
            ShowSnapshotObjects(CoreParam.resources, CoreParam.resourcesG, CreateResource, UpdateResource);
            if (CoreParam.map == null)
            {
                ShowSnapshotObjects(CoreParam.barriers, CoreParam.barriersG, CreateBarrier, UpdateBarrier);
            }
            else
            {
                // The authoritative map already renders barrier tiles with the approved
                // hazard-striped concrete sprites. Do not draw the per-frame barrier
                // messages on top, otherwise the scene regresses to the noisy machinery
                // overlays and looks different from the accepted first-frame layout.
                ClearSnapshotObjects(CoreParam.barriersG);
            }
            ShowSnapshotObjects(CoreParam.bushes, CoreParam.bushesG, CreateBush, UpdateBush);

            CoreParam.initialized = true;
        }

        private void UpdateUI()
        {
            if (gameTimeText != null)
            {
                int totalMilliseconds = CoreParam.playbackCurrentFrameIndex >= 0
                    ? CoreParam.playbackElapsedMilliseconds
                    : CoreParam.stableLiveGameMilliseconds;
                gameTimeText.text = FormatPlaybackTime(totalMilliseconds);
            }

            // Team status text is owned by UIController.  Keeping this renderer-side
            // writer disabled prevents playback frames from racing the per-frame UI
            // refresh and making the right-side team panel appear to jitter.
        }

        private void ShowMap()
        {
            if (CoreParam.map == null)
            {
                ShowFallbackGroundMap();
                return;
            }

            int rows = Mathf.Max((int)CoreParam.map.Height, 1);
            int cols = Mathf.Max((int)CoreParam.map.Width, 1);
            if (_usingFallbackGroundMap || !HasCompleteMapTiles(rows, cols))
            {
                ClearSnapshotObjects(CoreParam.mapTilesG);
                _usingFallbackGroundMap = false;
            }

            if (CoreParam.mapTilesG.Count > 0)
            {
                return;
            }

            for (int row = 0; row < CoreParam.map.Height; row++)
            {
                if (row >= CoreParam.map.Rows.Count)
                {
                    break;
                }

                for (int col = 0; col < CoreParam.map.Width; col++)
                {
                    if (col >= CoreParam.map.Rows[row].Cols.Count)
                    {
                        break;
                    }

                    PlaceType placeType = CoreParam.map.Rows[row].Cols[col];
                    Tuple<int, int> key = new Tuple<int, int>(row, col);
                    CoreParam.mapTilesG[key] = CreateMapTile(row, col, placeType);
                }
            }
        }

        private void ShowFallbackGroundMap()
        {
            int rows = Mathf.Max(Tool.GetMapRows(), 1);
            int cols = Mathf.Max(Tool.GetMapCols(), 1);
            if (HasCompleteMapTiles(rows, cols))
            {
                return;
            }

            ClearSnapshotObjects(CoreParam.mapTilesG);
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    Tuple<int, int> key = new Tuple<int, int>(row, col);
                    CoreParam.mapTilesG[key] = CreateMapTile(row, col, PlaceType.Space);
                }
            }

            _usingFallbackGroundMap = true;
        }

        private static bool HasCompleteMapTiles(int rows, int cols)
        {
            if (rows <= 0 || cols <= 0)
            {
                return false;
            }

            if (CoreParam.mapTilesG.Count < rows * cols)
            {
                return false;
            }

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    Tuple<int, int> key = new Tuple<int, int>(row, col);
                    if (!CoreParam.mapTilesG.TryGetValue(key, out GameObject tile) || tile == null)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private GameObject CreateMapTile(int row, int col, PlaceType placeType)
        {
            Vector2 unityPos = Tool.GridToUnity(row, col);
            string spriteKey = GetMapTileSpriteKey(placeType, row, col);
            if (TryCreateSpriteObject($"Tile_{row}_{col}_{placeType}", spriteKey, new Vector3(unityPos.x, unityPos.y, 1.5f), Vector3.one, -100, out GameObject spriteTile))
            {
                return spriteTile;
            }

            GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
            tile.name = $"Tile_{row}_{col}_{placeType}";
            tile.transform.position = new Vector3(unityPos.x, unityPos.y, 1.5f);
            tile.transform.localScale = Vector3.one;

            Renderer renderer = tile.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material material = new Material(Shader.Find("Unlit/Color"));
                material.color = _mapColors.TryGetValue(placeType, out Color color) ? color : Color.magenta;
                renderer.material = material;
            }

            RemoveCollider(tile);
            return tile;
        }

        private void ShowCharacters()
        {
            foreach (var kvp in CoreParam.characters)
            {
                if (!CoreParam.charactersG.ContainsKey(kvp.Key))
                {
                    CoreParam.charactersG[kvp.Key] = CreateCharacter(kvp.Key, kvp.Value);
                }

                UpdateCharacter(kvp.Key, kvp.Value);
            }

            RemoveMissing(CoreParam.characters, CoreParam.charactersG);
        }

        private void ShowFactories()
        {
            foreach (var kvp in CoreParam.factories)
            {
                if (ShouldHideCornerSpawnMarkerFactory(kvp.Key, kvp.Value))
                {
                    continue;
                }

                if (!CoreParam.factoriesG.TryGetValue(kvp.Key, out GameObject go) || go == null)
                {
                    CoreParam.factoriesG[kvp.Key] = CreateFactory(kvp.Key, kvp.Value);
                    go = CoreParam.factoriesG[kvp.Key];
                }

                UpdateFactory(go, kvp.Key, kvp.Value);
            }

            List<Tuple<int, int>> keysToRemove = new();
            foreach (var kvp in CoreParam.factoriesG)
            {
                bool shouldRemove = !CoreParam.factories.TryGetValue(kvp.Key, out MessageOfFactory msg)
                    || ShouldHideCornerSpawnMarkerFactory(kvp.Key, msg);
                if (!shouldRemove)
                {
                    continue;
                }

                if (kvp.Value != null)
                {
                    Destroy(kvp.Value);
                }

                keysToRemove.Add(kvp.Key);
            }

            foreach (Tuple<int, int> key in keysToRemove)
            {
                CoreParam.factoriesG.Remove(key);
            }
        }

        private GameObject CreateCharacter(long guid, MessageOfCharacter msg)
        {
            if (useCompactRuntimeUnitSprites && TryCreateCompactCharacterObject(guid, msg, out GameObject compactGo))
            {
                return compactGo;
            }

            string prefabKey = GetCharacterPrefabKey(msg);
            GameObject prefab = GetPixelPrefab(prefabKey);
            GameObject go;
            if (useAnimatedUnitPrefabs && prefab != null)
            {
                go = Instantiate(prefab);
                go.name = $"{msg.CharacterType}_{guid}";
                go.transform.localScale = GetCharacterPixelScale(msg.CharacterType);
                RuntimeVisual visual = go.GetComponent<RuntimeVisual>() ?? go.AddComponent<RuntimeVisual>();
                visual.assetKey = prefabKey;
            }
            else
            {
                go = msg.CharacterType switch
                {
                    CharacterType.Drone => GameObject.CreatePrimitive(PrimitiveType.Sphere),
                    CharacterType.Robot => GameObject.CreatePrimitive(PrimitiveType.Cube),
                    CharacterType.AutonomousCar => GameObject.CreatePrimitive(PrimitiveType.Cylinder),
                    _ => GameObject.CreatePrimitive(PrimitiveType.Capsule)
                };

                go.name = $"{msg.CharacterType}_{guid}";
                go.transform.localScale = msg.CharacterType switch
                {
                    CharacterType.Drone => new Vector3(0.8f, 0.8f, 0.8f),
                    CharacterType.Robot => new Vector3(1f, 1f, 1f),
                    CharacterType.AutonomousCar => new Vector3(1.1f, 0.5f, 1.1f),
                    _ => Vector3.one
                };

                SetRendererColor(go, GetTeamColor(msg.TeamId));
            }

            RemoveCollider(go);
            EnsureWorldLabel(go);
            return go;
        }

        private void UpdateCharacter(long guid, MessageOfCharacter msg)
        {
            if (!CoreParam.charactersG.TryGetValue(guid, out GameObject go) || go == null)
            {
                return;
            }

            Vector2 pos = Tool.GameToUnity(msg.X, msg.Y);
            go.transform.position = new Vector3(pos.x, pos.y, -0.5f);
            bool isCompactRuntimeUnit = useCompactRuntimeUnitSprites && IsCompactRuntimeUnit(go);
            RuntimeUnitAction runtimeAction = GetRuntimeUnitAction(guid, msg, pos);
            go.transform.rotation = isCompactRuntimeUnit
                ? Quaternion.identity
                : Quaternion.AngleAxis((float)GetScreenFacingDegrees(msg), Vector3.forward);

            Color baseColor = GetTeamColor(msg.TeamId);
            Color displayColor = msg.CharacterActiveState == CharacterState.Deceased
                ? new Color(baseColor.r * 0.35f, baseColor.g * 0.35f, baseColor.b * 0.35f, 1f)
                : baseColor;
            if (isCompactRuntimeUnit)
            {
                string runtimeDirection = GetRuntimeUnitDirectionKey(guid, msg, pos);
                Sprite sprite = GetRuntimeUnitSprite(msg, runtimeAction, guid, runtimeDirection);
                SpriteRenderer spriteRenderer = go.GetComponent<SpriteRenderer>();
                if (sprite != null && spriteRenderer != null)
                {
                    spriteRenderer.sprite = sprite;
                }
            }

            if (IsPixelVisual(go))
            {
                SetSpriteTint(go, msg.CharacterActiveState == CharacterState.Deceased ? new Color(0.45f, 0.45f, 0.45f, 1f) : Color.white);
            }
            else
            {
                SetRendererColor(go, displayColor);
            }

            WorldObjectInfo info = EnsureWorldObjectInfo(go);
            int baselineMaxHp = GetBaselineCharacterMaxHp(msg.CharacterType);
            info.observedMaxHp = Mathf.Max(Mathf.Max(info.observedMaxHp, msg.Hp), baselineMaxHp);
            int observedMaxHp = Mathf.Max(info.observedMaxHp, 1);
            string characterTitle = $"单位：{TranslateCharacterType(msg.CharacterType)} P{msg.PlayerId}";
            string characterDetail =
                $"状态：{TranslateCharacterState(msg.CharacterActiveState)}\n" +
                $"HP：{msg.Hp}/{observedMaxHp}\n" +
                $"负载：{msg.CurrentLoad}/{Mathf.Max(msg.CarryCapacity, 0)}\n" +
                $"速度：{msg.Speed}  视野：{msg.ViewRange}\n" +
                $"攻击：{msg.CommonAttack}  范围：{msg.CommonAttackRange}\n" +
                $"采集速率：{msg.HarvestRatePerSec}/s";
            info.SetInfo("Character", characterTitle, characterDetail, guid, msg.TeamId, Tool.GameToGrid(msg.X, msg.Y).x, Tool.GameToGrid(msg.X, msg.Y).y);
            info.SetCharacterInfo(msg);
            UpdateStatusBar(go, "HPStatusBar", (float)msg.Hp / observedMaxHp, GetTeamColor(msg.TeamId), new Vector2(0f, 0.54f), new Vector2(0.82f, 0.06f), 44);
            if (msg.CarryCapacity > 0)
            {
                UpdateStatusBar(go, "LoadStatusBar", (float)msg.CurrentLoad / msg.CarryCapacity, new Color(0.18f, 0.88f, 0.96f, 1f), new Vector2(0f, 0.44f), new Vector2(0.70f, 0.045f), 46);
            }

            UpdateWorldLabel(go, $"P{msg.PlayerId}\n{GetCharacterTypeShortName(msg.CharacterType)}\nHP {msg.Hp}");
            UpdateRuntimeAnimationState(guid, msg, pos);
        }

        private bool TryCreateCompactCharacterObject(long guid, MessageOfCharacter msg, out GameObject go)
        {
            go = null;
            Vector2 pos = Tool.GameToUnity(msg.X, msg.Y);
            string direction = GetRuntimeUnitDirectionKey(guid, msg, pos);
            Sprite sprite = GetRuntimeUnitSprite(msg, RuntimeUnitAction.Idle, guid, direction);
            if (sprite == null)
            {
                return false;
            }

            go = new GameObject($"{msg.CharacterType}_{guid}");
            go.transform.localScale = GetCompactUnitScale(msg.CharacterType);
            SpriteRenderer spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingOrder = 30;
            spriteRenderer.color = Color.white;
            RuntimeVisual visual = go.AddComponent<RuntimeVisual>();
            visual.assetKey = GetCompactUnitSpriteKey(msg);
            RemoveCollider(go);
            EnsureWorldLabel(go);
            return true;
        }

        private void ShowSnapshotObjects<TMessage>(
            Dictionary<Tuple<int, int>, TMessage> data,
            Dictionary<Tuple<int, int>, GameObject> objects,
            Func<Tuple<int, int>, TMessage, GameObject> createFunc,
            Action<GameObject, Tuple<int, int>, TMessage> updateFunc)
        {
            foreach (var kvp in data)
            {
                if (!objects.TryGetValue(kvp.Key, out GameObject go) || go == null)
                {
                    objects[kvp.Key] = createFunc(kvp.Key, kvp.Value);
                    go = objects[kvp.Key];
                }

                updateFunc(go, kvp.Key, kvp.Value);
            }

            RemoveMissing(data, objects);
        }

        private void RemoveMissing<TDataKey, TValue>(Dictionary<TDataKey, TValue> data, Dictionary<TDataKey, GameObject> objects)
        {
            List<TDataKey> keysToRemove = new();
            foreach (var kvp in objects)
            {
                if (!data.ContainsKey(kvp.Key))
                {
                    if (kvp.Value != null)
                    {
                        Destroy(kvp.Value);
                    }
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (TDataKey key in keysToRemove)
            {
                objects.Remove(key);
            }
        }

        private void ClearSnapshotObjects<TKey>(Dictionary<TKey, GameObject> objects)
        {
            foreach (GameObject go in objects.Values)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            objects.Clear();
        }

        private static void TrimList<T>(List<T> list, int maxCount)
        {
            while (list.Count > maxCount)
            {
                list.RemoveAt(0);
            }
        }

        private GameObject CreateFactory(Tuple<int, int> pos, MessageOfFactory msg)
        {
            return CreateStaticObject($"Factory_{pos.Item1}_{pos.Item2}", PrimitiveType.Cube, pos, new Vector3(0.95f, 0.95f, 0.95f), GetTeamColor(msg.TeamId), GetFactorySpriteKey(msg));
        }

        private void UpdateFactory(GameObject go, Tuple<int, int> pos, MessageOfFactory msg)
        {
            UpdateStaticObject(go, pos, GetTeamColor(msg.TeamId), new Vector3(0.95f, 0.95f, 0.95f), $"Factory\nHP {msg.Hp}", GetFactorySpriteKey(msg));
            WorldObjectInfo info = EnsureWorldObjectInfo(go);
            int baselineMaxHp = GetBaselineFactoryMaxHp();
            info.observedMaxHp = Mathf.Max(Mathf.Max(info.observedMaxHp, msg.Hp), baselineMaxHp);
            int observedMaxHp = Mathf.Max(info.observedMaxHp, 1);
            info.SetInfo(
                "Factory",
                $"工厂 #{msg.FactoryId}",
                $"HP：{msg.Hp}/{observedMaxHp}\n耐久：{msg.Robust}\n库存容量：{msg.Storage}\n效率：{msg.Efficiency}\n算力：{msg.ComputingPower}\n可生产：{YesNo(msg.CanProduce)}  可招募：{YesNo(msg.CanRecruit)}\n库存：\n{FormatInventory(msg)}",
                msg.FactoryId,
                msg.TeamId,
                pos.Item1,
                pos.Item2);
            UpdateStatusBar(go, "FactoryHPStatusBar", (float)msg.Hp / observedMaxHp, GetTeamColor(msg.TeamId), new Vector2(0f, 0.58f), new Vector2(0.92f, 0.07f), 20);
        }

        private GameObject CreateComputeCenter(Tuple<int, int> pos, MessageOfComputeCenter msg)
        {
            return CreateStaticObject($"ComputeCenter_{pos.Item1}_{pos.Item2}", PrimitiveType.Cube, pos, new Vector3(0.8f, 0.8f, 0.8f), GetComputeCenterColor(msg), GetComputeCenterSpriteKey(msg));
        }

        private void UpdateComputeCenter(GameObject go, Tuple<int, int> pos, MessageOfComputeCenter msg)
        {
            string label = msg.OwnerTeamId > 0 ? $"Compute\nTeam {msg.OwnerTeamId}" : $"Compute\n{msg.OccupyProgress}%";
            UpdateStaticObject(go, pos, GetComputeCenterColor(msg), new Vector3(0.8f, 0.8f, 0.8f), label, GetComputeCenterSpriteKey(msg));
            EnsureWorldObjectInfo(go).SetInfo(
                "ComputeCenter",
                $"算力中心 #{msg.CenterId}",
                $"占领队伍：{(msg.OwnerTeamId > 0 ? $"Team {msg.OwnerTeamId}" : "无")}\n占领进度：{msg.OccupyProgress}%",
                msg.CenterId,
                msg.OwnerTeamId,
                pos.Item1,
                pos.Item2);
            UpdateStatusBar(go, "OccupyStatusBar", msg.OccupyProgress / 100f, GetComputeCenterColor(msg), new Vector2(0f, 0.52f), new Vector2(0.78f, 0.06f), 20);
        }

        private GameObject CreateMarket(Tuple<int, int> pos, MessageOfMarket msg)
        {
            return CreateStaticObject($"Market_{pos.Item1}_{pos.Item2}", PrimitiveType.Cube, pos, new Vector3(0.85f, 0.85f, 0.85f), GetMarketColor(msg.MarketType), GetMarketSpriteKey(msg));
        }

        private void UpdateMarket(GameObject go, Tuple<int, int> pos, MessageOfMarket msg)
        {
            UpdateStaticObject(go, pos, GetMarketColor(msg.MarketType), new Vector3(0.85f, 0.85f, 0.85f), $"Market\n{GetMarketTypeShortName(msg.MarketType)}", GetMarketSpriteKey(msg));
            EnsureWorldObjectInfo(go).SetInfo(
                "Market",
                $"市场 #{msg.MarketId}",
                $"规模：{TranslateMarketType(msg.MarketType)}\n价格：{FormatMarketPrices(msg)}",
                msg.MarketId,
                0,
                pos.Item1,
                pos.Item2);
        }

        private GameObject CreateResource(Tuple<int, int> pos, MessageOfResource msg)
        {
            return CreateStaticObject($"Resource_{pos.Item1}_{pos.Item2}", PrimitiveType.Capsule, pos, GetResourceScale(msg), GetResourceColor(msg.ResourceType), GetResourceSpriteKey(msg));
        }

        private void UpdateResource(GameObject go, Tuple<int, int> pos, MessageOfResource msg)
        {
            UpdateStaticObject(go, pos, GetResourceColor(msg.ResourceType), GetResourceScale(msg), $"Resource\n{CompactAmount(msg.RemainingAmount)}", GetResourceSpriteKey(msg));
            float ratio = msg.MaxAmount > 0 ? (float)msg.RemainingAmount / msg.MaxAmount : 1f;
            EnsureWorldObjectInfo(go).SetInfo(
                "Resource",
                $"资源 #{msg.Id}",
                $"类型：{TranslateResourceType(msg.ResourceType)}\n状态：{TranslateResourceState(msg.ResourceState)}\n剩余：{msg.RemainingAmount}/{Mathf.Max(msg.MaxAmount, 0)}",
                msg.Id,
                0,
                pos.Item1,
                pos.Item2);
            UpdateStatusBar(go, "ResourceStatusBar", ratio, GetResourceColor(msg.ResourceType), new Vector2(0f, 0.50f), new Vector2(0.72f, 0.06f), 20);
        }

        private GameObject CreateBarrier(Tuple<int, int> pos, MessageOfBarrier _)
        {
            GameObject go = CreateStaticObject($"Barrier_{pos.Item1}_{pos.Item2}", PrimitiveType.Cube, pos, new Vector3(1f, 1f, 1f), new Color(0.3f, 0.3f, 0.3f, 1f), GetBarrierSpriteKey(pos));
            go.transform.position = new Vector3(go.transform.position.x, go.transform.position.y, 0.5f);
            UpdateWorldLabel(go, string.Empty);
            return go;
        }

        private void UpdateBarrier(GameObject go, Tuple<int, int> pos, MessageOfBarrier _)
        {
            Vector2 unityPos = Tool.GridToUnity(pos.Item1, pos.Item2);
            go.transform.position = new Vector3(unityPos.x, unityPos.y, 0.5f);
            EnsureWorldObjectInfo(go).SetInfo("Barrier", "障碍物", "来自服务端 Barrier 对象；有地图时使用地图瓦片渲染。", 0, 0, pos.Item1, pos.Item2);
        }

        private GameObject CreateBush(Tuple<int, int> pos, MessageOfBush msg)
        {
            GameObject go = CreateStaticObject($"Bush_{pos.Item1}_{pos.Item2}", PrimitiveType.Sphere, pos, Vector3.one, new Color(0.2f, 0.8f, 0.2f, 0.45f), GetBushSpriteKey(pos));
            UpdateBush(go, pos, msg);
            return go;
        }

        private void UpdateBush(GameObject go, Tuple<int, int> pos, MessageOfBush msg)
        {
            Vector2 unityPos = Tool.GridToUnity(pos.Item1, pos.Item2);
            go.transform.position = new Vector3(unityPos.x, unityPos.y, 0.2f);
            float radius = Mathf.Max(0.5f, msg.Radius / Tool.CellSize);
            go.transform.localScale = new Vector3(radius * 2f, radius * 2f, 0.2f);
            if (!TryApplySprite(go, GetBushSpriteKey(pos)))
            {
                SetRendererColor(go, new Color(0.2f, 0.8f, 0.2f, 0.45f));
            }
            UpdateWorldLabel(go, string.Empty);
            EnsureWorldObjectInfo(go).SetInfo(
                "Bush",
                $"草丛 #{msg.BushId}",
                $"半径：{msg.Radius}",
                msg.BushId,
                0,
                pos.Item1,
                pos.Item2);
        }

        private GameObject CreateStaticObject(string name, PrimitiveType primitiveType, Tuple<int, int> pos, Vector3 scale, Color color, string spriteKey = null)
        {
            Vector2 unityPos = Tool.GridToUnity(pos.Item1, pos.Item2);
            if (TryCreateSpriteObject(name, spriteKey, new Vector3(unityPos.x, unityPos.y, 0f), scale, 0, out GameObject spriteObject))
            {
                EnsureWorldLabel(spriteObject);
                return spriteObject;
            }

            GameObject go = GameObject.CreatePrimitive(primitiveType);
            go.name = name;
            go.transform.position = new Vector3(unityPos.x, unityPos.y, 0f);
            go.transform.localScale = scale;
            RemoveCollider(go);
            SetRendererColor(go, color);
            EnsureWorldLabel(go);
            return go;
        }

        private void UpdateStaticObject(GameObject go, Tuple<int, int> pos, Color color, Vector3 scale, string label, string spriteKey = null)
        {
            Vector2 unityPos = Tool.GridToUnity(pos.Item1, pos.Item2);
            go.transform.position = new Vector3(unityPos.x, unityPos.y, 0f);
            go.transform.localScale = scale;
            if (!TryApplySprite(go, spriteKey))
            {
                SetRendererColor(go, color);
            }
            UpdateWorldLabel(go, showWorldLabels ? label : string.Empty);
        }

        private bool TryCreateSpriteObject(string name, string spriteKey, Vector3 position, Vector3 scale, int sortingOrder, out GameObject go)
        {
            go = null;
            Sprite sprite = GetPixelSprite(spriteKey);
            if (sprite == null)
            {
                return false;
            }

            go = new GameObject(name);
            go.transform.position = position;
            go.transform.localScale = scale;
            SpriteRenderer spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingOrder = sortingOrder;
            spriteRenderer.color = Color.white;
            RuntimeVisual visual = go.AddComponent<RuntimeVisual>();
            visual.assetKey = spriteKey;
            return true;
        }

        private bool TryApplySprite(GameObject go, string spriteKey)
        {
            if (go == null)
            {
                return false;
            }

            Sprite sprite = GetPixelSprite(spriteKey);
            SpriteRenderer spriteRenderer = go.GetComponent<SpriteRenderer>();
            if (sprite == null || spriteRenderer == null)
            {
                return false;
            }

            spriteRenderer.sprite = sprite;
            spriteRenderer.color = Color.white;
            RuntimeVisual visual = go.GetComponent<RuntimeVisual>() ?? go.AddComponent<RuntimeVisual>();
            visual.assetKey = spriteKey;
            return true;
        }

        private static WorldObjectInfo EnsureWorldObjectInfo(GameObject go)
        {
            return go.GetComponent<WorldObjectInfo>() ?? go.AddComponent<WorldObjectInfo>();
        }

        private static void UpdateStatusBar(GameObject go, string name, float ratio, Color color, Vector2 offset, Vector2 size, int sortingOrder)
        {
            Transform existing = go.transform.Find(name);
            WorldStatusBar bar;
            if (existing == null)
            {
                GameObject barGo = new GameObject(name);
                barGo.transform.SetParent(go.transform, false);
                bar = barGo.AddComponent<WorldStatusBar>();
            }
            else
            {
                bar = existing.GetComponent<WorldStatusBar>() ?? existing.gameObject.AddComponent<WorldStatusBar>();
            }

            bar.Configure(color, size, offset, sortingOrder);
            bar.SetRatio(ratio);
        }

        private Sprite GetPixelSprite(string key)
        {
            return usePixelAssets && pixelAssets != null ? pixelAssets.GetSprite(key) : null;
        }

        private GameObject GetPixelPrefab(string key)
        {
            return usePixelAssets && pixelAssets != null ? pixelAssets.GetPrefab(key) : null;
        }

        private static bool IsPixelVisual(GameObject go)
        {
            return go != null && go.GetComponent<RuntimeVisual>() != null;
        }

        private static void SetSpriteTint(GameObject go, Color color)
        {
            foreach (SpriteRenderer spriteRenderer in go.GetComponentsInChildren<SpriteRenderer>())
            {
                spriteRenderer.color = color;
            }
        }

        private void SetRendererColor(GameObject go, Color color)
        {
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            if (renderer.material == null)
            {
                renderer.material = new Material(Shader.Find("Standard"));
            }
            renderer.material.color = color;
        }

        private void RemoveCollider(GameObject go)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private void EnsureWorldLabel(GameObject go)
        {
            if (!showWorldLabels)
            {
                Transform existingLabel = go.transform.Find("WorldLabel");
                if (existingLabel != null)
                {
                    existingLabel.gameObject.SetActive(false);
                }
                return;
            }

            if (go.transform.Find("WorldLabel") != null)
            {
                return;
            }

            GameObject labelGo = new GameObject("WorldLabel");
            labelGo.transform.SetParent(go.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.55f, -0.2f);
            TextMesh textMesh = labelGo.AddComponent<TextMesh>();
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = 0.08f;
            textMesh.fontSize = 48;
            textMesh.color = Color.black;
            textMesh.text = string.Empty;
        }

        private void UpdateWorldLabel(GameObject go, string text)
        {
            Transform label = go.transform.Find("WorldLabel");
            if (label == null)
            {
                return;
            }

            TextMesh mesh = label.GetComponent<TextMesh>();
            if (mesh != null)
            {
                if (!showWorldLabels)
                {
                    mesh.text = string.Empty;
                    mesh.gameObject.SetActive(false);
                    return;
                }

                mesh.text = text;
                mesh.gameObject.SetActive(!string.IsNullOrEmpty(text));
            }
        }

        private static string GetMapTileSpriteKey(PlaceType placeType, int row, int col)
        {
            int variant4 = DeterministicVariant(row, col, 4);
            int variant6 = DeterministicVariant(row, col, 6);
            return placeType switch
            {
                PlaceType.Factory => $"tile_factory_zone_{variant4:00}",
                PlaceType.Barrier => GetBarrierTileSpriteKey(row, col),
                PlaceType.Bush => $"tile_bush_signal_{variant6:00}",
                PlaceType.Resource => $"tile_mining_zone_{variant4:00}",
                PlaceType.ComputeCenter => $"tile_compute_zone_{variant4:00}",
                PlaceType.Market => $"tile_market_zone_{variant4:00}",
                _ => GetSpaceTileSpriteKey(row, col)
            };
        }

        private static string GetSpaceTileSpriteKey(int row, int col)
        {
            if (IsLogisticsRoadCell(row, col))
            {
                return $"tile_logistics_road_{DeterministicVariant(row, col, 8):00}";
            }

            return $"tile_ground_industrial_{DeterministicVariant(row, col, 8):00}";
        }

        private static bool IsLogisticsRoadCell(int row, int col)
        {
            // Keep logistics lanes sparse: they should hint at transport flow,
            // not repaint the whole board into a noisy road grid.
            return row == 24 || col == 24;
        }

        private static string GetBarrierTileSpriteKey(int row, int col)
        {
            return $"tile_barrier_connected_{GetMapBarrierNeighborMask(row, col):00}";
        }

        private static int GetMapBarrierNeighborMask(int row, int col)
        {
            int mask = 0;
            if (IsMapBarrierAt(row - 1, col)) mask |= 1;
            if (IsMapBarrierAt(row, col + 1)) mask |= 2;
            if (IsMapBarrierAt(row + 1, col)) mask |= 4;
            if (IsMapBarrierAt(row, col - 1)) mask |= 8;
            return mask;
        }

        private static bool IsMapBarrierAt(int row, int col)
        {
            if (CoreParam.map == null || row < 0 || col < 0 || row >= CoreParam.map.Rows.Count)
            {
                return false;
            }

            if (col >= CoreParam.map.Rows[row].Cols.Count)
            {
                return false;
            }

            return CoreParam.map.Rows[row].Cols[col] == PlaceType.Barrier;
        }

        private static string GetFactorySpriteKey(MessageOfFactory msg)
        {
            if (msg.Hp <= 0)
            {
                return "building_factory_destroyed";
            }

            int team = ClampTeamId(msg.TeamId);
            return team > 0 ? $"building_factory_team_{team}" : "building_factory_neutral";
        }

        private static bool ShouldHideCornerSpawnMarkerFactory(Tuple<int, int> pos, MessageOfFactory msg)
        {
            // Official replays currently include four team-owned factory records at
            // the absolute map corners, while the visible/usable factory spawn
            // zones are already represented by the in-board factory cells (for
            // example row/col 3 and 46 on a 50x50 map). Rendering both produces
            // duplicate-looking factories in the extreme corners, so hide only
            // the team-owned corner markers and keep all neutral/in-board factories.
            if (ClampTeamId(msg.TeamId) == 0)
            {
                return false;
            }

            int lastRow = Mathf.Max(Tool.GetMapRows() - 1, 0);
            int lastCol = Mathf.Max(Tool.GetMapCols() - 1, 0);
            bool onExtremeRow = pos.Item1 <= 0 || pos.Item1 >= lastRow;
            bool onExtremeCol = pos.Item2 <= 0 || pos.Item2 >= lastCol;
            return onExtremeRow && onExtremeCol;
        }

        private static string GetComputeCenterSpriteKey(MessageOfComputeCenter msg)
        {
            int team = ClampTeamId(msg.OwnerTeamId);
            return team > 0 ? $"building_compute_center_team_{team}" : "building_compute_center_neutral";
        }

        private static string GetMarketSpriteKey(MessageOfMarket msg)
        {
            return msg.MarketType == MarketType.LargeMarket ? "building_market_high" : "building_market_low";
        }

        private static string GetResourceSpriteKey(MessageOfResource msg)
        {
            if (msg.RemainingAmount <= 0)
            {
                return "building_resource_depleted";
            }

            float fillRatio = msg.MaxAmount > 0 ? Mathf.Clamp01((float)msg.RemainingAmount / msg.MaxAmount) : 1f;
            string suffix = fillRatio < 0.5f ? "_half" : string.Empty;
            return msg.ResourceType switch
            {
                ResourceType.SmallResource => $"building_resource_small{suffix}",
                ResourceType.MediumResource => $"building_resource_medium{suffix}",
                ResourceType.LargeResource => $"building_resource_large{suffix}",
                _ => $"building_resource_medium{suffix}"
            };
        }

        private static string GetBarrierSpriteKey(Tuple<int, int> pos)
        {
            return $"tile_barrier_connected_{GetRuntimeBarrierNeighborMask(pos):00}";
        }

        private static int GetRuntimeBarrierNeighborMask(Tuple<int, int> pos)
        {
            int row = pos.Item1;
            int col = pos.Item2;
            int mask = 0;
            if (CoreParam.barriers.ContainsKey(new Tuple<int, int>(row - 1, col))) mask |= 1;
            if (CoreParam.barriers.ContainsKey(new Tuple<int, int>(row, col + 1))) mask |= 2;
            if (CoreParam.barriers.ContainsKey(new Tuple<int, int>(row + 1, col))) mask |= 4;
            if (CoreParam.barriers.ContainsKey(new Tuple<int, int>(row, col - 1))) mask |= 8;
            return mask;
        }

        private static string GetBushSpriteKey(Tuple<int, int> pos)
        {
            return $"tile_bush_signal_{DeterministicVariant(pos.Item1, pos.Item2, 6):00}";
        }

        private static string GetCharacterPrefabKey(MessageOfCharacter msg)
        {
            int team = ClampTeamId(msg.TeamId);
            if (team == 0)
            {
                team = 1;
            }

            string unit = msg.CharacterType switch
            {
                CharacterType.Drone => "drone",
                CharacterType.Robot => "robot",
                CharacterType.AutonomousCar => "autocar",
                _ => "robot"
            };

            string state = msg.CharacterActiveState == CharacterState.Deceased
                ? "death"
                : msg.CharacterType == CharacterType.Drone ? "hover" : "idle";
            return $"unit_{unit}_team_{team}_{state}";
        }

        private static Sprite GetCompactUnitSprite(MessageOfCharacter msg)
        {
            return Resources.Load<Sprite>($"Runtime/Units/{GetCompactUnitSpriteKey(msg)}");
        }

        private static Sprite GetRuntimeUnitSprite(MessageOfCharacter msg, RuntimeUnitAction action, long guid, string directionOverride = null)
        {
            string unit = GetRuntimeUnitName(msg.CharacterType);
            int team = ClampTeamId(msg.TeamId);
            if (team == 0)
            {
                team = 1;
            }

            string direction = string.IsNullOrEmpty(directionOverride) ? GetFacingDirectionKey(msg) : directionOverride;
            string actionName = action.ToString().ToLowerInvariant();
            int frame = GetRuntimeUnitFrameIndex(action, guid);
            string animatedKey = $"unit_{unit}_team_{team}_{actionName}_{direction}_{frame:00}";
            Sprite animated = Resources.Load<Sprite>($"Runtime/UnitAnimations/{animatedKey}");
            if (animated != null)
            {
                return animated;
            }

            string idleKey = $"unit_{unit}_team_{team}_idle_{direction}_00";
            Sprite idle = Resources.Load<Sprite>($"Runtime/UnitAnimations/{idleKey}");
            return idle != null ? idle : GetCompactUnitSprite(msg);
        }

        private static string GetCompactUnitSpriteKey(MessageOfCharacter msg)
        {
            int team = ClampTeamId(msg.TeamId);
            if (team == 0)
            {
                team = 1;
            }

            string unit = GetRuntimeUnitName(msg.CharacterType);

            return $"unit_{unit}_team_{team}_compact";
        }

        private static string GetRuntimeUnitName(CharacterType type)
        {
            return type switch
            {
                CharacterType.Drone => "drone",
                CharacterType.Robot => "robot",
                CharacterType.AutonomousCar => "autocar",
                _ => "robot"
            };
        }

        private static string GetFacingDirectionKey(MessageOfCharacter msg)
        {
            double degrees = GetScreenFacingDegrees(msg);
            degrees %= 360.0;
            if (degrees < 0)
            {
                degrees += 360.0;
            }

            if (degrees >= 45.0 && degrees < 135.0)
            {
                return "n";
            }

            if (degrees >= 135.0 && degrees < 225.0)
            {
                return "w";
            }

            if (degrees >= 225.0 && degrees < 315.0)
            {
                return "s";
            }

            return "e";
        }

        private static double GetScreenFacingDegrees(MessageOfCharacter msg)
        {
            return msg.FacingDirection * Mathf.Rad2Deg - 90.0;
        }

        private string GetRuntimeUnitDirectionKey(long guid, MessageOfCharacter msg, Vector2 currentPosition)
        {
            if (_previousCharacterPositions.TryGetValue(guid, out Vector2 previousPosition))
            {
                Vector2 delta = currentPosition - previousPosition;
                if (delta.sqrMagnitude > 0.0001f)
                {
                    string movementDirection = GetScreenDirectionKey(delta);
                    return NormalizeRuntimeDirectionKey(guid, msg, movementDirection);
                }
            }

            return NormalizeRuntimeDirectionKey(guid, msg, GetFacingDirectionKey(msg));
        }

        private static string GetScreenDirectionKey(Vector2 delta)
        {
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            {
                return delta.x >= 0f ? "e" : "w";
            }

            return delta.y >= 0f ? "n" : "s";
        }

        private string NormalizeRuntimeDirectionKey(long guid, MessageOfCharacter msg, string direction)
        {
            if (msg.CharacterType != CharacterType.AutonomousCar)
            {
                _lastRuntimeUnitDirections[guid] = direction;
                return direction;
            }

            if (direction == "e" || direction == "w")
            {
                _lastRuntimeUnitDirections[guid] = direction;
                return direction;
            }

            if (_lastRuntimeUnitDirections.TryGetValue(guid, out string lastDirection) &&
                (lastDirection == "e" || lastDirection == "w"))
            {
                return lastDirection;
            }

            string fallbackDirection = GetHorizontalFacingDirectionKey(msg);
            _lastRuntimeUnitDirections[guid] = fallbackDirection;
            return fallbackDirection;
        }

        private static string GetHorizontalFacingDirectionKey(MessageOfCharacter msg)
        {
            double degrees = GetScreenFacingDegrees(msg);
            degrees %= 360.0;
            if (degrees < 0)
            {
                degrees += 360.0;
            }

            return degrees > 90.0 && degrees < 270.0 ? "w" : "e";
        }

        private static int GetRuntimeUnitFrameIndex(RuntimeUnitAction action, long guid)
        {
            int frameCount = action == RuntimeUnitAction.Idle ? 2 : 4;
            int offset = Mathf.Abs((int)(guid % frameCount));
            return Mathf.Abs(CoreParam.frameCount + offset) % frameCount;
        }

        private RuntimeUnitAction GetRuntimeUnitAction(long guid, MessageOfCharacter msg, Vector2 currentPosition)
        {
            bool hasPreviousPosition = _previousCharacterPositions.TryGetValue(guid, out Vector2 previousPosition);
            bool isMoving = hasPreviousPosition && (currentPosition - previousPosition).sqrMagnitude > 0.0001f;
            bool isHarvesting = _previousCharacterLoads.TryGetValue(guid, out int previousLoad) && msg.CurrentLoad > previousLoad;
            bool isAttacking = _previousCharacterAttackCooldowns.TryGetValue(guid, out long previousCooldown)
                && msg.CommonAttackCd > previousCooldown;

            if (isAttacking)
            {
                return RuntimeUnitAction.Attack;
            }

            if (isHarvesting)
            {
                return RuntimeUnitAction.Harvest;
            }

            return isMoving ? RuntimeUnitAction.Move : RuntimeUnitAction.Idle;
        }

        private void UpdateRuntimeAnimationState(long guid, MessageOfCharacter msg, Vector2 currentPosition)
        {
            _previousCharacterPositions[guid] = currentPosition;
            _previousCharacterLoads[guid] = msg.CurrentLoad;
            _previousCharacterAttackCooldowns[guid] = msg.CommonAttackCd;
        }

        private void ResetRuntimeAnimationState()
        {
            _previousCharacterPositions.Clear();
            _previousCharacterLoads.Clear();
            _previousCharacterAttackCooldowns.Clear();
            _lastRuntimeUnitDirections.Clear();
        }

        private static Vector3 GetCharacterPixelScale(CharacterType type)
        {
            return type switch
            {
                CharacterType.Drone => new Vector3(0.62f, 0.62f, 0.62f),
                CharacterType.Robot => new Vector3(0.58f, 0.58f, 0.58f),
                CharacterType.AutonomousCar => new Vector3(0.62f, 0.62f, 0.62f),
                _ => new Vector3(0.6f, 0.6f, 0.6f)
            };
        }

        private static Vector3 GetCompactUnitScale(CharacterType type)
        {
            return type switch
            {
                CharacterType.Drone => new Vector3(1.36f, 1.36f, 1.36f),
                CharacterType.Robot => new Vector3(1.30f, 1.30f, 1.30f),
                CharacterType.AutonomousCar => new Vector3(1.28f, 1.28f, 1.28f),
                _ => new Vector3(1.30f, 1.30f, 1.30f)
            };
        }

        private static string FormatPlaybackTime(int totalMilliseconds)
        {
            totalMilliseconds = CoreParam.ClampDisplayGameMilliseconds(totalMilliseconds);
            int minutes = totalMilliseconds / 60000;
            int seconds = totalMilliseconds / 1000 % 60;
            return $"{minutes:D2}:{seconds:D2}";
        }

        private static bool IsCompactRuntimeUnit(GameObject go)
        {
            RuntimeVisual visual = go != null ? go.GetComponent<RuntimeVisual>() : null;
            return visual != null && visual.assetKey != null && visual.assetKey.EndsWith("_compact", StringComparison.Ordinal);
        }

        private static int DeterministicVariant(int a, int b, int count)
        {
            return Mathf.Abs(a * 73856093 ^ b * 19349663) % Mathf.Max(count, 1) + 1;
        }

        private static int ClampTeamId(long teamId)
        {
            return teamId >= 1 && teamId <= 4 ? (int)teamId : 0;
        }

        private static Color GetTeamColor(long teamId)
        {
            return teamId switch
            {
                1 => new Color(1f, 0.27f, 0.27f, 1f),
                2 => new Color(0.27f, 0.67f, 1f, 1f),
                3 => new Color(0.27f, 0.85f, 0.35f, 1f),
                4 => new Color(1f, 0.72f, 0.20f, 1f),
                _ => Color.white
            };
        }

        private static Color GetComputeCenterColor(MessageOfComputeCenter msg)
        {
            return msg.OwnerTeamId > 0 ? GetTeamColor(msg.OwnerTeamId) : new Color(0.72f, 0.47f, 0.90f, 1f);
        }

        private static Color GetMarketColor(MarketType type)
        {
            return type switch
            {
                MarketType.SmallMarket => new Color(1f, 0.82f, 0.35f, 1f),
                MarketType.MediumMarket => new Color(1f, 0.62f, 0.22f, 1f),
                MarketType.LargeMarket => new Color(0.96f, 0.42f, 0.12f, 1f),
                _ => new Color(0.98f, 0.71f, 0.27f, 1f)
            };
        }

        private static Color GetResourceColor(ResourceType type)
        {
            return type switch
            {
                ResourceType.SmallResource => new Color(0.45f, 0.80f, 1f, 1f),
                ResourceType.MediumResource => new Color(0.20f, 0.60f, 0.95f, 1f),
                ResourceType.LargeResource => new Color(0.08f, 0.38f, 0.80f, 1f),
                _ => new Color(0.32f, 0.66f, 0.92f, 1f)
            };
        }

        private static Vector3 GetResourceScale(MessageOfResource msg)
        {
            float fillRatio = msg.MaxAmount > 0 ? Mathf.Clamp01((float)msg.RemainingAmount / msg.MaxAmount) : 1f;
            float baseScale = msg.ResourceType switch
            {
                ResourceType.SmallResource => 0.5f,
                ResourceType.MediumResource => 0.75f,
                ResourceType.LargeResource => 1.0f,
                _ => 0.75f
            };
            float scale = Mathf.Max(baseScale * (0.65f + fillRatio * 0.35f), 0.35f);
            return new Vector3(scale, scale, scale);
        }

        private static int GetBaselineCharacterMaxHp(CharacterType type)
        {
            return type switch
            {
                CharacterType.Drone => 100,
                CharacterType.Robot => 150,
                CharacterType.AutonomousCar => 100,
                _ => 1
            };
        }

        private static int GetBaselineFactoryMaxHp()
        {
            return 100;
        }

        private static string GetCharacterTypeShortName(CharacterType type)
        {
            return type switch
            {
                CharacterType.Drone => "Drone",
                CharacterType.Robot => "Robot",
                CharacterType.AutonomousCar => "AutoCar",
                _ => "Unit"
            };
        }

        private static string TranslateCharacterType(CharacterType type)
        {
            return type switch
            {
                CharacterType.Drone => "无人机",
                CharacterType.Robot => "机器人",
                CharacterType.AutonomousCar => "无人车",
                _ => "未知单位"
            };
        }

        private static string TranslateCharacterState(CharacterState state)
        {
            return state switch
            {
                CharacterState.Idle => "待机",
                CharacterState.Harvesting => "采集",
                CharacterState.Attacking => "攻击",
                CharacterState.Ocuppying => "占领",
                CharacterState.Trading => "交易",
                CharacterState.Moving => "移动",
                CharacterState.KnockedBack => "击退",
                CharacterState.Deceased => "阵亡",
                _ => "无"
            };
        }

        private static string GetMarketTypeShortName(MarketType type)
        {
            return type switch
            {
                MarketType.SmallMarket => "Small",
                MarketType.MediumMarket => "Medium",
                MarketType.LargeMarket => "Large",
                _ => "Unknown"
            };
        }

        private static string TranslateMarketType(MarketType type)
        {
            return type switch
            {
                MarketType.SmallMarket => "小型市场",
                MarketType.MediumMarket => "中型市场",
                MarketType.LargeMarket => "大型市场",
                _ => "未知市场"
            };
        }

        private static string TranslateResourceType(ResourceType type)
        {
            return type switch
            {
                ResourceType.SmallResource => "小型资源",
                ResourceType.MediumResource => "中型资源",
                ResourceType.LargeResource => "大型资源",
                _ => "未知资源"
            };
        }

        private static string TranslateResourceState(ResourceState state)
        {
            return state switch
            {
                ResourceState.Harvestable => "可采集",
                ResourceState.BeingHarvested => "采集中",
                ResourceState.Harvested => "已采完",
                _ => "未知"
            };
        }

        private static string TranslateGoodsType(GoodsType type)
        {
            return type switch
            {
                GoodsType.Semiconductor => "半导体",
                GoodsType.Medicine => "药品",
                GoodsType.Toys => "玩具",
                GoodsType.Clothes => "服装",
                GoodsType.Food => "食品",
                _ => "未知货物"
            };
        }

        private static string FormatInventory(MessageOfFactory msg)
        {
            if (msg.ProductInventory == null || msg.ProductInventory.Count == 0)
            {
                return "空";
            }

            List<MessageOfFactory.Types.GoodsStack> products = new List<MessageOfFactory.Types.GoodsStack>(msg.ProductInventory);
            products.Sort((left, right) => ((int)left.ProductType).CompareTo((int)right.ProductType));

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < products.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                builder.Append("· ");
                builder.Append(TranslateGoodsType(products[i].ProductType));
                builder.Append("：");
                builder.Append(products[i].Quantity);
            }

            return builder.ToString();
        }

        private static string FormatMarketPrices(MessageOfMarket msg)
        {
            if (msg.PriceList == null || msg.PriceList.Count == 0)
            {
                return "暂无";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < msg.PriceList.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                var entry = msg.PriceList[i];
                builder.Append(TranslateGoodsType(entry.GoodsType));
                builder.Append("：");
                builder.Append(entry.Price);
                builder.Append("（成交 ");
                builder.Append(entry.TradedQuantity);
                builder.Append("）");
            }

            return builder.ToString();
        }

        private static string YesNo(bool value)
        {
            return value ? "是" : "否";
        }

        private static string CompactAmount(int value)
        {
            if (value >= 10000)
            {
                return $"{value / 10000f:0.#}w";
            }
            if (value >= 1000)
            {
                return $"{value / 1000f:0.0}k";
            }
            return value.ToString();
        }

        private static Text FindTextByName(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            return go != null ? go.GetComponent<Text>() : null;
        }

        private void OnDestroy()
        {
            FrameSourceHub.ImmediateFrameSubmitted -= OnImmediateFrameSubmitted;
            FrameSourceHub.PumpRequested -= OnFramePumpRequested;
            ReleaseSingletonInstance();

            if (_updateCoroutine != null)
            {
                StopCoroutine(_updateCoroutine);
                _updateCoroutine = null;
            }
        }
    }
}

