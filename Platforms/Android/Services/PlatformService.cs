using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AviationApp.Services;
using Microsoft.Maui.Dispatching;
using Content = Android.Content;

namespace AviationApp.Platforms.Android.Services;

public class PlatformService : IPlatformService
{
    private const int FineLocationRequestCode = 100;
    private const int BackgroundLocationRequestCode = 101;
    private TaskCompletionSource<bool> permissionTcs;

    public async Task ShowPermissionPopupAndRequestAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null || activity.IsFinishing || activity.IsDestroyed)
        {
            System.Diagnostics.Debug.WriteLine("PlatformService: Activity is null or invalid, skipping popup");
            return;
        }

        var tcs = new TaskCompletionSource<bool>();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("PlatformService: Showing custom permission popup");
                var dialog = new AlertDialog.Builder(activity)
                    .SetTitle("Set Location Settings to ALL THE TIME and it may save your life.")
                    .SetMessage("Your device location data is used to calculate speed in knots. These data never leave the app and are not collected. The location is checked in background until you click to [=== PAUSE  ===].  2 nice to have permission popups included to allow for Notifications and Disable Battery Optimization.")
                    .SetPositiveButton("OK", (s, e) =>
                    {
                        System.Diagnostics.Debug.WriteLine("PlatformService: User clicked OK on custom popup");
                        tcs.SetResult(true);
                    })
                    .SetCancelable(false)
                    .Create();

                dialog.Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlatformService: Error showing popup: {ex}");
                tcs.SetException(ex);
            }
        });

        bool userConfirmed = await tcs.Task;

        if (userConfirmed)
        {
            System.Diagnostics.Debug.WriteLine("PlatformService: Starting location permission requests");
            await RequestLocationPermissionsAsync(activity);
        }
    }

    public async Task<bool> ArePermissionsGrantedAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null)
        {
            System.Diagnostics.Debug.WriteLine("PlatformService: Activity is null, assuming permissions not granted");
            return false;
        }

        bool fineLocationGranted = ActivityCompat.CheckSelfPermission(activity, "android.permission.ACCESS_FINE_LOCATION") == Permission.Granted;
        bool backgroundLocationGranted = Build.VERSION.SdkInt < BuildVersionCodes.Q || ActivityCompat.CheckSelfPermission(activity, "android.permission.ACCESS_BACKGROUND_LOCATION") == Permission.Granted;

        System.Diagnostics.Debug.WriteLine($"PlatformService: ArePermissionsGrantedAsync - Fine Location: {fineLocationGranted}, Background Location: {backgroundLocationGranted}");

        bool allGranted = fineLocationGranted && backgroundLocationGranted;
        return await Task.FromResult(allGranted);
    }

    private async Task RequestLocationPermissionsAsync(Activity activity)
    {
        if (activity == null || activity.IsFinishing || activity.IsDestroyed)
        {
            System.Diagnostics.Debug.WriteLine("PlatformService: Activity is invalid, skipping permission request");
            return;
        }

        // Request ACCESS_FINE_LOCATION first
        if (ActivityCompat.CheckSelfPermission(activity, "android.permission.ACCESS_FINE_LOCATION") != Permission.Granted)
        {
            System.Diagnostics.Debug.WriteLine("PlatformService: Requesting ACCESS_FINE_LOCATION");
            permissionTcs = new TaskCompletionSource<bool>();
            ActivityCompat.RequestPermissions(activity, new[] { "android.permission.ACCESS_FINE_LOCATION" }, FineLocationRequestCode);
            bool granted = await permissionTcs.Task;
            System.Diagnostics.Debug.WriteLine($"PlatformService: ACCESS_FINE_LOCATION request completed, granted: {granted}");
        }

        // Request ACCESS_BACKGROUND_LOCATION if fine location is granted (Android 10+)
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q &&
            ActivityCompat.CheckSelfPermission(activity, "android.permission.ACCESS_FINE_LOCATION") == Permission.Granted &&
            ActivityCompat.CheckSelfPermission(activity, "android.permission.ACCESS_BACKGROUND_LOCATION") != Permission.Granted)
        {
            System.Diagnostics.Debug.WriteLine("PlatformService: Requesting ACCESS_BACKGROUND_LOCATION");
            permissionTcs = new TaskCompletionSource<bool>();
            ActivityCompat.RequestPermissions(activity, new[] { "android.permission.ACCESS_BACKGROUND_LOCATION" }, BackgroundLocationRequestCode);
            bool granted = await permissionTcs.Task;
            System.Diagnostics.Debug.WriteLine($"PlatformService: ACCESS_BACKGROUND_LOCATION request completed, granted: {granted}");
        }
    }

    public void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        System.Diagnostics.Debug.WriteLine($"PlatformService: OnRequestPermissionsResult called with requestCode: {requestCode}, permissions: {string.Join(", ", permissions)}");
        if (requestCode == FineLocationRequestCode || requestCode == BackgroundLocationRequestCode)
        {
            bool granted = grantResults.Length > 0 && grantResults[0] == Permission.Granted;
            System.Diagnostics.Debug.WriteLine($"PlatformService: Permission result for code {requestCode}: {(granted ? "Granted" : "Denied")}");
            permissionTcs?.TrySetResult(granted);
        }
    }
}