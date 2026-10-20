using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Material.Icons;

namespace RPAWriter2.Converters
{
    /// <summary>
    /// Converts a <see cref="MaterialIconKind"/> into a WPF <see cref="Geometry"/>
    /// for rendering in the Controls -> svg gallery.
    /// </summary>
    public class MaterialIconToGeometryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is MaterialIconKind kind))
                return Geometry.Empty;

            try
            {
                return MaterialIconDataProvider.Get<Geometry>(kind);
            }
            catch
            {
                return Geometry.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}