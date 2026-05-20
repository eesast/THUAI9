using System.Collections;
using THUAI9.Unity.Core;
using UnityEngine;

namespace THUAI9.Unity.Generated
{
    /// <summary>
    /// Shows the real THUAI9 default first-frame map in a mode scene before a real frame arrives.
    /// Source of truth: logic/Server/GameServer.cs -> MapInfo.defaultMapStruct -> MapMsg().
    /// This is visual-only and stays independent from Live / Playback / Trial controllers.
    /// </summary>
    public sealed class PixelDemoBootstrap : MonoBehaviour
    {
        private const int PlaceFactory = 1;
        private const int PlaceSpace = 2;
        private const int PlaceBarrier = 3;
        private const int PlaceBush = 4;
        private const int PlaceResource = 5;
        private const int PlaceComputeCenter = 6;
        private const int PlaceMarket = 7;

        private static readonly int[,] DefaultFirstFrameMap = new int[50, 50]
        {
            { 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3 },
            { 3, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 3, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 2, 3, 3, 2, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 1, 2, 4, 2, 2, 2, 3, 3, 3, 3, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 3, 2, 2, 2, 1, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 4, 2, 3, 4, 4, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 5, 2, 2, 2, 6, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 3, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 2, 2, 3 },
            { 3, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 6, 3, 2, 4, 2, 2, 2, 3, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 2, 2, 3 },
            { 3, 4, 4, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 3, 2, 4, 2, 2, 2, 2, 3, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 5, 2, 4, 4, 2, 2, 2, 2, 3 },
            { 3, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 7, 2, 2, 2, 2, 2, 2, 5, 2, 4, 4, 2, 2, 2, 2, 2, 2, 5, 3, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 4, 4, 4, 2, 2, 6, 2, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 4, 4, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 4, 4, 4, 4, 4, 2, 2, 3, 3, 3, 3, 3, 3, 2, 2, 2, 2, 3, 2, 2, 4, 4, 3, 3, 2, 2, 2, 3, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 2, 3 },
            { 3, 2, 2, 4, 4, 4, 2, 4, 4, 2, 2, 2, 2, 3, 4, 3, 2, 2, 2, 2, 2, 3, 3, 2, 3, 2, 3, 2, 2, 2, 2, 3, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 3, 2, 2, 3, 2, 2, 3 },
            { 3, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 3, 2, 3, 2, 2, 3, 4, 3, 3, 3, 2, 3, 3, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 },
            { 3, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 2, 2, 2, 3, 2, 2, 3, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 },
            { 3, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 2, 2, 2, 2, 3, 2, 2, 2, 2, 3 },
            { 3, 2, 4, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 4, 4, 4, 2, 2, 2, 2, 2, 2, 4, 4, 7, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 3, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 2, 2, 4, 4, 4, 2, 2, 4, 2, 2, 2, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 5, 3, 3, 3, 2, 2, 3 },
            { 3, 2, 2, 7, 2, 2, 3, 2, 2, 2, 2, 6, 2, 2, 3, 3, 2, 2, 2, 2, 5, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 2, 2, 2, 2, 4, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 2, 2, 2, 2, 4, 4, 2, 4, 2, 2, 3, 2, 2, 2, 2, 3 },
            { 3, 2, 7, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 4, 2, 4, 4, 2, 2, 2, 4, 4, 2, 4, 2, 2, 2, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 2, 3, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 4, 2, 2, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 3, 3, 3, 3, 2, 2, 2, 4, 2, 5, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 4, 4, 2, 7, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 4, 4, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 4, 4, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 7, 2, 4, 4, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 5, 2, 4, 2, 2, 2, 3, 3, 3, 3, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 2, 2, 4, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 3, 2, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 2, 2, 2, 4, 2, 4, 4, 2, 2, 2, 4, 4, 2, 4, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 7, 2, 3 },
            { 3, 2, 2, 2, 2, 3, 2, 2, 4, 2, 4, 4, 2, 2, 2, 2, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 4, 2, 2, 2, 2, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 5, 2, 2, 2, 2, 3, 3, 2, 2, 6, 2, 2, 2, 2, 3, 2, 2, 7, 2, 2, 3 },
            { 3, 2, 2, 3, 3, 3, 5, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 2, 2, 2, 4, 2, 2, 4, 4, 4, 2, 2, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 3, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 7, 4, 4, 2, 2, 2, 2, 2, 2, 4, 4, 4, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 4, 2, 3 },
            { 3, 2, 2, 2, 2, 3, 2, 2, 2, 2, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 3 },
            { 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 2, 2, 3, 2, 2, 2, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 3 },
            { 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 3, 3, 2, 3, 3, 3, 4, 3, 2, 2, 3, 2, 3, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 3 },
            { 3, 2, 2, 3, 2, 2, 3, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 3, 2, 2, 2, 2, 3, 2, 3, 2, 3, 3, 2, 2, 2, 2, 2, 3, 4, 3, 2, 2, 2, 2, 4, 4, 2, 4, 4, 4, 2, 2, 3 },
            { 3, 2, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 3, 2, 2, 2, 3, 3, 4, 4, 2, 2, 3, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 2, 2, 4, 4, 4, 4, 4, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 4, 4, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 2, 6, 2, 2, 4, 4, 4, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 3, 5, 2, 2, 2, 2, 2, 2, 4, 4, 2, 5, 2, 2, 2, 2, 2, 2, 7, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 3 },
            { 3, 2, 2, 2, 2, 4, 4, 2, 5, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 3, 2, 2, 2, 2, 4, 2, 3, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 4, 4, 3 },
            { 3, 2, 2, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 2, 2, 2, 4, 2, 3, 6, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 3 },
            { 3, 2, 2, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 3, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 6, 2, 2, 2, 5, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 4, 4, 3, 2, 4, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 1, 2, 2, 2, 3, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 3, 3, 3, 3, 2, 2, 2, 4, 2, 1, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 2, 3, 3, 2, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 3, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 },
            { 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 3 },
            { 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3 }
        };

