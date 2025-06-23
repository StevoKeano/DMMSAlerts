using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static AviationApp.MainPage;

namespace AviationApp;
[Microsoft.Maui.Controls.Xaml.XamlCompilation(XamlCompilationOptions.Compile)]
public partial class OptionsPage : ContentPage, INotifyPropertyChanged
{
    private float _messageFrequency;
    private bool _showSkull;
    private bool _autoActivateMonitoring;
    private string _warningLabelText;
    private string _ttsAlertText;
    private bool _airportCallOuts;

    public float MessageFrequency
    {
        get => _messageFrequency;
        set
        {
            if (_messageFrequency != value)
            {
                _messageFrequency = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowSkull
    {
        get => _showSkull;
        set
        {
            if (_showSkull != value)
            {
                _showSkull = value;
                OnPropertyChanged();
            }
        }
    }
    public bool AirportCallOuts
    {
        get => _airportCallOuts;
        set
        {
            if (_airportCallOuts != value)
            {
                _airportCallOuts = value;
                OnPropertyChanged();
            }
        }
    }
    public bool AutoActivateMonitoring
    {
        get => _autoActivateMonitoring;
        set
        {
            if (_autoActivateMonitoring != value)
            {
                _autoActivateMonitoring = value;
                OnPropertyChanged();
            }
        }
    }

    public string WarningLabelText
    {
        get => _warningLabelText;
        set
        {
            if (_warningLabelText != value)
            {
                _warningLabelText = value;
                OnPropertyChanged();
            }
        }
    }

    public string TtsAlertText
    {
        get => _ttsAlertText;
        set
        {
            if (_ttsAlertText != value)
            {
                _ttsAlertText = value;
                OnPropertyChanged();
            }
        }
    }


    private readonly ObservableCollection<int> _iasOptions = new ObservableCollection<int>(); // Empty initially
    private int _selectedIas;

    public ObservableCollection<int> IasOptions => _iasOptions;
    public int SelectedIas
    {
        get => _selectedIas;
        set
        {
            _selectedIas = value;
            OnPropertyChanged(); // Notify UI
        }
    }
    // Constructor: Minimal initialization
    public OptionsPage()
    {
        InitializeComponent();
        BindingContext = this;
        // Initialize _iasOptions with 0, 20–150
        _iasOptions.Add(0); // Add 0 for disabling IAS
        foreach (int i in Enumerable.Range(4, 57).Select(i => i * 5)) // 20–150
        {
            _iasOptions.Add(i);
        }
    }
    
    // Load settings when page appears
    protected override void OnAppearing()
    {
        base.OnAppearing();
        SelectedIas = (int)Preferences.Get("ManualIasPicker", 0f); // Load saved IAS
        // Load saved preferences into UI
        //ManualIasEntry.Text = Preferences.Get("/*ManualIAS*/", 0f).ToString("F1");
        MessageFrequencyEntry.Text = Preferences.Get("MessageFrequency", 5f).ToString("F1");
        MessageFrequency = Preferences.Get("MessageFrequency", 5f);
        ShowSkull = Preferences.Get("ShowSkull", true);
        AutoActivateMonitoring = Preferences.Get("AutoActivateMonitoring", true);
        WarningLabelText = Preferences.Get("WarningLabelText", "< DMMS Alerter <");
        TtsAlertText = Preferences.Get("TtsAlertText", "SPEED CHECK, YOUR GONNA FALL OUTTA THE SKY LIKE UH PIANO");
        AirportCallOuts = Preferences.Get("AirportCallOuts", true); // Adjusted to match MainPage default
        System.Diagnostics.Debug.WriteLine($"OptionsPage: Loaded settings - ManualIAS: {Preferences.Get("ManualIAS", 0f):F1}, MessageFrequency: {MessageFrequency}, ShowSkull: {ShowSkull}, AutoActivateMonitoring: {AutoActivateMonitoring}, WarningLabelText: {WarningLabelText}, TtsAlertText: {TtsAlertText}, AirportCallOuts: {AirportCallOuts}");
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        // Validate MessageFrequency
        if (!float.TryParse(MessageFrequencyEntry.Text, out float frequency) || frequency <= 0)
        {
            await DisplayAlert("Error", "Please enter a valid frequency (seconds > 0).", "OK");
            return;
        }
        //float ias = float.TryParse(ManualIasEntry.Text, out float value) ? value : 0f;
        float ias = SelectedIas;
        System.Diagnostics.Debug.WriteLine($"OptionsPage: Selected IAS={ias:F1}");
        Preferences.Set("ManualIAS", ias);
        Preferences.Set("ManualIASUpdated", true); // Flag to trigger adjustment calculation
        WeakReferenceMessenger.Default.Send(new ManualIASChangedMessage(ias)); // Notify MainPage
        // Save settings to Preferences
        Preferences.Set("MessageFrequency", MessageFrequency);
        Preferences.Set("ShowSkull", ShowSkull);
        Preferences.Set("AutoActivateMonitoring", AutoActivateMonitoring);
        Preferences.Set("WarningLabelText", WarningLabelText);
        Preferences.Set("TtsAlertText", TtsAlertText);
        Preferences.Set("AirportCallOuts", AirportCallOuts);

        // Log saved settings
        System.Diagnostics.Debug.WriteLine($"OptionsPage: Saved settings - AirportCallOuts: {AirportCallOuts}, MessageFrequency: {MessageFrequency}, ShowSkull: {ShowSkull}, AutoActivateMonitoring: {AutoActivateMonitoring}, WarningLabelText: {WarningLabelText}, TtsAlertText: {TtsAlertText}");

        //await DisplayAlert("Success", "Settings saved successfully!", "OK");
        // With this:
        await Utility.DisplayAutoDismissAlert("Success", "Settings saved successfully!", "OK");
        await Navigation.PopAsync();
    }

    public new event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        });
    }
}