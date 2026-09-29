using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Converters
{
    public class ActivityTypeToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = value switch
            {
                ActivityType.Success => "BrushSuccess",
                ActivityType.Warning => "BrushWarning",
                ActivityType.Error => "BrushError",
                _ => "BrushAccent",
            };

            return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
