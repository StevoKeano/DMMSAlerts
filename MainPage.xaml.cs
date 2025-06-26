using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.Util;
using AviationApp.Services;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AviationApp;


public enum ButtonState { Active, Paused, Failed, PermissionRequired}
[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class MainPage : ContentPage, INotifyPropertyChanged
{
    // count sux.  It's 0 if counterbtn
    // unclicked, >0 if clicked. Counts clicks; but, pause sets it back to 0 I 
    private int count = 0;
    private string latitudeText = "Latitude: N/A";
    private string longitudeText = "Longitude: N/A";
    private string altitudeText = "Altitude: N/A";
    private string speedText = "Speed: N/A";
    private string lastUpdateText = "Last Update: N/A";    
    private string warningLabelText;
    private bool isActive = false;
    private ButtonState buttonState = ButtonState.PermissionRequired;
    private bool isFlashing = false;
    private bool showSkullWarning = true;
    private bool showSkull;
    private bool airportCallOuts = true;
    private string windCorrection = "Note: Stall alerts use GPS groundspeed, assuming calm winds";
    private Color pageBackground = Colors.Transparent;
    private CancellationTokenSource flashingCts = null;
    private CancellationTokenSource ttsCts = null;
    private Task _flashingTask = null;
    private Task _ttsTask = null;
    private readonly object _flashLock = new object();
    private DateTime _lastFlashUpdate = DateTime.MinValue;
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(100);
    private int _originalMediaVolume = -1;
    private string ttsAlertText;
    private float messageFrequency;
    private bool autoActivateMonitoring;
    private bool suppressWarningsUntilAboveDmms;
    private string closestAirportText = "Closest Airport: N/A";
    private int closestAirportElev = 0;
    private List<Airport> airports;
    // New: Track airspeed history for acceleration
    private readonly List<(DateTime Time, float SpeedKnots)> airspeedHistory = new List<(DateTime, float)>();
    private const double AccelerationThreshold = 0.5; // knots/s
    private const double MinAlertSpeed = 30; // knots
    private const double AirportProximityKm = .9; // km
    private const double AccelerationWindowSeconds = 2; // seconds
    private readonly IPlatformService _platformService;
    private bool _isMonitoringStarted = false;
    private readonly LocationService _locationService;
    private readonly float kmtomiles = 0.621371f;
    private bool disableAlerts; // Add field for alert suppression
    private string lastStationID = "N/A";
    private float ttsAlertVolume;
    private CancellationTokenSource ttsTestCts;
    private Task ttsTestTask;
    private readonly object TtsTestLock = new object();
    private DateTime lastVolumeChange = DateTime.MinValue;
    private readonly TimeSpan ttsTestDuration = TimeSpan.FromSeconds(5);
    private double _zeroGStallSpeed;
    private float _lastX, _lastY, _lastZ; // Store last accelerometer readings
    private Microsoft.Maui.Devices.Sensors.Location lastLocation; // Last known location
    private float windAdjustment = 0f; // Wind effect in knots (ManualIAS - GPS speed)
    private double referenceHeading = 0.0; // Heading when ManualIAS was set (degrees)
    private bool isWindAdjustmentSet = false; // Flag to enable wind adjustment
                                              // Picker for DMMS 
    //public ObservableCollection<string> SpeedRange { get; set; }
    public float TTSAlertVolume
    {
        get => ttsAlertVolume;
        set
        {
            // Round to nearest 0.1 before setting
            float roundedValue = (float)Math.Round(value / 0.1f) * 0.1f;
            roundedValue = Math.Max(0.5f, Math.Min(1.0f, roundedValue));
            if (Math.Abs(ttsAlertVolume - roundedValue) > 0.01f)
            {
                ttsAlertVolume = roundedValue;
                OnPropertyChanged();
                Preferences.Set("TTSAlertVolume", ttsAlertVolume);
                Log.Debug("MainPage", $"Saved TTSAlertVolume to Preferences: {ttsAlertVolume}");
                TriggerTTSVolumeTest();
            }
        }
    }
    public string LatitudeText
    {
        get => latitudeText;
        set { latitudeText = value; OnPropertyChanged(); }
    }

    public string LongitudeText
    {
        get => longitudeText;
        set { longitudeText = value; OnPropertyChanged(); }
    }

    public string AltitudeText
    {
        get => altitudeText;
        set { altitudeText = value; OnPropertyChanged(); }
    }

    public string SpeedText
    {
        get => speedText;
        set { speedText = value; OnPropertyChanged(); }
    }

    public string LastUpdateText
    {
        get => lastUpdateText;
        set { lastUpdateText = value; OnPropertyChanged(); }
    }
    private string _dmmsText;
    public string DmmsText
    {
        get => _dmmsText;
        set
        {
            if (_dmmsText != value)
            {
                _dmmsText = value;
                Preferences.Set("DmmsValue", value);
                OnPropertyChanged(nameof(DmmsText));
                if (double.TryParse(_dmmsText, out double parsedValue))
                {
                    _zeroGStallSpeed = parsedValue;
                    System.Diagnostics.Debug.WriteLine($"MainPage: Updated _zeroGStallSpeed to {parsedValue:F0} knots");
                    double gForce = CalculateGForceFromLastReading();
                    double adjustedStallSpeed = AdjustStallSpeed(gForce);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        StallSpeedLabel.Text = $"G-Adjusted Stall Speed: {adjustedStallSpeed:F0} knots";
                        System.Diagnostics.Debug.WriteLine($"MainPage:DmmsText.set() Updated StallSpeedLabel to {adjustedStallSpeed:F0} knots, G-Force: {gForce:F2}");
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"MainPage: Failed to parse DmmsText '{value}' to double");
                }
            }
        }
    }

    public ObservableCollection<string> SpeedRange { get; private set; }

    //public string DmmsText
    //{
    //    get => dmmsText;
    //    set
    //    {
    //        if (dmmsText != value)
    //        {
    //            dmmsText = value;
    //            OnPropertyChanged();
    //            Preferences.Set("DmmsValue", value);
    //            Log.Debug("MainPage", $"Saved DmmsText to Preferences: {value}");
    //            if (double.TryParse(dmmsText, out double parsedValue))
    //            {
    //                _zeroGStallSpeed = parsedValue;
    //                System.Diagnostics.Debug.WriteLine($"MainPage: Updated _zeroGStallSpeed to {parsedValue:F0} knots");
    //                // Force update StallSpeedLabel
    //                double gForce = CalculateGForceFromLastReading();
    //                double adjustedStallSpeed = AdjustStallSpeed(gForce);
    //                MainThread.BeginInvokeOnMainThread(() =>
    //                {
    //                    StallSpeedLabel.Text = $"G-Adjusted Stall Speed: {adjustedStallSpeed:F0} knots";
    //                    System.Diagnostics.Debug.WriteLine($"MainPage:DmmsText.set() Updated StallSpeedLabel to {adjustedStallSpeed:F0} knots, G-Force: {gForce:F2}");
    //                });
    //            }
    //            else
    //            {
    //                System.Diagnostics.Debug.WriteLine($"MainPage: Failed to parse DmmsText '{value}' to double");
    //            }
    //        }
    //    }
    //}

    public string WarningLabelText
    {
        get => warningLabelText;
        set { warningLabelText = value; OnPropertyChanged(); }
    }

    public bool IsActive
    {
        get => isActive;
        set { isActive = value; OnPropertyChanged(); }
    }

    public ButtonState ButtonState
    {
        get => buttonState;
        set 
        { 
            buttonState = value; 
            OnPropertyChanged();
            System.Diagnostics.Debug.WriteLine($"MainPage: CounterBtn state set to {value}");
        }
    }

    public Color PageBackground
    {
        get => pageBackground;
        set { pageBackground = value; OnPropertyChanged(); }
    }

    public bool IsFlashing
    {
        get => isFlashing;
        set
        {
            isFlashing = value;
            OnPropertyChanged();
            UpdateSkullVisibility();
            Log.Debug("MainPage", $"IsFlashing set to: {isFlashing}");
        }
    }

    public bool ShowSkullWarning
    {
        get => showSkullWarning;
        set
        {
            showSkullWarning = value;
            OnPropertyChanged();
            Log.Debug("MainPage", $"ShowSkullWarning set to: {showSkullWarning}");
        }
    }

    public string ClosestAirportText
    {
        get => closestAirportText;
        set { closestAirportText = value; OnPropertyChanged(); }
    }

    public bool DisableAlerts
    {
        get => disableAlerts;
        set { disableAlerts = value; OnPropertyChanged(); }
    }

    public bool AirportCallOuts 
    { 
        get => airportCallOuts; 
        set { airportCallOuts = value; OnPropertyChanged(); } 
    }

    private class Airport
    {
        public string StationId { get; set; }       
        public int Elev { get; set; }
        public string Site { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    private void UpdateSkullVisibility()
    {
        ShowSkullWarning = IsFlashing && showSkull;
        Log.Debug("MainPage", $"UpdateSkullVisibility - IsFlashing: {IsFlashing}, ShowSkull: {showSkull}, ShowSkullWarning: {ShowSkullWarning}");
    }
    private async Task<bool> AreLocationPermissionsGrantedAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationAlways>();
        return status == PermissionStatus.Granted;
    }

    public bool IsUsingGpsSpeed => Preferences.Get("ManualIAS", 0f) == 0;
    public bool IsUsingIASSpeed => !(Preferences.Get("ManualIAS", 0f) == 0);
    private bool useMetarWind = false; // Already exists
    public bool IsUsingMetarWind
    {
        get => useMetarWind;
        set
        {
            useMetarWind = value;
            OnPropertyChanged();
            System.Diagnostics.Debug.WriteLine($"MainPage: IsUsingMetarWind set to {value}");
        }
    }
    private string speedSource = "Gps"; // New property to track speed source
    public string SpeedSource
    {
        get => speedSource;
        set
        {
            speedSource = value;
            OnPropertyChanged();
            System.Diagnostics.Debug.WriteLine($"MainPage: SpeedSource set to {value}");
        }
    }
    public string WindCorrection
    {
        get => windCorrection;
        set { windCorrection = value; OnPropertyChanged(); }
    }
    // Initialize METAR-based wind adjustment
    private float metarWindSpeed = 0f;
    private float metarWindDirection = 0f;
    private float adjustedSpeedKnots;
    private float windComponent = 0f;
    private string metarAirportID = "N/A";
    public MainPage(IPlatformService platformService, LocationService locationService)
    {
        // Load settings at startup
        // Initialize platform services and settings
        _platformService = platformService;
        _locationService = locationService;
        SpeedRange = new ObservableCollection<string>(Enumerable.Range(1, 270).Select(i => i.ToString()));
        LoadSettings(); // Safe settings load 
        // Fix: Use _dmmsText instead of dmmsText
        double.TryParse(_dmmsText, out _zeroGStallSpeed);
     
        airportCallOuts = Preferences.Get("AirportCallOuts", true);
        warningLabelText = Preferences.Get("WarningLabelText", "< DMMS Alerter <");
        ttsAlertText = Preferences.Get("TtsAlertText", "SPEED CHECK, YOUR GONNA FALL OUTTA THE SKY LIKE UH PIANO");
        messageFrequency = Preferences.Get("MessageFrequency", 5f);
        showSkull = Preferences.Get("ShowSkull", true);
        autoActivateMonitoring = Preferences.Get("AutoActivateMonitoring", true);
        showSkullWarning = false;
        suppressWarningsUntilAboveDmms = true;
        airports = new List<Airport>();
        Log.Debug("MainPage", $"Loaded settings at startup - AirportCallOuts:{airportCallOuts}, WarningLabelText: {warningLabelText}, TtsAlertText: {ttsAlertText}, MessageFrequency: {messageFrequency}, ShowSkull: {showSkull}, AutoActivateMonitoring: {autoActivateMonitoring}, ShowSkullWarning: {showSkullWarning}, SuppressWarnings: {suppressWarningsUntilAboveDmms}");
        System.Diagnostics.Debug.WriteLine($"ShowSkullWarning initial value: {ShowSkullWarning}");
        BindingContext = this;
        ttsAlertVolume = Preferences.Get("TTSAlertVolume", 1.0f); // Default to full volume
        // Initialize data before UI rendering
        InitializeComponent();
        // New: Initialize SpeedRange and DmmsText after InitializeComponent
        SpeedRange = new ObservableCollection<string>(Enumerable.Range(1, 270).Select(i => i.ToString()));
        var prefValue = Preferences.Get("DmmsValue", "70");
        System.Diagnostics.Debug.WriteLine($"MainPage: Loaded DmmsValue from Preferences: '{prefValue}'");
        _dmmsText = string.IsNullOrEmpty(prefValue) || !SpeedRange.Contains(prefValue) ? "70" : prefValue;
        Preferences.Set("DmmsValue", _dmmsText);
        double.TryParse(_dmmsText, out _zeroGStallSpeed);
        LoadSettings(); // Safe settings load
        Preferences.Set("ManualIAS", 0f); // Reset ManualIAS to 0 at startup

        // Force Picker refresh
        var picker = this.FindByName<Picker>("DmmsPicker");
        picker.SelectedItem = _dmmsText; // Ensure selection is set                                          // Debug initial state
        System.Diagnostics.Debug.WriteLine($"DmmsText: {_dmmsText}, SpeedRange[69]: {SpeedRange[69]}");

        _platformService = platformService;
        _locationService = locationService;
        // Register for ManualIAS changes
        WeakReferenceMessenger.Default.Register<ManualIASChangedMessage>(this, (recipient, message) =>
        {
            float manualIAS = message.ManualIAS;
            if (manualIAS > 0 && lastLocation != null)
            {
                float gpsSpeedKnots = (float)(lastLocation.Speed.GetValueOrDefault() / 0.514444);
                windAdjustment = manualIAS - gpsSpeedKnots;
                referenceHeading = lastLocation.Course ?? 0;
                isWindAdjustmentSet = true;
                System.Diagnostics.Debug.WriteLine($"MainPage: Wind adjustment set: {windAdjustment:F1} knots at heading {referenceHeading:F0}°");
            }
            else
            {
                isWindAdjustmentSet = false;
                System.Diagnostics.Debug.WriteLine("MainPage: ManualIAS update ignored: Invalid ManualIAS or no location data");
            }
            OnPropertyChanged(nameof(IsUsingGpsSpeed)); // Update UI
            OnPropertyChanged(nameof(IsUsingIASSpeed)); // Update UI
            System.Diagnostics.Debug.WriteLine($"MainPage: IsUsingGpsSpeed = {IsUsingGpsSpeed}IsUsingIASSpeed = {IsUsingIASSpeed}");
        });

        // Start accelerometer
        if (Accelerometer.IsSupported)
        {
            Accelerometer.ReadingChanged += OnAccelerometerReadingChanged;
            Accelerometer.Start(SensorSpeed.UI);
        }

        // Initialize based on settings (AutoActivateMonitoring: True from log)
        _isMonitoringStarted = autoActivateMonitoring; // Respect AutoActivateMonitoring
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await UpdateCounterBtnState();
            if (!await AreLocationPermissionsGrantedAsync())
            {
                System.Diagnostics.Debug.WriteLine("Showing permission popup on startup");
                await _platformService.ShowPermissionPopupAndRequestAsync();
                await UpdateCounterBtnState();
            }
            else if (autoActivateMonitoring && !IsActive)
            {
                await StartLocationService();
                //  StartLocation service sets: IsActive = true;
                //                              ButtonState = ButtonState.Active;                
                // XAML DataTrigger sets:
                //                       CounterBtnBorder.Background = (Brush)Resources["ActiveGradient"];
                count = 1;
                await UpdateCounterBtnState();
            }
        });

        OnPropertyChanged(nameof(DmmsText));
        OnPropertyChanged(nameof(WarningLabelText));
        OnPropertyChanged(nameof(ShowSkullWarning));
        OnPropertyChanged(nameof(ClosestAirportText));
        OnPropertyChanged(nameof(AirportCallOuts));
        OnPropertyChanged(nameof(IsUsingGpsSpeed));
        OnPropertyChanged(nameof(IsUsingIASSpeed));
        OnPropertyChanged(nameof(WindCorrection));


        // Load airports from CSV // Test TTS on startup
        Task.Run(async () => await LoadAirportsAsync());
        Task.Run(async () =>
        {
            try
            {
                await TextToSpeech.Default.SpeakAsync("TTS Volumn Testing 1, 2, 3", new SpeechOptions { Volume = TTSAlertVolume }, CancellationToken.None);
                Log.Debug("MainPage", "Startup TTS test played successfully");
            }
            catch (Exception ex)
            {
                Log.Error("MainPage", $"Startup TTS test failed: {ex.Message}\n{ex.StackTrace}");
            }
        });
        System.Diagnostics.Debug.WriteLine("MainPage: Registering LocationMessage handler");
        WeakReferenceMessenger.Default.Register<LocationMessage>(this, async (recipient, message) =>
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"LocationMessage: Handler started, Message Time={message.UpdateTime:HH:mm:ss}");
                if (DateTime.Now - _lastFlashUpdate < _debounceInterval)
                {
                    System.Diagnostics.Debug.WriteLine("LocationMessage: Skipped due to debounce");
                    return;
                }
                _lastFlashUpdate = DateTime.Now;

                Log.Debug("MainPage", $"Received LocationMessage at {DateTime.Now:HH:mm:ss}");
                var androidLocation = message.Location;
                var updateTime = message.UpdateTime;
                if (androidLocation == null)
                {
                    Log.Error("MainPage", "Received null location");
                    System.Diagnostics.Debug.WriteLine("LocationMessage: Null location received");
                    return;
                }

                var location = new Microsoft.Maui.Devices.Sensors.Location
                {
                    Latitude = androidLocation.Latitude,
                    Longitude = androidLocation.Longitude,
                    Altitude = androidLocation.HasAltitude ? androidLocation.Altitude : null,
                    Speed = androidLocation.HasSpeed ? androidLocation.Speed : null,
                    Course = androidLocation.HasBearing ? androidLocation.Bearing : null
                };

                lastLocation = location;

                float gpsSpeedKnots = (float)(location.Speed.GetValueOrDefault() / 0.514444);
                double currentHeading = location.Course ?? 0;
                //float adjustedSpeedKnots;
                //float windComponent = 0f;

                //// Initialize METAR-based wind adjustment
                //float metarWindSpeed = 0f;
                //float metarWindDirection = 0f;
                bool isNearAirport = false; // Moved declaration
                Airport closestAirport = null; // Moved declaration

                // Airport callout and METAR fetch
                System.Diagnostics.Debug.WriteLine($"LocationMessage: Airports count = {airports.Count}");
                if (airports.Any())
                {
                    closestAirport = FindClosestAirport(location.Latitude, location.Longitude);
                    int heading = await UpdateMagneticHeadingAsync(location.Latitude, location.Longitude, closestAirport.Latitude, closestAirport.Longitude);
                    double distanceKm = CalculateDistance(location.Latitude, location.Longitude, closestAirport.Latitude, closestAirport.Longitude);
                    ClosestAirportText = $"Closest Airport: {closestAirport.StationId}, {(int)Math.Round(distanceKm * kmtomiles)} miles bearing {heading}° {closestAirport.Elev} feet";
                    isNearAirport = distanceKm <= AirportProximityKm;
                    Log.Debug("MainPage", $"Closest airport: {closestAirport.StationId} Elev: {closestAirport.Elev:F0}ft at {closestAirport.Latitude},{closestAirport.Longitude}, Distance: {distanceKm:F2} km");

                    if (airportCallOuts && lastStationID != closestAirport.StationId) // TTS only when you have a new airport
                    {
                        lastStationID = closestAirport.StationId;
                        Log.Info("DMMSAlerts", $"Processing new airport: {lastStationID}");
                        System.Diagnostics.Debug.WriteLine($"LocationMessage: Processing new airport: {lastStationID}");

                        try
                        {
                            await TextToSpeech.Default.SpeakAsync(
                                ClosestAirportText.Replace("Closest Airport:", ""),
                                new SpeechOptions { Volume = TTSAlertVolume });
                        }
                        catch (Exception ex)
                        {
                            Log.Error("DMMSAlerts", $"TextToSpeech Error: {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"TextToSpeech Error: {ex.Message}");
                        }
                    }
                        // Check if METAR data is stale (older than 5 minutes)
                        bool isMetarStale = true;
                        if (Preferences.ContainsKey($"LastMetarFetch_{lastStationID}"))
                        {
                            string lastFetchTimeStr = Preferences.Get($"LastMetarFetch_{lastStationID}", null);
                            if (DateTime.TryParse(lastFetchTimeStr, out DateTime lastFetchTime))
                            {
                                TimeSpan timeSinceLastFetch = DateTime.Now - lastFetchTime;
                                isMetarStale = timeSinceLastFetch.TotalMinutes > 5; // 5-minute threshold
                                if (isMetarStale)
                                {
                                    Log.Info("DMMSAlerts", $"METAR data for {lastStationID} is stale (last fetched {timeSinceLastFetch.TotalMinutes:F1} minutes ago), refetching");
                                    System.Diagnostics.Debug.WriteLine($"LocationMessage: METAR data for {lastStationID} is stale (last fetched {timeSinceLastFetch.TotalMinutes:F1} minutes ago), refetching");
                                }
                            }
                        }

                        // Fetch METAR data if no previous fetch or data is stale
                        List<MetarData> metars = new List<MetarData>();
                        if (!Preferences.ContainsKey($"LastMetarFetch_{lastStationID}") || isMetarStale)
                        {

                            // get metar from closest airport if not available, falls to getwinddate(location) by lat lon bbox
                            var response = await GetWindDataByID(lastStationID);
                            Log.Info("DMMSAlerts", $"GetWindDataByID response for {lastStationID}: {(string.IsNullOrEmpty(response) ? "Empty" : "Received")}");
                            System.Diagnostics.Debug.WriteLine($"LocationMessage: GetWindDataByID response for {lastStationID}: {(string.IsNullOrEmpty(response) ? "Empty" : "Received")}");

                            if (response != string.Empty)
                            {
                                try
                                {
                                    metars = JsonSerializer.Deserialize<List<MetarData>>(response);
                                    Log.Info("DMMSAlerts", $"Deserialized {metars?.Count ?? 0} METARs for {lastStationID}");
                                    System.Diagnostics.Debug.WriteLine($"LocationMessage: Deserialized {metars?.Count ?? 0} METARs for {lastStationID}");
                                }
                                catch (Exception ex)
                                {
                                    Log.Error("DMMSAlerts", $"METAR deserialization error for {lastStationID}: {ex.Message}");
                                    System.Diagnostics.Debug.WriteLine($"LocationMessage: METAR deserialization error for {lastStationID}: {ex.Message}");
                                }
                            }

                        MetarData validMetar = null;
                        // New: Select nearest METAR if multiple received
                        if (metars != null && metars.Any(m => !string.IsNullOrEmpty(m.Wdir) && int.TryParse(m.Wdir, out var wdir) && wdir >= 0 && wdir <= 360 && m.Wspd.HasValue && DateTime.TryParse(m.ReceiptTime, out var time) && (DateTime.UtcNow - time).TotalMinutes <= 60))
                        {
                            var validMetars = metars
                                .Where(m => !string.IsNullOrEmpty(m.Wdir) && int.TryParse(m.Wdir, out var wdir) && wdir >= 0 && wdir <= 360 && m.Wspd.HasValue && DateTime.TryParse(m.ReceiptTime, out var time) && (DateTime.UtcNow - time).TotalMinutes <= 60)
                                                        .ToList();
                            var airportCoords = airports.ToDictionary(a => a.StationId, a => (a.Latitude, a.Longitude));
                            validMetar = validMetars
                                                        .Where(m => airportCoords.ContainsKey(m.IcaoId))
                                                        .OrderBy(m => CalculateDistance(location.Latitude, location.Longitude, airportCoords[m.IcaoId].Latitude, airportCoords[m.IcaoId].Longitude))
                                                        .FirstOrDefault();
                        }
                        // Fallback to any valid METAR if none within 60 minutes
                        if (validMetar == null && metars != null && metars.Any(m => !string.IsNullOrEmpty(m.Wdir) && int.TryParse(m.Wdir, out var wdir) && wdir >= 0 && wdir <= 360 && m.Wspd.HasValue))
                        {
                               validMetar = metars
                                    .Where(m => !string.IsNullOrEmpty(m.Wdir) && int.TryParse(m.Wdir, out var wdir) && wdir >= 0 && wdir <= 360 && m.Wspd.HasValue)
                                    .OrderByDescending(m => DateTime.TryParse(m.ReceiptTime, out var time) ? time : DateTime.MinValue)
                                    .FirstOrDefault();
                                if (validMetar != null)
                                {
                                    int.TryParse(validMetar.Wdir, out var wdir);
                                    metarWindDirection = wdir;
                                    metarWindSpeed = validMetar.Wspd.Value + (validMetar.Wgst.HasValue ? validMetar.Wgst.Value / 2 : 0);
                                    metarAirportID = validMetar.IcaoId;
                                    IsUsingMetarWind = true; // Updated to use property
                                    Log.Info("DMMSAlerts", $"METAR Wind: Direction={metarWindDirection}°, Speed={metarWindSpeed}kt, Gust={validMetar.Wgst}kt");
                                    System.Diagnostics.Debug.WriteLine($"METAR Wind: Direction={metarWindDirection}°, Speed={metarWindSpeed}kt, Gust={validMetar.Wgst}kt");

                                    // Update the fetch timestamp only on successful fetch
                                    Preferences.Set($"LastMetarFetch_{lastStationID}", DateTime.Now.ToString("o"));
                                }
                                else
                                {
                                    Log.Warn("DMMSAlerts", $"No valid METAR data for {lastStationID}");
                                    System.Diagnostics.Debug.WriteLine($"LocationMessage: No valid METAR data for {lastStationID}");
                                    // Reset METAR usage if no valid data
                                    IsUsingMetarWind = false;
                                }
                            }
                            else
                            {
                                Log.Warn("DMMSAlerts", $"No METARs or invalid data for {lastStationID}");
                                System.Diagnostics.Debug.WriteLine($"LocationMessage: No METARs or invalid data for {lastStationID}");

                                //// Fetch METAR data if no previous fetch or data is stale
                                // Get METAR for a bound box bbox lat log's
                                response = await GetWindData(location);
                                System.Diagnostics.Debug.WriteLine($"LocationMessage: GetWindData response: {(string.IsNullOrEmpty(response) ? "Empty" : "Received")}");
                                Log.Info("DMMSAlerts", $"GetWindData response: {(string.IsNullOrEmpty(response) ? "Empty" : "Received")}");

                                if (!string.IsNullOrEmpty(response))
                                {
                                    try
                                    {
                                        metars = JsonSerializer.Deserialize<List<MetarData>>(response);
                                        Log.Info("DMMSAlerts", $"Deserialized {metars?.Count ?? 0} METARs from bbox");
                                        System.Diagnostics.Debug.WriteLine($"LocationMessage: Deserialized {metars?.Count ?? 0} METARs from bbox");
                                        Preferences.Set($"LastMetarFetch_{lastStationID}", DateTime.Now.ToString("o"));
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error("DMMSAlerts", $"METAR deserialization error from bbox: {ex.Message}");
                                        System.Diagnostics.Debug.WriteLine($"LocationMessage: METAR deserialization error from bbox: {ex.Message}");
                                    }
                                validMetar = null;
                                // New: Select nearest METAR if multiple received
                                if (metars != null && metars.Any(m => !string.IsNullOrEmpty(m.Wdir) && int.TryParse(m.Wdir, out var wdir) && wdir >= 0 && wdir <= 360 && m.Wspd.HasValue && DateTime.TryParse(m.ReceiptTime, out var time) && (DateTime.UtcNow - time).TotalMinutes <= 60))
                                {
                                    var validMetars = metars
                                             .Where(m => !string.IsNullOrEmpty(m.Wdir) && int.TryParse(m.Wdir, out var wdir) && wdir >= 0 && wdir <= 360 && m.Wspd.HasValue && DateTime.TryParse(m.ReceiptTime, out var time) && (DateTime.UtcNow - time).TotalMinutes <= 60)
                                            .ToList();
                                    var airportCoords = airports.ToDictionary(a => a.StationId, a => (a.Latitude, a.Longitude));
                                    validMetar = validMetars
                                            .Where(m => airportCoords.ContainsKey(m.IcaoId))
                                            .OrderBy(m => CalculateDistance(location.Latitude, location.Longitude, airportCoords[m.IcaoId].Latitude, airportCoords[m.IcaoId].Longitude))
                                            .FirstOrDefault();
                                    }
                                // Fallback to any valid METAR if none within 60 minutes
                                if (validMetar == null && metars != null && metars.Any(m => !string.IsNullOrEmpty(m.Wdir) && int.TryParse(m.Wdir, out var wdir) && wdir >= 0 && wdir <= 360 && m.Wspd.HasValue))

                                {
                                    validMetar = metars                                            
                                             .Where(m => !string.IsNullOrEmpty(m.Wdir) && int.TryParse(m.Wdir, out var wdir) && wdir >= 0 && wdir <= 360 && m.Wspd.HasValue)
                                            .OrderByDescending(m => DateTime.TryParse(m.ReceiptTime, out var time) ? time : DateTime.MinValue)
                                            .FirstOrDefault();
                                        if (validMetar != null)
                                        {
                                            int.TryParse(validMetar.Wdir, out var wdir);
                                            metarWindDirection = wdir;
                                            metarWindSpeed = validMetar.Wspd.Value + (validMetar.Wgst.HasValue ? validMetar.Wgst.Value/2 :  0);
                                            metarAirportID = validMetar.IcaoId;
                                            IsUsingMetarWind = true; // Updated to use property
                                            Log.Info("DMMSAlerts", $"METAR Wind (bbox): Direction={metarWindDirection}°, Speed={metarWindSpeed}kt, Gust={validMetar.Wgst}kt");
                                            System.Diagnostics.Debug.WriteLine($"METAR Wind (bbox): Direction={metarWindDirection}°, Speed={metarWindSpeed}kt, Gust={validMetar.Wgst}kt");

                                            // Update the fetch timestamp only on successful fetch
                                            Preferences.Set($"LastMetarFetch_{lastStationID}", DateTime.Now.ToString("o"));
                                        }
                                        else
                                        {
                                            Log.Warn("DMMSAlerts", $"No valid METAR data from bbox");
                                            System.Diagnostics.Debug.WriteLine($"LocationMessage: No valid METAR data from bbox");
                                            IsUsingMetarWind = false;
                                        }
                                }
                                else
                                {
                                        Log.Warn("DMMSAlerts", $"No METARs from bbox");
                                        System.Diagnostics.Debug.WriteLine($"LocationMessage: No METARs from bbox");
                                        IsUsingMetarWind = false;
                                }
                                }
                            }
                        }
                        else
                        {
                            Log.Info("DMMSAlerts", $"Using existing METAR data for {lastStationID}");
                            System.Diagnostics.Debug.WriteLine($"LocationMessage: Using existing METAR data for {lastStationID}");
                        }
                    
                    // Apply wind correction: METAR overrides ManualIAS
                    if (IsUsingMetarWind && location.Course.HasValue)
                    {
                        // Calculate wind adjustment using METAR data
                        referenceHeading = metarWindDirection; // Wind direction as reference
                        windAdjustment = metarWindSpeed; // Wind speed in knots
                        isWindAdjustmentSet = true;
                        double headingDiff = (currentHeading - referenceHeading + 360) % 360;
                        windComponent = windAdjustment * (float)Math.Cos(headingDiff * Math.PI / 180);
                        adjustedSpeedKnots = gpsSpeedKnots + windComponent;
                        WindCorrection = $"Wind {(int)Math.Round(windComponent, 0)} knot {(windComponent > 0 ? "headwind" : "tailwind")} ({metarAirportID} METAR)";
                        System.Diagnostics.Debug.WriteLine($"METAR Wind Adjustment: {windAdjustment:F1} knots at {referenceHeading:F0}°, Component: {windComponent:F1}, Adjusted Speed: {adjustedSpeedKnots:F0} knots");
                        SpeedSource = "Metar";
                    }
                    else if (IsUsingIASSpeed && location.Course.HasValue)
                    {
                        // Fallback to ManualIAS if no valid METAR
                        float manualIAS = Preferences.Get("ManualIAS", 0f);
                        windAdjustment = manualIAS - gpsSpeedKnots;
                        referenceHeading = location.Course ?? 0;
                        isWindAdjustmentSet = true;
                        double headingDiff = (currentHeading - referenceHeading + 360) % 360;
                        windComponent = windAdjustment * (float)Math.Cos(headingDiff * Math.PI / 180);
                        adjustedSpeedKnots = gpsSpeedKnots + windComponent;
                        WindCorrection = $"Wind {(int)Math.Round(windComponent, 0)} knot {(windComponent > 0 ? "headwind" : "tailwind")}";
                        System.Diagnostics.Debug.WriteLine($"ManualIAS Adjustment: {windAdjustment:F1} knots at {referenceHeading:F0}°, Component: {windComponent:F1}, Adjusted Speed: {adjustedSpeedKnots:F0} knots");
                        SpeedSource = "IAS";
                    }
                    else
                    {
                        adjustedSpeedKnots = gpsSpeedKnots;
                        isWindAdjustmentSet = false;
                        WindCorrection = "Note: Stall alerts use GPS groundspeed, assuming calm winds";
                        IsUsingMetarWind = false; // Ensure METAR wind is off
                        System.Diagnostics.Debug.WriteLine($"Using raw GPS speed: {adjustedSpeedKnots:F0} knots");
                        SpeedSource = "GPS";
                    }

                    // Update UI
                    double? altitudeFeet = location.Altitude.HasValue ? location.Altitude.Value * 3.28084 : null;
                    AltitudeText = $"Altitude: {altitudeFeet?.ToString("F0") ?? "N/A"} ft";
                    SpeedText = $"{(IsUsingMetarWind ? "IAS (METAR):" : IsUsingIASSpeed ? "IAS:" : "GPS:")} {adjustedSpeedKnots:F0} knots";
                    LastUpdateText = $"Updated: {updateTime:HH:mm:ss}";
                    OnPropertyChanged(nameof(SpeedSource));
                    OnPropertyChanged(nameof(IsUsingGpsSpeed));
                    OnPropertyChanged(nameof(IsUsingIASSpeed));
                    OnPropertyChanged(nameof(IsUsingMetarWind));
                    OnPropertyChanged(nameof(WindCorrection));
                    OnPropertyChanged(nameof(SpeedText));
                    OnPropertyChanged(nameof(AltitudeText));
                    OnPropertyChanged(nameof(LastUpdateText));
                    // Track adjusted speed for acceleration
                    airspeedHistory.Add((DateTime.Now, adjustedSpeedKnots));
                    airspeedHistory.RemoveAll(x => (DateTime.Now - x.Time).TotalSeconds > 5);

                    // Rest of your logic (acceleration, alerts, etc.) remains unchanged
                    float dmmsKnots = 0f;
                    bool isDmmsValid = IsActive && float.TryParse(DmmsText, out dmmsKnots) && dmmsKnots > 0;
                    double gForce = CalculateGForceFromLastReading();
                    double adjustedStallSpeed = AdjustStallSpeed(gForce);
                    if (isDmmsValid && adjustedSpeedKnots > adjustedStallSpeed && suppressWarningsUntilAboveDmms)
                    {
                        suppressWarningsUntilAboveDmms = false;
                        Log.Debug("MainPage", "Speed exceeded adjusted stall speed, enabling normal alerts");
                    }
                    if (airports.Any() && altitudeFeet.HasValue && closestAirport != null)
                    {
                        double altitudeDifference = altitudeFeet.Value - closestAirport.Elev;
                        DisableAlerts = altitudeDifference < 10 && isNearAirport;
                        System.Diagnostics.Debug.WriteLine($"MainPage: Altitude Difference: {altitudeDifference:F0} ft, DisableAlerts: {DisableAlerts}");
                    }
                    else
                    {
                        DisableAlerts = false;
                    }
                    bool isPlateau = false;
                    if (isDmmsValid && adjustedSpeedKnots > MinAlertSpeed && adjustedSpeedKnots < dmmsKnots)
                    {
                        isPlateau = IsAccelerationPlateau();
                        if (isPlateau)
                        {
                            Log.Debug("MainPage", $"Acceleration plateau detected at {adjustedSpeedKnots:F1} knots, below DMMS {dmmsKnots:F1}");
                        }
                    }
                    if (!DisableAlerts && isDmmsValid && adjustedSpeedKnots < adjustedStallSpeed && (isPlateau || !suppressWarningsUntilAboveDmms))
                    {
                        if (!IsFlashing)
                        {
                            IsFlashing = true;
                            ShowSkullWarning = Preferences.Get("ShowSkull", false);
                            await StartFlashingBackground();
                            BringAppToForeground();
                            System.Diagnostics.Debug.WriteLine($"MainPage: Alerts triggered: Adjusted Speed {adjustedSpeedKnots:F1} < Adjusted Stall Speed {adjustedStallSpeed:F1}, G-Force: {gForce:F2}, Plateau: {isPlateau}, DisableAlerts: {DisableAlerts}, Wind Component: {windComponent:F1}");
                        }
                    }
                    else if (IsFlashing)
                    {
                        await StopFlashingBackground();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("MainPage", $"Message handler error: {ex.Message}\n{ex.StackTrace}");
                Debug.WriteLine($"MainPage: Message handler error: {ex.Message}\n{ex.StackTrace}");
            }
        });
    }

    public static async Task<string> GetWindData(Microsoft.Maui.Devices.Sensors.Location here)
    {
        if (here == null || double.IsNaN(here.Latitude))
        {
            System.Diagnostics.Debug.WriteLine("GetWindData: Invalid location or NULL Latitude");
            return string.Empty;
        }
        double increment = 0.05; // Smaller increment for more precise bounding box
        using var client = new HttpClient();
        try
        {
            // Use ICAO code for precise airport METAR data
            var response = ""; // Initialize response to empty array
            for(double i = .25; i <2.0; i += increment)
            { 
            double lat1 = Math.Round(here.Latitude - i, 2); // Smaller box for precision
            double lat2 = Math.Round(here.Latitude + i, 2);
            double lon1 = Math.Round(here.Longitude - i, 2);
            double lon2 = Math.Round(here.Longitude + i, 2);
            System.Diagnostics.Debug.WriteLine($"https://aviationweather.gov/api/data/metar?bbox={lat1},{lon1},{lat2},{lon2}&format=json");
            response = await client.GetStringAsync($"https://aviationweather.gov/api/data/metar?bbox={lat1},{lon1},{lat2},{lon2}&format=json");
            var metars = JsonSerializer.Deserialize<List<MetarData>>(response);
                if (metars != null && metars.Any(m => !string.IsNullOrEmpty(m.Wdir) && int.TryParse(m.Wdir, out var wdir) && wdir >= 0 && wdir <= 360 && m.Wspd.HasValue))

                {
                    System.Diagnostics.Debug.WriteLine($"Wind Data for {lon1},{lat1},{lon2},{lat2}: {response}");
                    System.Diagnostics.Debug.WriteLine($"GetWindData: Found valid METAR data for {lat1},{lon1},{lat2},{lon2}");                
                    return response;  
                }
            }        
            return string.Empty; // Ensure we return an empty string if no valid data found
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetWindData Error: {ex.Message}");
            return string.Empty;
        }
    }
    public static async Task<string> GetWindDataByID(string airportCode)
    {
        if (string.IsNullOrEmpty(airportCode))
        {
            System.Diagnostics.Debug.WriteLine("GetWindData: Invalid ID");
            return string.Empty;
        }
        using var client = new HttpClient();
        try
        {
            // Use ICAO code for precise airport METAR data
            var response = await client.GetStringAsync($"https://aviationweather.gov/api/data/metar?ids={airportCode}&format=json");
            var metars = JsonSerializer.Deserialize<List<MetarData>>(response);
            if (metars != null && metars.Any(m => !string.IsNullOrEmpty(m.Wdir) && int.TryParse(m.Wdir, out var wdir) && wdir >= 0 && wdir <= 360 && m.Wspd.HasValue))
            {
                System.Diagnostics.Debug.WriteLine($"https://aviationweather.gov/api/data/metar?ids={airportCode}&format=json");
                System.Diagnostics.Debug.WriteLine($"Wind Data for {airportCode}: {response}");
                System.Diagnostics.Debug.WriteLine($"GetWindData: Found valid METAR data for {airportCode}");
                return response;
            }
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetWindData Error: {ex.Message}");
            return string.Empty;
        }
        return string.Empty; // Ensure we return an empty string if no valid data found
    }
    public record MetarData //  https://x.com/i/grok?conversation=1937912238297264182
    {
        [JsonPropertyName("icaoId")]
        public string? IcaoId { get; init; }
        [JsonPropertyName("wdir")]
        [JsonConverter(typeof(WindDirectionConverter))]
        public string? Wdir { get; init; }
        [JsonPropertyName("wspd")]
        public int? Wspd { get; init; }
        [JsonPropertyName("wgst")]
        public int? Wgst { get; init; }
        [JsonPropertyName("receiptTime")]
        public string ReceiptTime { get; init; }
    }
    // New: Custom converter for wind direction
    public class WindDirectionConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return reader.GetString();
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
                return number.ToString();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value == null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(value);
        }
    }
    public class ManualIASChangedMessage
    {
        public float ManualIAS { get; }
        public ManualIASChangedMessage(float manualIAS) => ManualIAS = manualIAS;
    }
    private void OnAccelerometerReadingChanged(object sender, AccelerometerChangedEventArgs e)
    {
        try
        {
            var data = e.Reading.Acceleration;
            _lastX = data.X; // Store readings
            _lastY = data.Y;
            _lastZ = data.Z;
            double gForce = CalculateGForce(data.X, data.Y, data.Z);
            double adjustedStallSpeed = AdjustStallSpeed(gForce);
            // System.Diagnostics.Debug.WriteLine($"MainPage:OnAccelerometerReadingChanged Updated adjustedStallSpeed to {adjustedStallSpeed:F0} knots, G-Force: {gForce:F2}");

            MainThread.BeginInvokeOnMainThread(() =>
            {
                StallSpeedLabel.Text = $"G-Adjusted Stall Speed: {adjustedStallSpeed:F0} knots";
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Accelerometer error: {ex.Message}");
        }
    }

    private double CalculateGForce(float x, float y, float z)
    {
        double totalAcceleration = Math.Sqrt(x * x + y * y + z * z);
        double gForce = totalAcceleration / 9.81;
        //System.Diagnostics.Debug.WriteLine($"MainPage: Raw G-Force: {gForce:F2}, X: {x:F2}, Y: {y:F2}, Z: {z:F2}, TotalAccel: {totalAcceleration:F2} m/s²");
        if (totalAcceleration < 8.0) // Below expected gravity (~9.81 m/s²)
        {
            // System.Diagnostics.Debug.WriteLine("MainPage: Low or invalid acceleration, assuming 1G");
            return 1.0;
        }
        return gForce;
    }

    private double CalculateGForceFromLastReading()
    {
        if (Math.Abs(_lastX) < 0.1 && Math.Abs(_lastY) < 0.1 && Math.Abs(_lastZ) < 0.1)
        {
            System.Diagnostics.Debug.WriteLine("MainPage: No accelerometer data, assuming 1G");
            return 1.0;
        }
        double totalAcceleration = Math.Sqrt(_lastX * _lastX + _lastY * _lastY + _lastZ * _lastZ);
        double gForce = totalAcceleration / 9.81;
        System.Diagnostics.Debug.WriteLine($"MainPage: Last G-Force: {gForce:F2}, X: {_lastX:F2}, Y: {_lastY:F2}, Z: {_lastZ:F2}, TotalAccel: {totalAcceleration:F2} m/s²");
        if (totalAcceleration < 8.0) // Below expected gravity
        {
            //System.Diagnostics.Debug.WriteLine("MainPage: Low or invalid last acceleration, assuming 1G");
            return 1.0;
        }
        return gForce;
    }

    private double AdjustStallSpeed(double gForce)
    {
        if (gForce < 0.01) // Near 0G, stall speed approaches 0
        {
            return 0;
        }
        // Adjusted stall speed = 0G stall speed * sqrt(abs(load factor))
        return _zeroGStallSpeed * Math.Sqrt(Math.Abs(gForce));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Accelerometer.IsSupported)
        {
            Accelerometer.Stop();
            Accelerometer.ReadingChanged -= OnAccelerometerReadingChanged;
        }
    }
    private void ShowSkullNotification(bool isActive)
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            var notificationManager = (Android.App.NotificationManager)context.GetSystemService(Context.NotificationService);
            const int notificationId = 1001; // Unique ID for this notification
            string channelId = "aviation_alert_channel";
            string channelName = "Aviation Alerts";

            // Create notification channel for Android 8.0+ (API 26+)
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            {
                var channel = new Android.App.NotificationChannel(channelId, channelName, Android.App.NotificationImportance.High)
                {
                    Description = "Alerts for DMMS monitoring",
                    LockscreenVisibility = Android.App.NotificationVisibility.Public
                };
                channel.EnableVibration(true);
                channel.EnableLights(true);
                // Set custom stall warning sound
                var soundUri = Android.Net.Uri.Parse($"android.resource://{context.PackageName}/{Resource.Raw.stallwarning}");
                channel.SetSound(soundUri, null);
                notificationManager.CreateNotificationChannel(channel);
            }

            if (isActive)
            {
                var builder = new Android.App.Notification.Builder(context, channelId)
                    .SetContentTitle("DMMS Alert")
                    .SetContentText(ttsAlertText)
                    .SetSmallIcon(Resource.Drawable.skull_crossbones_notification) // Assumes icon is correctly set up
                    .SetPriority((int)Android.App.NotificationPriority.High)
                    .SetAutoCancel(false)
                    .SetOngoing(true)
                    .SetVibrate(new long[] { 0, 500, 250, 500 }); // Vibration pattern

                // Set sound explicitly for pre-Android 8.0 or as fallback
                if (Android.OS.Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.O)
                {
                    builder.SetSound(Android.Media.RingtoneManager.GetDefaultUri(Android.Media.RingtoneType.Notification));
                }

                // Intent to open the app when the notification is clicked
                var intent = context.PackageManager.GetLaunchIntentForPackage(context.PackageName);
                if (intent != null)
                {
                    intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
                    var pendingIntent = PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
                    builder.SetContentIntent(pendingIntent);
                }

                // Show the notification
                notificationManager.Notify(notificationId, builder.Build());
                Log.Debug("MainPage", "Skull and Crossbones notification shown with stall warning sound");
            }
            else
            {
                // Cancel the notification
                notificationManager.Cancel(notificationId);
                Log.Debug("MainPage", "Skull and Crossbones notification cancelled");
            }
        }
        catch (Exception ex)
        {
            Log.Error("MainPage", $"Failed to manage notification: {ex.Message}\n{ex.StackTrace}");
            Debug.WriteLine($"MainPage: Failed to manage notification: {ex.Message}");
        }
