using Avalonia.Media;

namespace THUAI9_Avalonia.Models
{
    /// <summary>
    /// 地图单元格类型
    /// </summary>
    public enum MapCellType
    {
        Space,              // 空地
        Factory,            // 工厂
        Barrier,            // 障碍
        Bush,               // 草丛
        Resource,           // 资源点
        ComputeCenter,      // 算力中心
        Market,             // 市场
        Building,           // 建筑（动态）
        Trap                // 陷阱（动态）
    }

    /// <summary>
    /// 地图单元格模型
    /// </summary>
    public class MapCell
    {
        public int CellX { get; set; }              // 单元格 X 坐标（网格索引）
        public int CellY { get; set; }              // 单元格 Y 坐标（网格索引）
        public MapCellType CellType { get; set; }   // 单元格类型
        public IBrush DisplayColor { get; set; }    // 显示颜色（背景）
        public IBrush ForegroundColor { get; set; } // 前景颜色（文字）
        public string DisplayText { get; set; }     // 显示文字（血量、类型等）

        public MapCell()
        {
            DisplayColor = new SolidColorBrush(Colors.White);
            ForegroundColor = new SolidColorBrush(Colors.Black);
            DisplayText = "";
        }
    }
}
