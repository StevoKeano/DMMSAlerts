using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using Android.Util;
using AndroidX.Core.App;
using CommunityToolkit.Mvvm.Messaging;
using System.Net.Http.Json;
using Location = Android.Locations.Location;

namespace AviationApp.Services;

[Service(ForegroundServiceType = Android.Content.PM.ForegroundService.TypeLocation)]
public class LocationService : Service
{
    private LocationManager locationManager;
    private Notification notification;
    private LocationListener locationListener;
    private const float DMMS_THRESHOLD = 258.0f; // Knots, from log
    private const float METERS_PER_SECOND_TO_KNOTS = 1.94384f; // Conversion factor
    private DateTime lastAlertTime = DateTime.MinValue;
    private readonly TimeSpan alertThrottleInterval = TimeSpan.FromSeconds(5); // Throttle alerts to every 5 seconds

    public override void OnCreate()
    {
        base.OnCreate();
        Log.Debug("LocationService", "OnCreate called");
        try
        {
            locationManager = GetSystemService(LocationService) as LocationManager;
            if (locationManager == null)
            {
                Log.Error("LocationService", "LocationManager is null");
                System.Diagnostics.Debug.WriteLine("LocationService: LocationManager is null");
                return;
            }

            // Check permissions
            if (!HasLocationPermissions())
            {
                Log.Error("LocationService", "Location permissions not granted");
                System.Diagnostics.Debug.WriteLine("LocationService: Location permissions not granted");
                ShowPermissionDeniedNotification();
                return;
            }

            if (locationManager.IsProviderEnabled(LocationManager.GpsProvider))
            {
                locationListener = new LocationListener(this);
                locationManager.RequestLocationUpdates(LocationManager.GpsProvider, 2000, 5, locationListener);
                Log.Debug("LocationService", "Requested location updates with GPS provider");
                System.Diagnostics.Debug.WriteLine("LocationService: Requested GPS updates");
            }
            else if (locationManager.IsProviderEnabled(LocationManager.NetworkProvider))
            {
                locationListener = new LocationListener(this);
                locationManager.RequestLocationUpdates(LocationManager.NetworkProvider, 2000, 5, locationListener);
                Log.Debug("LocationService", "Requested location updates with network provider");
                System.Diagnostics.Debug.WriteLine("LocationService: Requested Network updates");
            }
            else
            {
                Log.Error("LocationService", "Both GPS and Network providers are disabled");
                System.Diagnostics.Debug.WriteLine("LocationService: Both GPS and Network providers disabled");
                PromptEnableLocation();
                return;
            }

            notification = CreateNotification();
            if (notification == null)
            {
                Log.Error("LocationService", "Failed to create notification");
            }
        }
        catch (Exception ex)
        {
            Log.Error("LocationService", $"OnCreate error: {ex.Message}\n{ex.StackTrace}");
            System.Diagnostics.Debug.WriteLine("LocationService: Failed to create notification");
        }
    }

    private bool HasLocationPermissions()
    {
        bool fineLocationGranted = ActivityCompat.CheckSelfPermission(this, "android.permission.ACCESS_FINE_LOCATION") == Permission.Granted;
        bool coarseLocationGranted = ActivityCompat.CheckSelfPermission(this, "android.permission.ACCESS_COARSE_LOCATION") == Permission.Granted;
        bool backgroundLocationGranted = Build.VERSION.SdkInt < BuildVersionCodes.Q || ActivityCompat.CheckSelfPermission(this, "android.permission.ACCESS_BACKGROUND_LOCATION") == Permission.Granted;
        Log.Debug("LocationService", $"Permissions - Fine: {fineLocationGranted}, Coarse: {coarseLocationGranted}, Background: {backgroundLocationGranted}");
        return fineLocationGranted || coarseLocationGranted;
    }

