using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

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

    public OptionsPage()
    {
        InitializeComponent();
        BindingContext = this;

        // Load saved settings or defaults
        MessageFrequency = Preferences.Get("MessageFrequency", 5f); // Match MainPage default
        ShowSkull = Preferences.Get("ShowSkull", true);
        AutoActivateMonitoring = Preferences.Get("AutoActivateMonitoring", true); // Match MainPage default
        WarningLabelText = Preferences.Get("WarningLabelText", "< DMMS Alerter <");
        TtsAlertText = Preferences.Get("TtsAlertText", "SPEED CHECK, YOUR GONNA FALL OUTTA THE SKY LIKE UH PIANO");
        AirportCallOuts = Preferences.Get("AirportCallOuts", true);
        // Log for debugging
        System.Diagnostics.Debug.WriteLine($"OptionsPage: Loaded settings -  AirportCallOuts: {AirportCallOuts}, MessageFrequency: {MessageFrequency}, ShowSkull: {ShowSkull}, AutoActivateMonitoring: {AutoActivateMonitoring}, WarningLabelText: {WarningLabelText}, TtsAlertText: {TtsAlertText}");
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        // Validate MessageFrequency
        if (!float.TryParse(MessageFrequencyEntry.Text, out float frequency) || frequency <= 0)
        {
            await DisplayAlert("Error", "Please enter a valid frequency (seconds > 0).", "OK");
            return;
        }

        // Save settings to Preferences
        Preferences.Set("MessageFrequency", MessageFrequency);
        Preferences.Set("ShowSkull", ShowSkull);
        Preferences.Set("AutoActivateMonitoring", AutoActivateMonitoring);
        Preferences.Set("WarningLabelText", WarningLabelText);
        Preferences.Set("TtsAlertText", TtsAlertText);
        Preferences.Set("AirportCallOuts", AirportCallOuts);

        // Log saved settings
        System.Diagnostics.Debug.WriteLine($"OptionsPage: Saved settings - AirportCallOuts: {AirportCallOuts}, MessageFrequency: {MessageFrequency}, ShowSkull: {ShowSkull}, AutoActivateMonitoring: {AutoActivateMonitoring}, WarningLabelText: {WarningLabelText}, TtsAlertText: {TtsAlertText}");

        await DisplayAlert("Success", "Settings saved successfully!", "OK");
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