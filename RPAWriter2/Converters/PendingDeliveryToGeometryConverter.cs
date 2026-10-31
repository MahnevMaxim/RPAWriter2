using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Material.Icons;
using RPAWriter.Core.Models;

namespace RPAWriter2.Converters
{
    /// <summary>
    /// Resolves the queue-delivery icon (<see cref="PendingDelivery.Steer"/> → fast-forward,
    /// <see cref="PendingDelivery.Queue"/> → hourglass) and returns it as a WPF <see cref="Geometry"/>.
    /// Lives in the view because the model must not reference the icon pack.
    /// </summary>
    public class PendingDeliveryToGeometryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var delivery = value is PendingDelivery mode ? mode : PendingDelivery.Queue;
            var kind = delivery == PendingDelivery.Steer
                ? MaterialIconKind.FastForward
                : MaterialIconKind.HourglassEmpty;

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