    private void ShowPermissionDeniedNotification()
    {
        try
        {
            var channelId = "LocationServiceChannel";
            var notificationManager = GetSystemService(NotificationService) as NotificationManager;
            var intent = new Intent(this, typeof(MainActivity));
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            var pendingIntent = PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            var notification = new NotificationCompat.Builder(this, channelId)
                .SetContentTitle("Location Permission Denied")
                .SetContentText("Please grant location permissions to enable monitoring.")
                .SetSmallIcon(Android.Resource.Drawable.IcDialogAlert)
                .SetPriority((int)NotificationPriority.High)
                .SetContentIntent(pendingIntent)
                .SetAutoCancel(true)
                .Build();

            notificationManager.Notify(3, notification);
            Log.Debug("LocationService", "Permission denied notification shown");
        }
        catch (Exception ex)
        {
            Log.Error("LocationService", $"Permission denied notification error: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void PromptEnableLocation()
    {
        try
        {
            var intent = new Intent(Android.Provider.Settings.ActionLocationSourceSettings);
            intent.AddFlags(ActivityFlags.NewTask);
            StartActivity(intent);
            Log.Debug("LocationService", "Prompted user to enable location services");
        }
        catch (Exception ex)
        {
            Log.Error("LocationService", $"Location enable prompt error: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
    {
        Log.Debug("LocationService", "OnStartCommand called");
        System.Diagnostics.Debug.WriteLine("LocationService: OnStartCommand called");
        try
        {
            if (notification == null)
            {
                Log.Warn("LocationService", "Notification is null, recreating");
                System.Diagnostics.Debug.WriteLine("LocationService: Notification is null, recreating");
                notification = CreateNotification();
            }

            if (notification == null)
            {
                Log.Error("LocationService", "Failed to create notification, cannot start foreground service");
                System.Diagnostics.Debug.WriteLine("LocationService: Failed to create notification");
                return StartCommandResult.Sticky;
            }

            StartForeground(1, notification);
            Log.Debug("LocationService", "Foreground service started");
            System.Diagnostics.Debug.WriteLine("LocationService: Foreground service started");
            return StartCommandResult.Sticky;
        }
        catch (Exception ex)
        {
            Log.Error("LocationService", $"OnStartCommand error: {ex.Message}\n{ex.StackTrace}");
            System.Diagnostics.Debug.WriteLine($"LocationService: OnStartCommand error: {ex.Message}");
            return StartCommandResult.Sticky;
        }
    }

    private Notification CreateNotification()
    {
        try
        {
            var channelId = "LocationServiceChannel";
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(channelId, "Location Service", NotificationImportance.Low);
                var notificationManager = GetSystemService(NotificationService) as NotificationManager;
                if (notificationManager == null)
                {
                    Log.Error("LocationService", "NotificationManager is null");
                    return null;
                }
                notificationManager.CreateNotificationChannel(channel);
            }

            var builder = new NotificationCompat.Builder(this, channelId)
                .SetContentTitle("Location Service")
                .SetContentText("Tracking location")
                .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
                .SetPriority((int)NotificationPriority.Low);

            return builder.Build();
        }
        catch (Exception ex)
        {
            Log.Error("LocationService", $"Notification creation failed: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    public override IBinder OnBind(Intent intent)
    {
        return null;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (locationManager != null && locationListener != null)
        {
            locationManager.RemoveUpdates(locationListener);
            Log.Debug("LocationService", "Removed location updates");
        }
    }

    private class LocationListener : Java.Lang.Object, ILocationListener
    {
        private readonly LocationService service;

        public LocationListener(LocationService service)
        {
            this.service = service;
            Log.Debug("LocationService", "LocationListener created");
        }

        public void OnLocationChanged(Location location)
        {
            Log.Info("DMMSAlerts", $"Entering OnLocationChanged, Time={DateTime.Now:HH:mm:ss}");
            System.Diagnostics.Debug.WriteLine($"LocationService: Entering OnLocationChanged, Time={DateTime.Now:HH:mm:ss}");           
            try
            {
                if (location == null)
                {
                    Log.Error("LocationService", "Received null location");
                    System.Diagnostics.Debug.WriteLine("LocationService: Received null location");
                    return;
                }

                // Log memory usage
                var runtime = Java.Lang.Runtime.GetRuntime();
                var usedMemory = (runtime.TotalMemory() - runtime.FreeMemory()) / (1024 * 1024);
                Log.Debug("LocationService", $"Location updated: Lat={location.Latitude}, Lon={location.Longitude}, Time={DateTime.Now:HH:mm:ss}, MemoryUsed={usedMemory}MB");
                System.Diagnostics.Debug.WriteLine($"LocationService: Location updated: Lat={location.Latitude}, Lon={location.Longitude}, Speed={location.Speed}, Course={location.Bearing}, Time={DateTime.Now:HH:mm:ss}");

                WeakReferenceMessenger.Default.Send(new LocationMessage(location, DateTime.Now));
                Log.Debug("LocationService", "Sent LocationMessage");

                if (location.HasSpeed)
                {
                    float speedKnots = location.Speed * METERS_PER_SECOND_TO_KNOTS;
                    if (speedKnots < DMMS_THRESHOLD)
                    {
                        string alertMessage = $"Acceleration plateau detected at {speedKnots:F1} knots, below DMMS {DMMS_THRESHOLD:F1}";
                        Log.Debug("LocationService", $"Triggering alert: {alertMessage}");
                        System.Diagnostics.Debug.WriteLine($"LocationService: Triggering alert: {alertMessage}");
                        service.TriggerAlert(alertMessage);
                    }
                }
                else
                {
                    Log.Debug("LocationService", "Location has no speed data");
                    System.Diagnostics.Debug.WriteLine("LocationService: Location has no speed data");

                }
            }
            catch (Exception ex)
            {
                Log.Error("LocationService", $"OnLocationChanged error: {ex.Message}\n{ex.StackTrace}\nInnerException: {(ex.InnerException?.Message ?? "None")}");
                System.Diagnostics.Debug.WriteLine($"LocationService: OnLocationChanged error: {ex.Message}");

            }
        }

        public void OnProviderDisabled(string provider)
        {
            Log.Error("LocationService", $"Provider disabled: {provider}");
            service.PromptEnableLocation();
        }

        public void OnProviderEnabled(string provider)
        {
            Log.Debug("LocationService", $"Provider enabled: {provider}");
        }

        public void OnStatusChanged(string provider, Availability status, Bundle extras)
        {
            Log.Debug("LocationService", $"Status changed: {provider}, {status}");
        }
    }

    private void TriggerAlert(string alertMessage)
    {
        try
        {
            if (DateTime.Now - lastAlertTime < alertThrottleInterval)
            {
                Log.Debug("LocationService", $"Alert throttled: {alertMessage}");
                return;
            }
            lastAlertTime = DateTime.Now;

            // Run notification on background thread
            Task.Run(() =>
            {
                try
                {
                    var channelId = "LocationServiceChannel";
                    var notificationManager = GetSystemService(Context.NotificationService) as NotificationManager;
                    var intent = new Intent(this, typeof(MainActivity));
                    intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
                    intent.PutExtra("AlertMessage", alertMessage);
                    var pendingIntent = PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

                    var notification = new NotificationCompat.Builder(this, channelId)
                        .SetContentTitle("DMMS Alert")
                        .SetContentText(alertMessage)
                        .SetSmallIcon(Android.Resource.Drawable.IcDialogAlert)
                        .SetPriority((int)NotificationPriority.High)
                        .SetContentIntent(pendingIntent)
                        .SetAutoCancel(true)
                        .Build();

                    notificationManager.Notify(2, notification);
                    Log.Debug("LocationService", $"Alert notification sent: {alertMessage}");
                }
                catch (Exception ex)
                {
                    Log.Error("LocationService", $"TriggerAlert error: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error("LocationService", $"TriggerAlert error: {ex.Message}\n{ex.StackTrace}");
        }
    }
}

public class LocationMessage
{
    public Location Location { get; }
    public DateTime UpdateTime { get; }

    public LocationMessage(Location location, DateTime updateTime)
    {

        Location = location;
        UpdateTime = updateTime;
    }
}