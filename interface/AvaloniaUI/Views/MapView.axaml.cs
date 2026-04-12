using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Protobuf;
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
            public required Border Body { get; init; }
            public required Border HpBar { get; init; }
            public required TextBlock Label { get; init; }
            public double GameX { get; set; }
            public double GameY { get; set; }
            public int TeamId { get; set; }
            public int Hp { get; set; }
            public int MaxHp { get; set; }
            public long PlayerId { get; set; }
            public CharacterType CharacterType { get; set; }
        }

        private const int GridSize = 50;
        private const double CellSize = 20;

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
                Width = CellSize,
                Height = CellSize,
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

            border.Background = Brushes.Transparent;
            border.BorderBrush = Brushes.Transparent;
            border.BorderThickness = new Thickness(0);
            border.Opacity = 1;
            border.CornerRadius = new CornerRadius(0);

            if (border.Child is TextBlock textBlock)
            {
                textBlock.Text = overlay.Label;
                textBlock.Foreground = overlay.Foreground;
            }

            border.Width = CellSize;
            border.Height = CellSize;
            ToolTip.SetTip(border, overlay.Tooltip);
            Canvas.SetLeft(border, overlay.CellY * CellSize);
            Canvas.SetTop(border, overlay.CellX * CellSize);
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

        public void UpdateCharacterOnMap(long guid, int gameX, int gameY, int teamId, int hp, int maxHp, long playerId, CharacterType characterType)
        {
            if (_characterCanvas == null)
            {
                return;
            }

            double x = gameY / 1000.0 * CellSize + CellSize / 2;
            double y = gameX / 1000.0 * CellSize + CellSize / 2;
            var teamColor = GetTeamBrush(teamId);

            if (_characterElements.TryGetValue(guid, out var visual))
            {
                if (Math.Abs(visual.GameX - gameX) > double.Epsilon || Math.Abs(visual.GameY - gameY) > double.Epsilon)
                {
                    Canvas.SetLeft(visual.Root, x - 8);
                    Canvas.SetTop(visual.Root, y - 10);
                    visual.GameX = gameX;
                    visual.GameY = gameY;
                }

                if (visual.TeamId != teamId || visual.CharacterType != characterType)
                {
                    ApplyBodyStyle(visual.Body, characterType, teamColor);
                    visual.TeamId = teamId;
                    visual.CharacterType = characterType;
                }

                if (visual.Hp != hp || visual.MaxHp != maxHp)
                {
                    visual.HpBar.Width = Math.Max(4, 24 * ((double)hp / Math.Max(maxHp, 1)));
                    visual.Hp = hp;
                    visual.MaxHp = maxHp;
                }

                if (visual.PlayerId != playerId)
                {
                    visual.Label.Text = $"P{playerId}";
                    visual.PlayerId = playerId;
                }

                return;
            }

            var body = new Border
            {
                Width = 10,
                Height = 10,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1)
            };
            ApplyBodyStyle(body, characterType, teamColor);

            var label = new TextBlock
            {
                Text = $"P{playerId}",
                FontSize = 8,
                FontWeight = FontWeight.Bold,
                Foreground = teamColor,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            var hpBarBackground = new Border
            {
                Width = 16,
                Height = 3,
                Background = Brushes.DarkGray,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            var hpBar = new Border
            {
                Width = Math.Max(3, 16 * ((double)hp / Math.Max(maxHp, 1))),
                Height = 3,
                Background = Brushes.LimeGreen,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };

            var hpBarContainer = new Grid
            {
                Margin = new Thickness(0, 0, 0, 1)
            };
            hpBarContainer.Children.Add(hpBarBackground);
            hpBarContainer.Children.Add(hpBar);

            var root = new Grid
            {
                Width = 16,
                Height = 22,
                RowDefinitions = new RowDefinitions("Auto,Auto,*")
            };
            Grid.SetRow(hpBarContainer, 0);
            Grid.SetRow(body, 1);
            Grid.SetRow(label, 2);
            root.Children.Add(hpBarContainer);
            root.Children.Add(body);
            root.Children.Add(label);

            Canvas.SetLeft(root, x - 8);
            Canvas.SetTop(root, y - 10);

            _characterCanvas.Children.Add(root);
            _characterElements[guid] = new CharacterVisual
            {
                Root = root,
                Body = body,
                HpBar = hpBar,
                Label = label,
                GameX = gameX,
                GameY = gameY,
                TeamId = teamId,
                Hp = hp,
                MaxHp = maxHp,
                PlayerId = playerId,
                CharacterType = characterType
            };
        }

        private static IBrush GetTeamBrush(int teamId)
        {
            return teamId switch
            {
                1 => Brushes.Red,
                2 => Brushes.Blue,
                3 => Brushes.Green,
                4 => Brushes.Orange,
                _ => Brushes.Gray
            };
        }

        private static void ApplyBodyStyle(Border body, CharacterType characterType, IBrush teamColor)
        {
            body.Background = teamColor;
            switch (characterType)
            {
                case CharacterType.Drone:
                    body.CornerRadius = new CornerRadius(5);
                    body.Width = 10;
                    body.Height = 10;
                    break;
                case CharacterType.Robot:
                    body.CornerRadius = new CornerRadius(2);
                    body.Width = 10;
                    body.Height = 10;
                    break;
                case CharacterType.AutonomousCar:
                    body.CornerRadius = new CornerRadius(3);
                    body.Width = 14;
                    body.Height = 8;
                    break;
                default:
                    body.CornerRadius = new CornerRadius(4);
                    body.Width = 10;
                    body.Height = 10;
                    break;
            }
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
