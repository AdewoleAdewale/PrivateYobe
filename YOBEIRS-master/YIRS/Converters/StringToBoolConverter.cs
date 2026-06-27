using System;
using System.Globalization;
using Xamarin.Forms;

namespace YIRS.Converters
{
    /// <summary>
    /// Converts a string to boolean (true if not null/empty)
    /// </summary>
    public class StringToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !string.IsNullOrWhiteSpace(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Inverts a boolean value
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return true;
        }



    }

    /// <summary>
    /// Formats currency values with proper Nigerian Naira symbol
    /// </summary>
    public class CurrencyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "₦0.00";

            if (decimal.TryParse(value.ToString(), out decimal amount))
            {
                return $"₦{amount:N2}";
            }

            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Returns color based on amount value (red for high amounts, green for low)
    /// </summary>
    public class AmountToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (decimal.TryParse(value?.ToString(), out decimal amount))
            {
                if (amount >= 10000)
                    return Color.FromHex("#D32F2F"); // Red for high amounts
                else if (amount >= 5000)
                    return Color.FromHex("#F57C00"); // Orange for medium
                else
                    return Color.FromHex("#388E3C"); // Green for low
            }

            return Color.FromHex("#666666"); // Gray default
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Truncates long text with ellipsis
    /// </summary>
    public class TextTruncateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;

            string text = value.ToString();
            int maxLength = 100;

            if (parameter != null && int.TryParse(parameter.ToString(), out int paramLength))
            {
                maxLength = paramLength;
            }

            if (text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength) + "...";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}