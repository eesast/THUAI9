using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace THUAI9_Avalonia.Models
{
    public enum MapOverlayKind
    {
        Factory,
        Resource,
        ComputeCenter,
        Market
    }

    /// <summary>
    /// 动态地图覆盖物。底图只保留静态地形，建筑/资源/据点状态以覆盖层形式渲染。
    /// </summary>
    public partial class MapOverlayItem : ObservableObject
    {
        [ObservableProperty]
        private string key = string.Empty;

        [ObservableProperty]
        private int cellX;

        [ObservableProperty]
        private int cellY;

        [ObservableProperty]
        private string label = string.Empty;

        [ObservableProperty]
        private string tooltip = string.Empty;

        [ObservableProperty]
        private IBrush background = Brushes.Gray;

        [ObservableProperty]
        private IBrush foreground = Brushes.White;

        [ObservableProperty]
        private IBrush borderBrush = Brushes.White;

        [ObservableProperty]
        private double opacity = 0.88;

        [ObservableProperty]
        private MapOverlayKind kind;
    }
}
