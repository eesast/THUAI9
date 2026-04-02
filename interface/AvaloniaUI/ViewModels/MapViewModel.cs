using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using THUAI9_Avalonia.Models;

namespace THUAI9_Avalonia.ViewModels
{
    /// <summary>
    /// 地图视图模型。
    /// </summary>
    public partial class MapViewModel : ViewModelBase
    {
        private const int GridSize = 50;

        [ObservableProperty]
        private ObservableCollection<MapCell> mapCells = new();

        public MapViewModel()
        {
            InitializeMapCells();
        }

        /// <summary>
        /// 初始化 50x50 地图格子。
        /// </summary>
        public void InitializeMapCells()
        {
            MapCells.Clear();
            for (int i = 0; i < GridSize; i++)
            {
                for (int j = 0; j < GridSize; j++)
                {
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

        /// <summary>
        /// 根据背景色自动选择黑/白文字颜色。
        /// </summary>
        private IBrush GetTextColorBasedOnBackground(IBrush background)
        {
            if (background is SolidColorBrush solidColor)
            {
                var color = solidColor.Color;
                double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
                return luminance > 0.5 ? Brushes.Black : Brushes.White;
            }
            return Brushes.Black;
        }

        /// <summary>
        /// 根据 MessageOfMap 更新基础地图。
        /// </summary>
        public void UpdateMap(Protobuf.MessageOfMap mapMessage)
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
                    UpdateCellType(i, j, row.Cols[j]);
                }
            }
        }

        private void UpdateCellType(int x, int y, Protobuf.PlaceType placeType)
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
                case Protobuf.PlaceType.Factory:
                    cell.CellType = MapCellType.Factory;
                    cell.DisplayColor = new SolidColorBrush(Colors.Cyan);
                    break;
                case Protobuf.PlaceType.Space:
                    cell.CellType = MapCellType.Space;
                    cell.DisplayColor = new SolidColorBrush(Colors.White);
                    break;
                case Protobuf.PlaceType.Barrier:
                    cell.CellType = MapCellType.Barrier;
                    cell.DisplayColor = new SolidColorBrush(Colors.DarkGray);
                    break;
                case Protobuf.PlaceType.Bush:
                    cell.CellType = MapCellType.Bush;
                    cell.DisplayColor = new SolidColorBrush(Colors.LightGreen);
                    break;
                case Protobuf.PlaceType.Resource:
                    cell.CellType = MapCellType.Resource;
                    cell.DisplayColor = new SolidColorBrush(Colors.Gold);
                    cell.DisplayText = "R";
                    break;
                case Protobuf.PlaceType.ComputeCenter:
                    cell.CellType = MapCellType.ComputeCenter;
                    cell.DisplayColor = new SolidColorBrush(Colors.LightBlue);
                    cell.DisplayText = "CC";
                    break;
                case Protobuf.PlaceType.Market:
                    cell.CellType = MapCellType.Market;
                    cell.DisplayColor = new SolidColorBrush(Colors.LightYellow);
                    cell.DisplayText = "M";
                    break;
                default:
                    cell.CellType = MapCellType.Space;
                    cell.DisplayColor = new SolidColorBrush(Colors.Gainsboro);
                    cell.DisplayText = "?";
                    break;
            }

            cell.ForegroundColor = GetTextColorBasedOnBackground(cell.DisplayColor);
        }

        public void UpdateBuildingCell(int x, int y, string team, string buildingType, int hp)
        {
            int index = x * GridSize + y;
            if (index < 0 || index >= MapCells.Count)
            {
                return;
            }

            var cell = MapCells[index];

            switch (buildingType)
            {
                case "Factory":
                    cell.CellType = MapCellType.Factory;
                    cell.DisplayColor = team switch
                    {
                        "Team1" => new SolidColorBrush(Colors.Red),
                        "Team2" => new SolidColorBrush(Colors.Blue),
                        "Team3" => new SolidColorBrush(Colors.Green),
                        "Team4" => new SolidColorBrush(Colors.Orange),
                        _ => new SolidColorBrush(Colors.Gray)
                    };
                    cell.DisplayText = $"{hp}";
                    break;
                case "ComputeCenter":
                    cell.CellType = MapCellType.ComputeCenter;
                    cell.DisplayColor = team switch
                    {
                        "Team1" => new SolidColorBrush(Colors.Red),
                        "Team2" => new SolidColorBrush(Colors.Blue),
                        "Team3" => new SolidColorBrush(Colors.Green),
                        "Team4" => new SolidColorBrush(Colors.Orange),
                        "Neutral" => new SolidColorBrush(Colors.LightBlue),
                        _ => new SolidColorBrush(Colors.LightBlue)
                    };
                    cell.DisplayText = team == "Neutral" ? "CC" : $"{hp}";
                    break;
                case "Market":
                    cell.CellType = MapCellType.Market;
                    cell.DisplayColor = new SolidColorBrush(Colors.LightYellow);
                    cell.DisplayText = "M";
                    break;
                default:
                    cell.DisplayColor = new SolidColorBrush(Colors.Gray);
                    cell.DisplayText = $"{hp}";
                    break;
            }

            cell.ForegroundColor = GetTextColorBasedOnBackground(cell.DisplayColor);
        }

        public void UpdateResourceCell(int x, int y, int remainingAmount)
        {
            int index = x * GridSize + y;
            if (index < 0 || index >= MapCells.Count)
            {
                return;
            }

            var cell = MapCells[index];
            cell.CellType = MapCellType.Resource;
            cell.DisplayColor = new SolidColorBrush(Colors.Gold);
            cell.DisplayText = remainingAmount.ToString();
            cell.ForegroundColor = Brushes.Black;
        }

        public void ResetCellToBaseType(int x, int y, Protobuf.PlaceType[,] baseMapState)
        {
            if (x < 0 || x >= GridSize || y < 0 || y >= GridSize)
            {
                return;
            }

            var baseType = baseMapState[x, y];
            UpdateCellType(x, y, baseType);
        }
    }
}