        public PixelAssetRegistry pixelAssets;
        public bool showWhenFrameSourceMissing = false;
        public int columns = 50;
        public int rows = 50;

        private GameObject _previewRoot;

        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            if (!showWhenFrameSourceMissing || pixelAssets == null)
            {
                yield break;
            }

            if (FrameSourceHub.ActiveKind != FrameSourceHub.SourceKind.None || FrameSourceHub.SubmittedFrameCount > 0)
            {
                yield break;
            }

            BuildPreview();
        }

        private void BuildPreview()
        {
            if (_previewRoot != null)
            {
                Destroy(_previewRoot);
            }

            rows = DefaultFirstFrameMap.GetLength(0);
            columns = DefaultFirstFrameMap.GetLength(1);
            _previewRoot = new GameObject($"DefaultFirstFramePreview_{columns}x{rows}");

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    int place = DefaultFirstFrameMap[row, col];
                    Vector3 tilePosition = GridToUnity(row, col, 1.5f);
                    CreateSprite($"Tile_{row}_{col}_{place}", GetTileKey(place, row, col), tilePosition, Vector3.one, -100);
                }
            }

            int factoryIndex = 0;
            int resourceIndex = 0;
            int computeIndex = 0;
            int marketIndex = 0;

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    int place = DefaultFirstFrameMap[row, col];
                    switch (place)
                    {
                        case PlaceFactory:
                            factoryIndex++;
                            int team = FactoryTeamByQuadrant(row, col);
                            CreateSprite($"Factory_Team{team}_{row}_{col}", $"building_factory_team_{team}", GridToUnity(row, col, 0f), Vector3.one * 0.58f, 0);
                            break;
                        case PlaceResource:
                            CreateSprite($"Resource_{resourceIndex}_{row}_{col}", ResourceSpriteKey(resourceIndex), GridToUnity(row, col, -0.05f), Vector3.one * 0.42f, 1);
                            resourceIndex++;
                            break;
                        case PlaceComputeCenter:
                            CreateSprite($"ComputeCenter_{computeIndex}_{row}_{col}", "building_compute_center_neutral", GridToUnity(row, col, -0.08f), Vector3.one * 0.44f, 2);
                            computeIndex++;
                            break;
                        case PlaceMarket:
                            CreateSprite($"Market_{marketIndex}_{row}_{col}", marketIndex % 3 == 0 ? "building_market_high" : "building_market_low", GridToUnity(row, col, -0.06f), Vector3.one * 0.44f, 1);
                            marketIndex++;
                            break;
                    }
                }
            }

            // Tiny base beacons keep the four-corner ownership readable without inventing extra unit positions.
            CreateTeamBeacon("Team1_BaseBeacon", 1, 3, 3);
            CreateTeamBeacon("Team2_BaseBeacon", 2, 3, 46);
            CreateTeamBeacon("Team3_BaseBeacon", 3, 46, 3);
            CreateTeamBeacon("Team4_BaseBeacon", 4, 46, 46);

            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.orthographic = true;
                camera.transform.position = new Vector3(columns / 2f, rows / 2f, -10f);
                camera.orthographicSize = Mathf.Max(rows * 0.55f, columns / Mathf.Max(camera.aspect, 0.01f) * 0.55f);
                camera.backgroundColor = new Color(0.015f, 0.020f, 0.032f, 1f);
            }
        }

        private void CreateTeamBeacon(string objectName, int team, int row, int col)
        {
            string key = team switch
            {
                1 => "ui_team_badge_1",
                2 => "ui_team_badge_2",
                3 => "ui_team_badge_3",
                4 => "ui_team_badge_4",
                _ => "ui_team_badge_1"
            };

            CreateSprite(objectName, key, GridToUnity(row, col, -0.2f) + new Vector3(0.34f, 0.34f, 0f), Vector3.one * 0.20f, 10);
        }

        private Vector3 GridToUnity(int row, int col, float z)
        {
            return new Vector3(col + 0.5f, rows - row - 0.5f, z);
        }

        private string GetTileKey(int place, int row, int col)
        {
            return place switch
            {
                PlaceFactory => $"tile_factory_zone_{Variant(row, col, 4):00}",
                PlaceBarrier => GetBarrierTileKey(row, col),
                PlaceBush => $"tile_bush_signal_{Variant(row, col, 6):00}",
                PlaceResource => $"tile_mining_zone_{Variant(row, col, 4):00}",
                PlaceComputeCenter => $"tile_compute_zone_{Variant(row, col, 4):00}",
                PlaceMarket => $"tile_market_zone_{Variant(row, col, 4):00}",
                PlaceSpace => GetSpaceTileKey(row, col),
                _ => GetSpaceTileKey(row, col)
            };
        }

        private static string GetSpaceTileKey(int row, int col)
        {
            if (IsLogisticsRoadCell(row, col))
            {
                return $"tile_logistics_road_{Variant(row, col, 8):00}";
            }

            return $"tile_ground_industrial_{Variant(row, col, 8):00}";
        }

        private static bool IsLogisticsRoadCell(int row, int col)
        {
            return row == 24 || col == 24;
        }

        private static string GetBarrierTileKey(int row, int col)
        {
            return $"tile_barrier_connected_{GetBarrierNeighborMask(row, col):00}";
        }

        private static int GetBarrierNeighborMask(int row, int col)
        {
            int mask = 0;
            if (IsDefaultBarrierAt(row - 1, col)) mask |= 1;
            if (IsDefaultBarrierAt(row, col + 1)) mask |= 2;
            if (IsDefaultBarrierAt(row + 1, col)) mask |= 4;
            if (IsDefaultBarrierAt(row, col - 1)) mask |= 8;
            return mask;
        }

        private static bool IsDefaultBarrierAt(int row, int col)
        {
            return row >= 0
                && col >= 0
                && row < DefaultFirstFrameMap.GetLength(0)
                && col < DefaultFirstFrameMap.GetLength(1)
                && DefaultFirstFrameMap[row, col] == PlaceBarrier;
        }

        private static int FactoryTeamByQuadrant(int row, int col)
        {
            if (row < 25 && col < 25) return 1;
            if (row < 25 && col >= 25) return 2;
            if (row >= 25 && col < 25) return 3;
            return 4;
        }

        private static string ResourceSpriteKey(int index)
        {
            return (index % 3) switch
            {
                0 => "building_resource_large",
                1 => "building_resource_medium",
                _ => "building_resource_small"
            };
        }

        private static int Variant(int row, int col, int count)
        {
            return Mathf.Abs(row * 73856093 ^ col * 19349663) % Mathf.Max(count, 1) + 1;
        }

        private void CreateSprite(string objectName, string key, Vector3 position, Vector3 scale, int sortingOrder)
        {
            Sprite sprite = pixelAssets.GetSprite(key);
            if (sprite == null)
            {
                return;
            }

            GameObject go = new GameObject(objectName);
            go.transform.SetParent(_previewRoot.transform, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            RuntimeVisual visual = go.AddComponent<RuntimeVisual>();
            visual.assetKey = key;
        }
    }
}