#endif
    }
    private async void TriggerTTSVolumeTest()
    {
        lock (TtsTestLock)
        {
            // Cancel and dispose previous token safely
            if (ttsTestCts != null)
            {
                try
                {
                    ttsTestCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    Log.Debug("MainPage", "TTS test CancellationTokenSource already disposed");
                }
                finally
                {
                    ttsTestCts.Dispose();
                    ttsTestCts = null;
                }
            }
            ttsTestCts = new CancellationTokenSource();
        }

        var token = ttsTestCts.Token;
        lastVolumeChange = DateTime.Now;
        ttsTestTask = Task.Run(async () =>
        {
            try
            {
                while ((DateTime.Now - lastVolumeChange) < ttsTestDuration && !token.IsCancellationRequested)
                {
                    await TextToSpeech.Default.SpeakAsync(
                        "TTS testing Alert Volume. Loud enough to get your attention?",
                        new SpeechOptions { Volume = ttsAlertVolume },
                        token);
                    await Task.Delay(1500, token);
                }
            }
            catch (TaskCanceledException)
            {
                Log.Debug("MainPage", "TTS volume test task cancelled");
            }
            catch (ObjectDisposedException)
            {
                Log.Debug("MainPage", "TTS volume test task encountered disposed object");
            }
            catch (Exception ex)
            {
                Log.Error("MainPage", $"TTS volume test error: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                lock (TtsTestLock)
                {
                    if (ttsTestCts != null)
                    {
                        ttsTestCts.Dispose();
                        ttsTestCts = null;
                    }
                    ttsTestTask = null;
                }
            }
        }, token);
    }

    private void OnTTSVolumeChanged(object sender, ValueChangedEventArgs e)
    {
        // Set rounded value directly through property to trigger TTS test
        TTSAlertVolume = (float)e.NewValue;
    }
    private bool IsAccelerationPlateau()
    {
        if (airspeedHistory.Count < 2)
        {
            return false;
        }

        // Get data within the last AccelerationWindowSeconds
        var recent = airspeedHistory
            .Where(x => (DateTime.Now - x.Time).TotalSeconds <= AccelerationWindowSeconds)
            .OrderBy(x => x.Time)
            .ToList();

        if (recent.Count < 2)
        {
            return false;
        }

        // Calculate average acceleration
        double totalAccel = 0;
        int count = 0;
        for (int i = 1; i < recent.Count; i++)
        {
            var t1 = recent[i - 1].Time;
            var t2 = recent[i].Time;
            var s1 = recent[i - 1].SpeedKnots;
            var s2 = recent[i].SpeedKnots;
            var deltaT = (t2 - t1).TotalSeconds;
            if (deltaT > 0)
            {
                var accel = (s2 - s1) / deltaT; // knots/s
                totalAccel += accel;
                count++;
            }
        }

        if (count == 0)
        {
            return false;
        }

        var avgAccel = totalAccel / count;
        Log.Debug("MainPage", $"Average acceleration: {avgAccel:F2} knots/s");
        return avgAccel < AccelerationThreshold;
    }

    private async Task LoadAirportsAsync()
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("Stations.csv");
            using var reader = new StreamReader(stream);
            var csvContent = await reader.ReadToEndAsync();
            var lines = csvContent.Split('\n').Skip(1); // Skip header
            airports.Clear();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',', StringSplitOptions.None);
                if (parts.Length < 9) continue;
                if (double.TryParse(parts[2], out var lat) && double.TryParse(parts[3], out var lon))
                {
                    airports.Add(new Airport
                    {
                        StationId = parts[0].Trim(),
                        Elev = string.IsNullOrEmpty(parts[4]) ? 0 : (int)(double.Parse(parts[4])),
                        Site = parts[5].Trim(),
                        Latitude = lat,
                        Longitude = lon
                    });
                }
            }
            Log.Debug("MainPage", $"Loaded {airports.Count} airports from Stations.csv");
            System.Diagnostics.Debug.WriteLine($"LoadAirportsAsync: Loaded {airports.Count} airports");
        }
        catch (Exception ex)
        {
            Log.Error("MainPage", $"Failed to load airports: {ex.Message}\n{ex.StackTrace}");
            ClosestAirportText = "Closest Airport: Error loading data";
            System.Diagnostics.Debug.WriteLine($"LoadAirportsAsync: Error - {ex.Message}");
        }
    }

    private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371; // Earth's radius in km
        var lat1Rad = lat1 * Math.PI / 180;
        var lat2Rad = lat2 * Math.PI / 180;
        var deltaLat = (lat2 - lat1) * Math.PI / 180;
        var deltaLon = (lon2 - lon1) * Math.PI / 180;

        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c; // Distance in km
    }
    // New: Safe settings load
    private void LoadSettings()
    {
        System.Diagnostics.Debug.WriteLine("MainPage: Loading settings");
        var loadedDmms = Preferences.Get("DmmsValue", "70");
        if (SpeedRange != null && SpeedRange.Contains(loadedDmms) && !string.IsNullOrEmpty(loadedDmms) && StallSpeedLabel != null)

        {
            DmmsText = loadedDmms;
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"MainPage: Ignored invalid DmmsText '{loadedDmms}' from settings");
        }
    }
    private Airport FindClosestAirport(double latitude, double longitude)
    {
        return airports.OrderBy(a => CalculateDistance(latitude, longitude, a.Latitude, a.Longitude))
                       .FirstOrDefault() ?? new Airport { StationId = "N/A", Site = "Unknown" };
    }

    private async void OnOptionsClicked(object sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("Options button clicked"); 
        await Navigation.PushAsync(new OptionsPage());
    }

    private async void OnCounterClicked(object sender, EventArgs e)
    {
        if (!await AreLocationPermissionsGrantedAsync())
        {
            System.Diagnostics.Debug.WriteLine("MainPage: Requesting location permissions");
            await _platformService.ShowPermissionPopupAndRequestAsync();
            // If permissions are granted, start the service and update the button state
            if (await AreLocationPermissionsGrantedAsync())
            {
                await StartLocationService();
                await UpdateCounterBtnState();
            }
            return;
        }
        // 0 !True
        //if (count < 1 || !IsActive)
        if (ButtonState != ButtonState.Active)
        {
            bool wasMonitoringStarted = _isMonitoringStarted;
            _isMonitoringStarted = !_isMonitoringStarted;

            if (!await AreLocationPermissionsGrantedAsync())
            {
                System.Diagnostics.Debug.WriteLine("MainPage: Showing permission popup");
                await _platformService.ShowPermissionPopupAndRequestAsync();
            }
            if (await AreLocationPermissionsGrantedAsync())
            {
                await StartLocationService();
            }

            await UpdateCounterBtnState();

            if (_isMonitoringStarted && await AreLocationPermissionsGrantedAsync())
            {
                await StartLocationService();
            }
            //CounterBtnBorder.Background = (Brush)Resources["ActiveGradient"];
            count = 1;
        }
        else
        {
            if (StopLocationService() == 0)
            {
                IsActive = false;
                ButtonState = ButtonState.Paused;
                _isMonitoringStarted = false;
                if (IsFlashing)
                {
                    await StopFlashingBackground();
                }
                //CounterBtnBorder.Background = (Brush)Resources["PausedGradient"];
            }
            else
            {
                ButtonState = ButtonState.Failed;
                //CounterBtn.Text = $"Pause failed, please click again. Tried {count} times...";
                //CounterBtnBorder.Background = (Brush)Resources["FailedGradient"];
            }
        }

        //if (count == 1)
        //{
        //    IsActive = true;            
        //    CounterBtn.Text = "DMMS Monitoring Started";
        //}
        //else if (ButtonState == ButtonState.Paused)
        //{
        //    CounterBtn.Text = "=== P A U S E D ===";
        //    CounterBtn.TextColor = Microsoft.Maui.Graphics.Color.FromRgba("#FFFFFF");
        //}
        //else if (ButtonState == ButtonState.Failed)
        //{
        //    CounterBtn.Text = $"Pause failed, please click again. Tried {count} times...";
        //}



        SemanticScreenReader.Announce(CounterBtn.Text);
    }
   
    private async Task UpdateCounterBtnState()
    {

        if (!await AreLocationPermissionsGrantedAsync())
        {
            ButtonState = ButtonState.PermissionRequired;
            Log.Debug("MainPage", "CounterBtn set to 'Set Location Permission' due to missing permissions");
        }
        else
        {
            ButtonState = _isMonitoringStarted ? ButtonState.Active : ButtonState.Paused;
            Log.Debug("MainPage", $"CounterBtn set to {ButtonState.ToString()}");
        }

    }
    private async void OnButtonTapped(object sender, EventArgs e)
    {
        await CounterBtnBorder.ScaleTo(0.95, 100);
        await CounterBtnBorder.ScaleTo(1.0, 100);
    }
    private async Task StopFlashingBackground()
    {
        CancellationTokenSource localFlashingCts;
        CancellationTokenSource localTtsCts;
        lock (_flashLock)
        {
            localFlashingCts = flashingCts;
            localTtsCts = ttsCts;
            flashingCts = null;
            ttsCts = null;
        }

        if (localFlashingCts != null)
        {
            try
            {
                localFlashingCts.Cancel();
            }
            catch (Exception ex)
            {
                Log.Error("MainPage", $"Error cancelling flashingCts: {ex.Message}");
            }
            finally
            {
                localFlashingCts.Dispose();
                Log.Debug("MainPage", "Flashing CancellationTokenSource cancelled and disposed");
            }
        }

        if (localTtsCts != null)
        {
            try
            {
                localTtsCts.Cancel();
            }
            catch (Exception ex)
            {
                Log.Error("MainPage", $"Error cancelling ttsCts: {ex.Message}");
            }
            finally
            {
                localTtsCts.Dispose();
                Log.Debug("MainPage", "TTS CancellationTokenSource cancelled and disposed");
            }
        }

        if (_flashingTask != null)
        {
            try
            {
                await _flashingTask;
            }
            catch (Exception ex)
            {
                Log.Error("MainPage", $"Error awaiting flashing task: {ex.Message}");
            }
            _flashingTask = null;
        }

        if (_ttsTask != null)
        {
            try
            {
                await _ttsTask;
            }
            catch (Exception ex)
            {
                Log.Error("MainPage", $"Error awaiting TTS task: {ex.Message}");
            }
            _ttsTask = null;
        }

#if ANDROID
        try
        {
            if (_originalMediaVolume != -1)
            {
                var audioManager = (Android.Media.AudioManager)Android.App.Application.Context.GetSystemService(Context.AudioService);
                audioManager.SetStreamVolume(Android.Media.Stream.Music, _originalMediaVolume, 0);
                Log.Debug("MainPage", $"Restored media volume to original: {_originalMediaVolume}");
                _originalMediaVolume = -1;
            }
        }
        catch (Exception ex)
        {
            Log.Error("MainPage", $"Failed to restore media volume: {ex.Message}\n{ex.StackTrace}");
        }
#endif

        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsFlashing = false;
            PageBackground = Colors.Transparent;
        });
        ShowSkullNotification(false); // Cancel notification
        Log.Debug("MainPage", "Flashing and TTS stopped");
    }
    private async Task StartFlashingBackground()
    {
        lock (_flashLock)
        {
            if (IsFlashing && flashingCts != null && !flashingCts.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine("MainPage: Flashing already active, skipping start");
                return;
            }
            flashingCts?.Dispose();
            ttsCts?.Dispose();
            flashingCts = new CancellationTokenSource();
            ttsCts = new CancellationTokenSource();
            System.Diagnostics.Debug.WriteLine($"MainPage: Created flashingCts={flashingCts.GetHashCode()}, ttsCts={ttsCts.GetHashCode()}");
            IsFlashing = true;
            //BringAppToForeground();
            ShowSkullNotification(true); // Show notification
        }

#if ANDROID
        try
        {
            var audioManager = (Android.Media.AudioManager)Android.App.Application.Context.GetSystemService(Context.AudioService);
            _originalMediaVolume = audioManager.GetStreamVolume(Android.Media.Stream.Music);
            int maxVolume = audioManager.GetStreamMaxVolume(Android.Media.Stream.Music);
            audioManager.SetStreamVolume(Android.Media.Stream.Music, maxVolume, 0);
            System.Diagnostics.Debug.WriteLine($"MainPage: Captured original media volume {_originalMediaVolume}, set to max {maxVolume}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainPage: Failed to capture or set media volume: {ex.Message}\n{ex.StackTrace}");
        }
#endif

        System.Diagnostics.Debug.WriteLine($"MainPage: Starting flashing and TTS tasks with TtsAlertText={ttsAlertText}, MessageFrequency={messageFrequency}");
        var flashingToken = flashingCts.Token;
        _flashingTask = Task.Run(async () =>
        {
            try
            {
                while (!flashingToken.IsCancellationRequested)
                {
                    MainThread.BeginInvokeOnMainThread(() => PageBackground = Colors.Red.WithAlpha(0.8f));
                    await Task.Delay(500, flashingToken);
                    MainThread.BeginInvokeOnMainThread(() => PageBackground = Colors.Transparent);
                    await Task.Delay(500, flashingToken);
                }
            }
            catch (TaskCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("MainPage: Flashing task cancelled");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainPage: Flashing error: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                lock (_flashLock)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        PageBackground = Colors.Transparent;
                        IsFlashing = false;
                    });
                    flashingCts?.Dispose();
                    flashingCts = null;
                    _flashingTask = null;
                    System.Diagnostics.Debug.WriteLine("MainPage: Flashing task cleaned up");
                }
            }
        }, flashingToken);

        var ttsToken = ttsCts.Token;
        _ttsTask = Task.Run(async () =>
        {
            try
            {
                await TextToSpeech.Default.SpeakAsync(
                    ttsAlertText,
                    new SpeechOptions { Volume = TTSAlertVolume},
                    ttsToken);
                System.Diagnostics.Debug.WriteLine("MainPage: TTS: Initial message played at maximum volume");
                while (!ttsToken.IsCancellationRequested)
                {
                    await Task.Delay((int)(messageFrequency * 1000), ttsToken);
                    if (!ttsToken.IsCancellationRequested)
                    {
                        System.Diagnostics.Debug.WriteLine($"MainPage: Attempting to play TTS: {ttsAlertText}");
                        await TextToSpeech.Default.SpeakAsync(
                            ttsAlertText,
                            new SpeechOptions { Volume = TTSAlertVolume },
                            ttsToken);
                        System.Diagnostics.Debug.WriteLine("MainPage: TTS: Played message at maximum volume");
                    }
                }
            }
            catch (TaskCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("MainPage: TTS task cancelled");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainPage: TTS error: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                lock (_flashLock)
                {
                    ttsCts?.Dispose();
                    ttsCts = null;
                    _ttsTask = null;
                    System.Diagnostics.Debug.WriteLine("MainPage: TTS task cleaned up");
                }
            }
        }, ttsToken);
    }

    private void BringAppToForeground()
    {
        try
        {
            var activity = Platform.CurrentActivity;
            if (activity == null)
            {
                Log.Error("MainPage", "Current activity is null");
                return;
            }

            var packageManager = activity.PackageManager;
            var intent = packageManager.GetLaunchIntentForPackage(activity.PackageName);
            if (intent != null)
            {
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
                activity.StartActivity(intent);
                Log.Debug("MainPage", "Brought app to foreground");
            }
            else
            {
                Log.Error("MainPage", "Failed to get launch intent");
            }
        }
        catch (Exception ex)
        {
            Log.Error("MainPage", $"Failed to bring app to foreground: {ex.Message}\n{ex.StackTrace}");
            Debug.WriteLine($"MainPage: Failed to bring app to foreground: {ex.Message}");
        }
    }
    private async Task StartLocationService()
    {
        Debug.WriteLine("MainPage: StartLocationService started");
        try
        {
            if (!await _platformService.ArePermissionsGrantedAsync())
            {
                Debug.WriteLine("MainPage: Permissions not granted, skipping service start");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    WarningLabelText = "Please allow 'ALL THE TIME' in settings by clicking button below.";
                });
                return;
            }
            else if(WarningLabelText == "Please allow 'ALL THE TIME' in settings by clicking button below.")
            {
                WarningLabelText = "< DMMS Alerter <";
            }
            // Check notification permission for Android 13+
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
            {
                var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.PostNotifications>();
                    if (status != PermissionStatus.Granted)
                    {
                        Debug.WriteLine("MainPage: Notification permission denied");
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            WarningLabelText = "Please allow notifications in settings.";
                        });
                        return;
                    }
                }
            }
            var context = Android.App.Application.Context;
            var startServiceIntent = new Intent(context, typeof(AviationApp.Services.LocationService));
            context.StartForegroundService(startServiceIntent);
            Debug.WriteLine("MainPage: StartForegroundService called");
            // assuming service started //
            IsActive = true;
            _isMonitoringStarted = true;
            ButtonState = ButtonState.Active; // Update button state to active
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MainPage: Failed to start service: {ex.Message}");
            Log.Debug("MainPage", $"Failed to start service: {ex.Message}");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                WarningLabelText = "Failed to start location service.";
            });
        }
    }

    private int StopLocationService()
    {
        Debug.WriteLine("MainPage: StopLocationService started");
        try
        {
            var context = Android.App.Application.Context;
            var stopServiceIntent = new Intent(context, typeof(AviationApp.Services.LocationService));
            context.StopService(stopServiceIntent);
            Debug.WriteLine("MainPage: StopService called");
            return 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MainPage: StopLocationService failed: {ex.Message}");
            Log.Debug("MainPage", $"StopLocationService failed: {ex.Message}");
            return -1;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ttsAlertVolume = Preferences.Get("TTSAlertVolume", 1.0f);
        Preferences.Remove($"LastMetarFetch_{lastStationID}"); // delete any METAR data.
        // Reload settings live
        warningLabelText = Preferences.Get("WarningLabelText", "< DMMS Alerter <");
        ttsAlertText = Preferences.Get("TtsAlertText", "SPEED CHECK, YOUR GONNA FALL OUTTA THE SKY LIKE UH PIANO");
        messageFrequency = Preferences.Get("MessageFrequency", 5f);
        showSkull = Preferences.Get("ShowSkull", true);
        autoActivateMonitoring = Preferences.Get("AutoActivateMonitoring", true);
        airportCallOuts = Preferences.Get("AirportCallOuts", true);
        ShowSkullWarning = IsFlashing && showSkull;
        OnPropertyChanged(nameof(TTSAlertVolume));
        OnPropertyChanged(nameof(WarningLabelText));
        OnPropertyChanged(nameof(ShowSkullWarning));
        OnPropertyChanged(nameof(AirportCallOuts));
        Log.Debug("MainPage", $"OnAppearing - Reloaded settings: WarningLabelText: {warningLabelText}, TtsAlertText: {ttsAlertText}, MessageFrequency: {messageFrequency}, ShowSkull: {showSkull}, AirportCallOuts:{airportCallOuts}, AutoActivateMonitoring: {autoActivateMonitoring}, ShowSkullWarning: {showSkullWarning}, SuppressWarnings: {suppressWarningsUntilAboveDmms}");

        // Handle ManualIAS update for wind adjustment
        if (Preferences.Get("ManualIASUpdated", false))
        {
            float manualIAS = Preferences.Get("ManualIAS", 0f);
            if (manualIAS > 0 && lastLocation != null) // Ensure valid input and location
            {
                float gpsSpeedKnots = (float)(lastLocation.Speed.GetValueOrDefault() / 0.514444); // Convert m/s to knots
                windAdjustment = manualIAS - gpsSpeedKnots; // Wind effect
                referenceHeading = lastLocation.Course ?? 0; // Store heading at this moment
                isWindAdjustmentSet = true;
                System.Diagnostics.Debug.WriteLine($"Wind adjustment set: {windAdjustment:F1} knots at heading {referenceHeading:F0}°");
            }
            else
            {
                isWindAdjustmentSet = false; // Disable adjustment if conditions not met
                System.Diagnostics.Debug.WriteLine("ManualIAS update ignored: Invalid ManualIAS or no location data");
            }
            Preferences.Set("ManualIASUpdated", false); // Reset the update flag            
        }

        await UpdateCounterBtnState();
    }

    public new event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        });
    }
    private async Task<int> UpdateMagneticHeadingAsync(double currentLat, double currentLon, double airportLat, double airportLon)
    {
        try
        {
            if (airportLat == 0 && airportLon == 0)
            {
                System.Diagnostics.Debug.WriteLine("Update: Invalid airport coordinates");
                return 0;
            }

            // Helper functions
            double ToRadians(double degrees) => degrees * Math.PI / 180;
            double ToDegrees(double radians) => radians * 180 / Math.PI;

            // True bearing
            double CalculateTrueBearing(double lat1, double lon1, double lat2, double lon2)
            {
                double dLon = ToRadians(lon2 - lon1);
                lat1 = ToRadians(lat1);
                lat2 = ToRadians(lat2);

                double y = Math.Sin(dLon) * Math.Cos(lat2);
                double x = Math.Cos(lat1) * Math.Sin(lat2) -
                           Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
                double bearing = Math.Atan2(y, x);

                bearing = ToDegrees(bearing);
                if (bearing < 0) bearing += 360;
                return bearing;
            }

            // Magnetic variation (placeholder, replace with your method)
            double EstimateMagneticVariation(double latitude, double longitude)
            {
                // Simplified: ~1° per 10° longitude
                return longitude / 10.0;
            }

            // Calculate heading
            double trueBearing = CalculateTrueBearing(currentLat, currentLon, airportLat, airportLon);
            double magneticVariation = EstimateMagneticVariation(currentLat, currentLon);
            double heading = trueBearing + magneticVariation;
            if (heading < 0) heading += 360;
            if (heading >= 360) heading -= 360;

            int result = (int)heading;
            System.Diagnostics.Debug.WriteLine($"Update: Magnetic heading: {result}°");
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update: Error calculating heading: {ex.Message}");
            return 0;
        }
    }
}
