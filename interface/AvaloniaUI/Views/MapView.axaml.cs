using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using THUAI9_Avalonia.Models;
using THUAI9_Avalonia.ViewModels;

namespace THUAI9_Avalonia.Views
{
    public partial class MapView : UserControl
    {
        private sealed class CharacterVisual
        {
            public required Grid Root { get; init; }
            public required Ellipse Body { get; init; }
            public required Border HpBar { get; init; }
            public int GridX { get; set; }
            public int GridY { get; set; }
            public int TeamId { get; set; }
            public int Hp { get; set; }
            public int MaxHp { get; set; }
        }

        private const int GridSize = 50;
        private const double CellSize = 20;
        private const double CharacterVisualSize = 20;

        private Canvas? _characterCanvas;
        private Canvas? _dynamicOverlayCanvas;
        private Grid? _mapGrid;
        private readonly Dictionary<long, CharacterVisual> _characterElements = new();
        private readonly Dictionary<string, Border> _dynamicOverlayElements = new();
        private MapViewModel? _viewModel;
        private bool _isMapInitialized;

        public MapView()
        {
            InitializeComponent();
            DataContextChanged += MapView_DataContextChanged;
            AttachedToVisualTree += MapView_AttachedToVisualTree;
        }

        private void MapView_DataContextChanged(object? sender, EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.DynamicOverlays.CollectionChanged -= DynamicOverlays_CollectionChanged;
                foreach (var overlay in _viewModel.DynamicOverlays)
                {
                    overlay.PropertyChanged -= Overlay_PropertyChanged;
                }
            }

            if (DataContext is MapViewModel vm)
            {
                _viewModel = vm;
                _viewModel.DynamicOverlays.CollectionChanged += DynamicOverlays_CollectionChanged;
                TryInitializeMap();
                RefreshDynamicOverlays();
            }
        }

