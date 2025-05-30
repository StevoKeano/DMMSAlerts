using AviationApp.Services;
using Microsoft.Maui.Controls;

namespace AviationApp;

public partial class App : Application
{
    public App(IPlatformService platformService, LocationService locationService)
    {
        InitializeComponent();
        MainPage = new NavigationPage(new MainPage(platformService, locationService));
    }
}