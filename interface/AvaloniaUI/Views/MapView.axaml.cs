using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using System.Collections.Generic;
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

        private Canvas? _characterCanvas;
        private Grid? _mapGrid;
        private readonly Dictionary<long, CharacterVisual> _characterElements = new();
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
            if (DataContext is MapViewModel vm)
            {
                _viewModel = vm;
                TryInitializeMap();
            }
        }

        private void MapView_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _characterCanvas = this.FindControl<Canvas>("CharacterCanvas");
            _mapGrid = this.FindControl<Grid>("MapGrid");
            TryInitializeMap();
        }

        private void TryInitializeMap()
        {
            if (_viewModel != null && _mapGrid != null && _characterCanvas != null && !_isMapInitialized)
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

            const int gridSize = 50;
            const double cellSize = 20;

            foreach (var cell in _viewModel.MapCells)
            {
                var border = new Border
                {
                    Width = cellSize,
                    Height = cellSize,
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

            for (int i = 0; i < gridSize; i++)
            {
                _mapGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(cellSize)));
                _mapGrid.RowDefinitions.Add(new RowDefinition(new GridLength(cellSize)));
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
                int index = cell.CellX * 50 + cell.CellY;
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

        public void UpdateCharacterOnMap(long guid, int gridX, int gridY, int teamId, int hp, int maxHp)
        {
            if (_characterCanvas == null)
            {
                return;
            }

            const double cellSize = 20;
            double x = gridY * cellSize + cellSize / 2;
            double y = gridX * cellSize + cellSize / 2;

            var teamColor = teamId switch
            {
                0 => Brushes.Red,
                1 => Brushes.Blue,
                2 => Brushes.Green,
                3 => Brushes.Orange,
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
                    visual.HpBar.Width = Math.Max(4, 20 * ((double)hp / maxHp));
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
                Width = Math.Max(4, 20 * ((double)hp / maxHp)),
                Height = 3,
                Background = Brushes.LimeGreen,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };
            newHpBarContainer.Children.Add(newHpBarBackground);
            newHpBarContainer.Children.Add(newHpBar);
            newCharacterGrid.Children.Add(newHpBarContainer);

            Canvas.SetLeft(newCharacterGrid, x - 10);
            Canvas.SetTop(newCharacterGrid, y - 10);

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