        private void MapView_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _characterCanvas = this.FindControl<Canvas>("CharacterCanvas");
            _dynamicOverlayCanvas = this.FindControl<Canvas>("DynamicOverlayCanvas");
            _mapGrid = this.FindControl<Grid>("MapGrid");
            TryInitializeMap();
            RefreshDynamicOverlays();
        }

        private void TryInitializeMap()
        {
            if (_viewModel != null && _mapGrid != null && _characterCanvas != null && _dynamicOverlayCanvas != null && !_isMapInitialized)
            {
                InitializeMapGrid();
                _isMapInitialized = true;
            }
        }

        private void InitializeMapGrid()
        {
            if (_mapGrid == null || _viewModel == null)
            {
                return;
            }

            _mapGrid.Children.Clear();
            _mapGrid.ColumnDefinitions.Clear();
            _mapGrid.RowDefinitions.Clear();

            for (int i = 0; i < GridSize; i++)
            {
                _mapGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(CellSize)));
                _mapGrid.RowDefinitions.Add(new RowDefinition(new GridLength(CellSize)));
            }

            foreach (var cell in _viewModel.MapCells)
            {
                var border = new Border
                {
                    Width = CellSize,
                    Height = CellSize,
                    Background = cell.DisplayColor,
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(0.5)
                };

                if (!string.IsNullOrEmpty(cell.DisplayText))
                {
                    border.Child = new TextBlock
                    {
                        Text = cell.DisplayText,
                        FontSize = 8,
                        Foreground = cell.ForegroundColor,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };
                }

                Grid.SetColumn(border, cell.CellY);
                Grid.SetRow(border, cell.CellX);
                _mapGrid.Children.Add(border);
            }
        }

        public void RefreshMap()
        {
            if (_mapGrid == null || _viewModel == null)
            {
                return;
            }

            foreach (var cell in _viewModel.MapCells)
            {
                int index = cell.CellX * GridSize + cell.CellY;
                if (index < _mapGrid.Children.Count && _mapGrid.Children[index] is Border border)
                {
                    border.Background = cell.DisplayColor;

                    if (string.IsNullOrEmpty(cell.DisplayText))
                    {
                        border.Child = null;
                    }
                    else if (border.Child is TextBlock textBlock)
                    {
                        textBlock.Text = cell.DisplayText;
                        textBlock.Foreground = cell.ForegroundColor;
                    }
                    else
                    {
                        border.Child = new TextBlock
                        {
                            Text = cell.DisplayText,
                            FontSize = 8,
                            Foreground = cell.ForegroundColor,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        };
                    }
                }
            }
        }

        private void DynamicOverlays_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_dynamicOverlayCanvas == null)
            {
                return;
            }

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                _dynamicOverlayCanvas.Children.Clear();
                _dynamicOverlayElements.Clear();
                return;
            }

            if (e.OldItems != null)
            {
                foreach (MapOverlayItem overlay in e.OldItems)
                {
                    overlay.PropertyChanged -= Overlay_PropertyChanged;
                    RemoveDynamicOverlayVisual(overlay.Key);
                }
            }

            if (e.NewItems != null)
            {
                foreach (MapOverlayItem overlay in e.NewItems)
                {
                    overlay.PropertyChanged += Overlay_PropertyChanged;
                    AddDynamicOverlayVisual(overlay);
                }
            }
        }

        private void Overlay_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is MapOverlayItem overlay)
            {
                UpdateDynamicOverlayVisual(overlay);
            }
        }

        private void RefreshDynamicOverlays()
        {
            if (_viewModel == null || _dynamicOverlayCanvas == null)
            {
                return;
            }

            _dynamicOverlayCanvas.Children.Clear();
            _dynamicOverlayElements.Clear();

            foreach (var overlay in _viewModel.DynamicOverlays)
            {
                overlay.PropertyChanged -= Overlay_PropertyChanged;
                overlay.PropertyChanged += Overlay_PropertyChanged;
                AddDynamicOverlayVisual(overlay);
            }
        }

        private void AddDynamicOverlayVisual(MapOverlayItem overlay)
        {
            if (_dynamicOverlayCanvas == null || _dynamicOverlayElements.ContainsKey(overlay.Key))
            {
                return;
            }

            var textBlock = new TextBlock
            {
                FontSize = 8,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var border = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Child = textBlock
            };

            ToolTip.SetTip(border, overlay.Tooltip);
            _dynamicOverlayCanvas.Children.Add(border);
            _dynamicOverlayElements[overlay.Key] = border;
            UpdateDynamicOverlayVisual(overlay);
        }

        private void UpdateDynamicOverlayVisual(MapOverlayItem overlay)
        {
            if (_dynamicOverlayCanvas == null || !_dynamicOverlayElements.TryGetValue(overlay.Key, out var border))
            {
                return;
            }

            border.Background = overlay.Background;
            border.BorderBrush = overlay.BorderBrush;
            border.Opacity = overlay.Opacity;
            border.CornerRadius = overlay.Kind switch
            {
                MapOverlayKind.Resource => new CornerRadius(9),
                MapOverlayKind.ComputeCenter => new CornerRadius(3),
                MapOverlayKind.Market => new CornerRadius(6),
                _ => new CornerRadius(4)
            };

            if (border.Child is TextBlock textBlock)
            {
                textBlock.Text = overlay.Label;
                textBlock.Foreground = overlay.Foreground;
            }

            ToolTip.SetTip(border, overlay.Tooltip);
            Canvas.SetLeft(border, overlay.CellY * CellSize + 1);
            Canvas.SetTop(border, overlay.CellX * CellSize + 1);
        }

        private void RemoveDynamicOverlayVisual(string key)
        {
            if (_dynamicOverlayCanvas == null)
            {
                return;
            }

            if (_dynamicOverlayElements.TryGetValue(key, out var border))
            {
                _dynamicOverlayCanvas.Children.Remove(border);
                _dynamicOverlayElements.Remove(key);
            }
        }

        public void UpdateCharacterOnMap(long guid, int gridX, int gridY, int teamId, int hp, int maxHp)
        {
            if (_characterCanvas == null)
            {
                return;
            }

            double x = gridY * CellSize + CellSize / 2;
            double y = gridX * CellSize + CellSize / 2;

            var teamColor = teamId switch
            {
                1 => Brushes.Red,
                2 => Brushes.Blue,
                3 => Brushes.Green,
                4 => Brushes.Orange,
                _ => Brushes.Gray
            };

            if (_characterElements.TryGetValue(guid, out var visual))
            {
                if (visual.GridX != gridX || visual.GridY != gridY)
                {
                    Canvas.SetLeft(visual.Root, x - 10);
                    Canvas.SetTop(visual.Root, y - 10);
                    visual.GridX = gridX;
                    visual.GridY = gridY;
                }

                if (visual.TeamId != teamId)
                {
                    visual.Body.Fill = teamColor;
                    visual.TeamId = teamId;
                }

                if (visual.Hp != hp || visual.MaxHp != maxHp)
                {
                    visual.HpBar.Width = Math.Max(4, 20 * ((double)hp / Math.Max(maxHp, 1)));
                    visual.Hp = hp;
                    visual.MaxHp = maxHp;
                }

                return;
            }

            var body = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = teamColor,
                Stroke = Brushes.White,
                StrokeThickness = 1
            };
            var newCharacterGrid = new Grid();
            newCharacterGrid.Children.Add(body);

            var newHpBarContainer = new Grid();
            var newHpBarBackground = new Border
            {
                Width = 20,
                Height = 3,
                Background = Brushes.DarkGray,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            var newHpBar = new Border
            {
                Width = Math.Max(4, 20 * ((double)hp / Math.Max(maxHp, 1))),
                Height = 3,
                Background = Brushes.LimeGreen,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };
            newHpBarContainer.Children.Add(newHpBarBackground);
            newHpBarContainer.Children.Add(newHpBar);
            newCharacterGrid.Children.Add(newHpBarContainer);

            Canvas.SetLeft(newCharacterGrid, x - CharacterVisualSize / 2);
            Canvas.SetTop(newCharacterGrid, y - CharacterVisualSize / 2);

            _characterCanvas.Children.Add(newCharacterGrid);
            _characterElements[guid] = new CharacterVisual
            {
                Root = newCharacterGrid,
                Body = body,
                HpBar = newHpBar,
                GridX = gridX,
                GridY = gridY,
                TeamId = teamId,
                Hp = hp,
                MaxHp = maxHp
            };
        }

        public void RemoveCharacterFromMap(long guid)
        {
            if (_characterCanvas == null)
            {
                return;
            }

            if (_characterElements.TryGetValue(guid, out var visual))
            {
                _characterCanvas.Children.Remove(visual.Root);
                _characterElements.Remove(guid);
            }
        }

        public void ClearAllCharacters()
        {
            if (_characterCanvas == null)
            {
                return;
            }

            _characterCanvas.Children.Clear();
            _characterElements.Clear();
        }

        public static (int gridX, int gridY) GamePosToGridPos(int gameX, int gameY)
        {
            return (gameX / 1000, gameY / 1000);
        }
    }
}
