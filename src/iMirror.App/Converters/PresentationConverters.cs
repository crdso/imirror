using System.Globalization;
using System.Windows;
using System.Windows.Data;
using iMirror.Input;

namespace iMirror.App.Converters;

public sealed class PageSelectionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is int page && page == int.Parse((string)parameter, CultureInfo.InvariantCulture);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class PageVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int page && page == int.Parse((string)parameter, CultureInfo.InvariantCulture) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class ChromeVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class KeyboardLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    { KeyboardLayoutMode.PortugueseBrazilAbnt2 => "Português Brasil · ABNT2", KeyboardLayoutMode.UnitedStates => "English · US", _ => "Automático · layout do Windows" };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
