cd /e/dev/AviationApp
dotnet clean /e/dev/AviationApp/AviationApp.csproj -f net9.0-android -c Release
rm -rf obj bin
dotnet build -c Release -f net9.0-android
dotnet publish /e/dev/AviationApp/AviationApp.csproj -f net9.0-android -c Release -p:AndroidCreatePackage=true -p:AndroidPackageFormat=apk -p:NoBuild=false --no-restore
adb install /e/dev/AviationApp/bin/Release/net9.0-android/com.steve.DMMSAlerts-Signed.apk
cat b.sh
