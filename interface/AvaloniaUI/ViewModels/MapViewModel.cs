using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Protobuf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using THUAI9_Avalonia.Models;

namespace THUAI9_Avalonia.ViewModels
{
    /// <summary>
    /// 地图视图模型：底图与动态覆盖物分层维护。
    /// </summary>
    public partial class MapViewModel : ViewModelBase
    {
        private const int GridSize = 50;
        private readonly PlaceType[,] _baseMapState = new PlaceType[GridSize, GridSize];
        private readonly Dictionary<string, MapOverlayItem> _dynamicOverlayIndex = new();

        [ObservableProperty]
        private ObservableCollection<MapCell> mapCells = new();

        [ObservableProperty]
        private ObservableCollection<MapOverlayItem> dynamicOverlays = new();

        public MapViewModel()
        {
            InitializeMapCells();
        }

        public void InitializeMapCells()
        {
            MapCells.Clear();
            DynamicOverlays.Clear();
            _dynamicOverlayIndex.Clear();

            for (int i = 0; i < GridSize; i++)
            {
                for (int j = 0; j < GridSize; j++)
                {
                    _baseMapState[i, j] = PlaceType.Space;
                    MapCells.Add(new MapCell
                    {
                        CellX = i,
                        CellY = j,
                        CellType = MapCellType.Space,
                        DisplayColor = new SolidColorBrush(Colors.White),
                        DisplayText = string.Empty
                    });
                }
            }
        }

        private IBrush GetTextColorBasedOnBackground(IBrush background)
        {
            if (background is SolidColorBrush solidColor)
            {
                var color = solidColor.Color;
                double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
                return luminance > 0.35 ? Brushes.Black : Brushes.White;
            }

            return Brushes.Black;
        }

        public void UpdateMap(MessageOfMap mapMessage)
        {
            if (mapMessage == null || mapMessage.Rows == null)
            {
                return;
            }

            for (int i = 0; i < mapMessage.Rows.Count && i < GridSize; i++)
            {
                var row = mapMessage.Rows[i];
                if (row == null || row.Cols == null)
                {
                    continue;
                }

                for (int j = 0; j < row.Cols.Count && j < GridSize; j++)
                {
                    _baseMapState[i, j] = row.Cols[j];
                    UpdateCellType(i, j, row.Cols[j]);
                }
            }
        }

        private void UpdateCellType(int x, int y, PlaceType placeType)
        {
            int index = x * GridSize + y;
            if (index < 0 || index >= MapCells.Count)
            {
                return;
            }

            var cell = MapCells[index];
            cell.DisplayText = string.Empty;

            switch (placeType)
            {
                case PlaceType.Factory:
                    cell.CellType = MapCellType.Factory;
                    cell.DisplayColor = new SolidColorBrush(Colors.Cyan);
                    break;
                case PlaceType.Space:
                    cell.CellType = MapCellType.Space;
                    cell.DisplayColor = new SolidColorBrush(Colors.White);
                    break;
                case PlaceType.Barrier:
                    cell.CellType = MapCellType.Barrier;
                    cell.DisplayColor = new SolidColorBrush(Colors.DarkGray);
                    break;
                case PlaceType.Bush:
                    cell.CellType = MapCellType.Bush;
                    cell.DisplayColor = new SolidColorBrush(Colors.LightGreen);
                    break;
                case PlaceType.Resource:
                    cell.CellType = MapCellType.Resource;
                    cell.DisplayColor = new SolidColorBrush(Colors.Gold);
                    break;
                case PlaceType.ComputeCenter:
                    cell.CellType = MapCellType.ComputeCenter;
                    cell.DisplayColor = new SolidColorBrush(Colors.LightBlue);
                    break;
                case PlaceType.Market:
                    cell.CellType = MapCellType.Market;
                    cell.DisplayColor = new SolidColorBrush(Colors.LightYellow);
                    break;
                default:
                    cell.CellType = MapCellType.Space;
                    cell.DisplayColor = new SolidColorBrush(Colors.Gainsboro);
                    cell.DisplayText = "？";
                    break;
            }

            cell.ForegroundColor = GetTextColorBasedOnBackground(cell.DisplayColor);
        }

        public void UpsertDynamicOverlay(MapOverlayItem overlay)
        {
            if (_dynamicOverlayIndex.TryGetValue(overlay.Key, out var existing))
            {
                if (existing.CellX != overlay.CellX)
                {
                    existing.CellX = overlay.CellX;
                }

                if (existing.CellY != overlay.CellY)
                {
                    existing.CellY = overlay.CellY;
                }

                if (!string.Equals(existing.Label, overlay.Label, StringComparison.Ordinal))
                {
                    existing.Label = overlay.Label;
                }

                if (!string.Equals(existing.Tooltip, overlay.Tooltip, StringComparison.Ordinal))
                {
                    existing.Tooltip = overlay.Tooltip;
                }

                if (!AreBrushesEqual(existing.Background, overlay.Background))
                {
                    existing.Background = overlay.Background;
                }

                if (!AreBrushesEqual(existing.Foreground, overlay.Foreground))
                {
                    existing.Foreground = overlay.Foreground;
                }

                if (!AreBrushesEqual(existing.BorderBrush, overlay.BorderBrush))
                {
                    existing.BorderBrush = overlay.BorderBrush;
                }

                if (Math.Abs(existing.Opacity - overlay.Opacity) > 0.001)
                {
                    existing.Opacity = overlay.Opacity;
                }

                if (existing.Kind != overlay.Kind)
                {
                    existing.Kind = overlay.Kind;
                }

                return;
            }

            _dynamicOverlayIndex[overlay.Key] = overlay;
            DynamicOverlays.Add(overlay);
        }

        private static bool AreBrushesEqual(IBrush? left, IBrush? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is ISolidColorBrush leftSolid && right is ISolidColorBrush rightSolid)
            {
                return leftSolid.Color == rightSolid.Color
                    && Math.Abs(leftSolid.Opacity - rightSolid.Opacity) <= 0.001;
            }

            return Equals(left, right);
        }

        public void RemoveDynamicOverlay(string key)
        {
            if (_dynamicOverlayIndex.TryGetValue(key, out var overlay))
            {
                DynamicOverlays.Remove(overlay);
                _dynamicOverlayIndex.Remove(key);
            }
        }

        public void ClearDynamicOverlays()
        {
            DynamicOverlays.Clear();
            _dynamicOverlayIndex.Clear();
        }

        public void ResetCellToBaseType(int x, int y)
        {
            if (x < 0 || x >= GridSize || y < 0 || y >= GridSize)
            {
                return;
            }

            UpdateCellType(x, y, _baseMapState[x, y]);
        }
    }
}
