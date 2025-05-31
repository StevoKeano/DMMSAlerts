DMMS Alerts
DMMS Alerts is a .NET MAUI Android application for real-time aviation monitoring, providing location-based alerts with customizable settings. Built with .NET 9.0 (preview/RC), it displays latitude, longitude, altitude, and speed, with a user-friendly interface and background services.
Project Details
Namespace/Package: com.steve.DMMSAlerts
Target Framework: net9.0-android
Project Path: /e/dev/AviationApp
Key Files:
MainPage.xaml, MainPage.xaml.cs: Main UI with location data and Options button.
OptionsPage.xaml, OptionsPage.xaml.cs: Settings for MessageFrequency, ShowSkull, AutoActivateMonitoring, WarningLabelText, TtsAlertText.
App.xaml, App.xaml.cs: Global styles and navigation setup.
MainActivity.cs: Android-specific status bar and permissions.
AndroidManifest.xml: Configures package, permissions, and icons.
proguard-rules.pro: Prevents resource stripping.
Features
Displays latitude, longitude, altitude, and speed as whole numbers (F0 format).
Options button (top-right) navigates to settings page.
Transparent status bar with no navigation bar (NavigationPage.HasNavigationBar="False").
Customizable settings: MessageFrequency, ShowSkull, AutoActivateMonitoring, WarningLabelText, TtsAlertText.
Android permissions: ACCESS_FINE_LOCATION, ACCESS_COARSE_LOCATION, FOREGROUND_SERVICE.
Min SDK: 21, Target SDK: 34.
Custom app icon (appicon.png) in mipmap folders.
Prerequisites
.NET 9.0 SDK (preview/RC)
Android SDK (API 21–34)
adb for device installation
Git for cloning the repository
WSL or Linux-like environment for build commands
Setup
Clone the repository:
bash
git clone <repository-url>
cd /e/dev/AviationApp
Copy app icons to mipmap folders:
bash
mkdir -p /e/dev/AviationApp/Resources/mipmap-mdpi
mkdir -p /e/dev/AviationApp/Resources/mipmap-hdpi
mkdir -p /e/dev/AviationApp/Resources/mipmap-xhdpi
mkdir -p /e/dev/AviationApp/Resources/mipmap-xxhdpi
mkdir -p /e/dev/AviationApp/Resources/mipmap-xxxhdpi
cp "<path-to-icons>/48x48.png" /e/dev/AviationApp/Resources/mipmap-mdpi/appicon.png
cp "<path-to-icons>/72x72.png" /e/dev/AviationApp/Resources/mipmap-hdpi/appicon.png
cp "<path-to-icons>/96x96.png" /e/dev/AviationApp/Resources/mipmap-xhdpi/appicon.png
cp "<path-to-icons>/144x144.png" /e/dev/AviationApp/Resources/mipmap-xxhdpi/appicon.png
cp "<path-to-icons>/192x192.png" /e/dev/AviationApp/Resources/mipmap-xxxhdpi/appicon.png
Ensure AndroidManifest.xml references the icon:
xml
<application android:icon="@mipmap/appicon" android:roundIcon="@mipmap/appicon" android:supportsRtl="true" android:label="@string/app_name">
Build Instructions
Run the following commands to build and install the app:
bash
cd /e/dev/AviationApp
dotnet clean /e/dev/AviationApp/AviationApp.csproj -f net9.0-android -c Release
rm -rf obj bin
dotnet build -c Release -f net9.0-android
dotnet publish /e/dev/AviationApp/AviationApp.csproj -f net9.0-android -c Release -p:AndroidCreatePackage=true -p:AndroidPackageFormat=apk -p:NoBuild=false --no-restore
adb install /e/dev/AviationApp/bin/Release/net9.0-android/com.steve.DMMSAlerts-Signed.apk
Debugging
Use System.Diagnostics.Debug.WriteLine for logs in MainPage.xaml.cs and OptionsPage.xaml.cs.
Monitor Logcat:
bash
adb logcat | grep com.steve.DMMSAlerts
Check for resource issues if the icon or UI elements fail to load.
Project Structure
MainPage.xaml: AbsoluteLayout with ScrollView above SkullImage, purple Options button (Margin="0,10,10,0").
OptionsPage.xaml: Settings page with Button style from App.xaml.
App.xaml: Defines global styles (Button, Headline, SubHeadline, PageBackgroundColor=Transparent).
MainActivity.cs: Transparent status bar using WindowInsetsController (API 30+) or WindowManagerFlags.TranslucentStatus.
proguard-rules.pro: Prevents resource stripping for MAUI and app resources.
Notes
Ensure ProGuard is enabled in AviationApp.csproj for Release builds.
The app uses modern Android APIs; avoid deprecated methods like Window.SetStatusBarColor.
Commit messages should be concise (<50 characters, e.g., "Fix Options button clickability").
Troubleshooting
Purple .NET Icon: If the default MAUI icon appears, remove <MauiIcon> from AviationApp.csproj and verify @mipmap/appicon in AndroidManifest.xml.
Unclickable Options Button: Check MainPage.xaml for Margin="0,10,10,0" on the button and Padding="30,10,30,0" on the VerticalStackLayout.
XamlParseException: Ensure Button styles are defined in App.xaml.
Contributing
Submit pull requests with concise commit messages (e.g., "Update icon to mipmap/appicon"). Test all changes with the provided build sequence.
Project File Reference:
MainPage.xaml, MainPage.xaml.cs
OptionsPage.xaml, OptionsPage.xaml.cs
App.xaml, App.xaml.cs
MainActivity.cs (Platforms/Android)
AndroidManifest.xml (Platforms/Android)
proguard-rules.pro (Platforms/Android)
AviationApp.csproj
If you need specific additions (e.g., screenshots, version info), let me know!
