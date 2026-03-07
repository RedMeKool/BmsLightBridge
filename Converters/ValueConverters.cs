using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BmsLightBridge.Models;

namespace BmsLightBridge.Converters
{
    /// <summary>bool → Brush (voor verbindingsstatus en lampjes)</summary>
    [ValueConversion(typeof(bool), typeof(Brush))]
    public class BoolToColorConverter : IValueConverter
    {
        public Brush TrueColor  { get; set; } = new SolidColorBrush(Color.FromRgb(78, 201, 148));   // Groen
        public Brush FalseColor { get; set; } = new SolidColorBrush(Color.FromRgb(100, 100, 120));  // Grijs

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? TrueColor : FalseColor;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>bool → Visibility (voor tonen/verbergen van UI elementen)</summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool bVal && bVal;
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>DeviceType → Visibility (voor Arduino/WinWing panels)</summary>
    [ValueConversion(typeof(DeviceType), typeof(Visibility))]
    public class DeviceTypeToVisibilityConverter : IValueConverter
    {
        public DeviceType ShowFor { get; set; } = DeviceType.Arduino;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is DeviceType dt && dt == ShowFor ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// bool → "ON"/"OFF" text for use in XAML bindings.
    /// Note: SignalViewModel.StatusText provides the same conversion for the signal list
    /// items; both are intentionally kept — the converter is used for non-SignalViewModel
    /// bindings (e.g. SelectedSignal.IsOn in the mapping panel).
    /// </summary>
    [ValueConversion(typeof(bool), typeof(string))]
    public class BoolToOnOffConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? "ON" : "OFF";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>bool → achtergrondkleur voor lampje indicator</summary>
    [ValueConversion(typeof(bool), typeof(Brush))]
    public class LightStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isOn && isOn)
                return new SolidColorBrush(Color.FromRgb(255, 210, 50));   // Geel/aan
            return new SolidColorBrush(Color.FromRgb(50, 50, 65));          // Donker/uit
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>bool → tekst voor mapping status</summary>
    [ValueConversion(typeof(bool), typeof(string))]
    public class MappedStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? "✓" : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>null → Collapsed, non-null → Visible</summary>
    public class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>null or empty string → Collapsed, non-empty → Visible</summary>
    public class StringNullOrEmptyToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
