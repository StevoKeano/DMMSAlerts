# AviationApp

DMM gotcha stop the death spirals

DMMSAlert
git checkout feature/options-page
git reset --hard HEAD
git clean -fd
git fetch origin
git reset --hard origin/feature/options-page
git status


adb uninstall com.steve.DMMSAlerts



cd /e/dev/AviationApp

dotnet clean /e/dev/AviationApp/AviationApp.csproj -f net9.0-android -c Release

rm -rf obj bin

dotnet build -c release

dotnet publish /e/dev/AviationApp/AviationApp.csproj -f net9.0-android -c Release -p:AndroidCreatePackage=true -p:AndroidPackageFormat=apk -p:NoBuild=false --no-restore

adb install /e/Dev/AviationApp/bin/Release/net9.0-android/com.steve.DMMSAlerts-Signed.apk
