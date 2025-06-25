using AviationApp.Services;
using Microsoft.Maui.Controls;
using System.Globalization;

namespace AviationApp;

public partial class App : Application
{
    public App(IPlatformService platformService, LocationService locationService)
    {
        InitializeComponent();
        Resources.Add("StringToBoolConverter", new StringToBoolConverter());
        MainPage = new NavigationPage(new MainPage(platformService, locationService));
    }
}
public class StringToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string stringValue && parameter is string param)
        {
            return stringValue == param;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}