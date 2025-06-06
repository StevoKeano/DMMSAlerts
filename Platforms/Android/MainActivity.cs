using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using AndroidX.Core.App;
using AviationApp.Platforms.Android.Services;
using AviationApp.Services;
using Android.Provider;

namespace AviationApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle savedInstanceState) //https://x.com/i/grok?conversation=1927470350159290717
    {
        base.OnCreate(savedInstanceState);
        // Set transparent status bar
        Window.SetFlags(WindowManagerFlags.LayoutNoLimits, WindowManagerFlags.LayoutNoLimits);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            Window.InsetsController?.SetSystemBarsAppearance(
                (int)WindowInsetsControllerAppearance.LightStatusBars,
                (int)WindowInsetsControllerAppearance.LightStatusBars);
        }
        else
        {
#pragma warning disable CA1422
            Window.SetFlags(WindowManagerFlags.TranslucentStatus, WindowManagerFlags.TranslucentStatus);
#pragma warning restore CA1422
        }

        // Request to ignore battery optimizations
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            var powerManager = (PowerManager)GetSystemService(PowerService);
            if (!powerManager.IsIgnoringBatteryOptimizations(PackageName))
            {
                var intent = new Intent(Settings.ActionRequestIgnoreBatteryOptimizations);
                intent.SetData(Android.Net.Uri.Parse("package:" + PackageName));
                StartActivity(intent);
            }
        }


        HandleAlertIntent(Intent);
    }

    protected override void OnNewIntent(Intent intent)
    {
        base.OnNewIntent(intent);
        HandleAlertIntent(intent);
    }

    private void HandleAlertIntent(Intent intent)
    {
        if (intent?.GetStringExtra("AlertMessage") is string alertMessage)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    Android.Widget.Toast.MakeText(this, alertMessage, Android.Widget.ToastLength.Long).Show();
                    Log.Debug("MainActivity", $"Displayed alert: {alertMessage}");
                }
                catch (Exception ex)
                {
                    Log.Error("MainActivity", $"Alert toast error: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        // Forward to PlatformService
        var platformService = MauiApplication.Current.Services.GetService<IPlatformService>() as PlatformService;
        platformService?.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == 100 || requestCode == 101)
        {
            bool fineLocationGranted = ActivityCompat.CheckSelfPermission(this, "android.permission.ACCESS_FINE_LOCATION") == Permission.Granted;
            bool backgroundLocationGranted = Build.VERSION.SdkInt < BuildVersionCodes.Q || ActivityCompat.CheckSelfPermission(this, "android.permission.ACCESS_BACKGROUND_LOCATION") == Permission.Granted;

            System.Diagnostics.Debug.WriteLine($"MainActivity: Permission Results - Fine Location: {fineLocationGranted}, Background Location: {backgroundLocationGranted}");

            if (!fineLocationGranted)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Android.Widget.Toast.MakeText(this, "Location permission denied. Monitoring cannot function.", Android.Widget.ToastLength.Long).Show();
                });
            }
            else if (!backgroundLocationGranted)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Android.Widget.Toast.MakeText(this, "Background location permission denied. Monitoring may be limited in the background.", Android.Widget.ToastLength.Long).Show();
                });
            }
        }
    }
}