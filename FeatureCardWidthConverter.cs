using System.Globalization;
using System.Windows.Data;

namespace RidersToolkit;

internal sealed class FeatureCardWidthConverter : IValueConverter
{
    public object Convert(object value,Type target,object parameter,CultureInfo culture)
    {
        double width=value is double available&&double.IsFinite(available)?available:0;
        return Math.Max(1,(width>=680?Math.Floor(width/2):width)-4);
    }
    public object ConvertBack(object value,Type target,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
