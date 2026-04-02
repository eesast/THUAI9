using Avalonia;
using Avalonia.Media;

namespace THUAI9_Avalonia.Models
{
    /// <summary>
    /// 地图图例项模型。
    /// </summary>
    public class LegendItem
    {
        public IBrush Color { get; set; }
        public string Description { get; set; }
        public IBrush? Stroke { get; set; }
        public Thickness BorderThickness { get; set; }

        public LegendItem(IBrush color, string description, IBrush? stroke = null, Thickness borderThickness = default)
        {
            Color = color;
            Description = description;
            Stroke = stroke;
            BorderThickness = borderThickness;
        }
    }
}
