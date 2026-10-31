using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RPAWriter2.Converters
{
    /// <summary>
    /// Shows an element when a collection has more than <see cref="Threshold"/> items.
    /// <para>
    /// Used where the model exposes a collection but the decision "this control is only worth
    /// showing when there is a choice" belongs to the layout, not to the model
    /// (<c>documentation/13-model-layer.md</c>, rule 10). Example: the provider combo is hidden
    /// while a single provider is available.
    /// </para>
    /// </summary>
    public sealed class CountToVisibilityConverter : IValueConverter
    {
        /// <summary>Item count above which the element becomes visible (default 1).</summary>
        public int Threshold { get; set; } = 1;

        /// <summary>Visibility used when the count does not exceed <see cref="Threshold"/>.</summary>
        public Visibility HiddenVisibility { get; set; } = Visibility.Collapsed;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int count;
            switch (value)
            {
                case null:
                    count = 0;
                    break;
                case int integer:
                    count = integer;
                    break;
                case System.Collections.ICollection collection:
                    count = collection.Count;
                    break;
                default:
                    count = 0;
                    break;
            }

            return count > Threshold ? Visibility.Visible : HiddenVisibility;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
