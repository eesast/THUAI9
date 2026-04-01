using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace THUAI9_Avalonia.Converters
{
    /// <summary>
    /// 将队伍 ID 转换为颜色。
    /// </summary>
    public class TeamIdToColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int teamId)
            {
                return teamId switch
                {
                    0 => new SolidColorBrush(Colors.Red),
                    1 => new SolidColorBrush(Colors.Blue),
                    2 => new SolidColorBrush(Colors.Green),
                    3 => new SolidColorBrush(Colors.Orange),
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// 将日志级别转换为颜色。
    /// </summary>
    public class LevelToColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string level)
            {
                return level.ToUpper() switch
                {
                    "INFO" => Colors.LightBlue,
                    "SUCCESS" => Colors.LightGreen,
                    "WARNING" => Colors.Orange,
                    "ERROR" => Colors.LightCoral,
                    _ => Colors.LightGray
                };
            }
            return Colors.LightGray;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
