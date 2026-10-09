@echo off
setlocal
set "APPDIR=%LOCALAPPDATA%\PiggyBuddy"
set "FX=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FX%\csc.exe" set "FX=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FX%\csc.exe" goto noframework
if not exist "%APPDIR%" mkdir "%APPDIR%"
if exist "%APPDIR%\PiggyBuddy-v2.0.exe" goto launch
pushd "%~dp0"
echo Checking the timer...
"%FX%\csc.exe" /nologo /target:exe /out:"%APPDIR%\TimerChecks.exe" "source\TimerCore.cs" "source\CoreTests.cs" >"%APPDIR%\build.log" 2>&1
if errorlevel 1 goto failed
"%APPDIR%\TimerChecks.exe" >>"%APPDIR%\build.log" 2>&1
if errorlevel 1 goto failed
echo Building your desktop companion...
"%FX%\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /utf8output /out:"%APPDIR%\PiggyBuddy-v2.0.exe" /win32manifest:"source\app.manifest" /resource:"assets\pet.png",PiggyBuddy.pet.png /r:"%FX%\WPF\PresentationCore.dll" /r:"%FX%\WPF\PresentationFramework.dll" /r:"%FX%\WPF\WindowsBase.dll" /r:"%FX%\WPF\System.Speech.dll" /r:"%FX%\System.Xaml.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.dll "source\TimerCore.cs" "source\Settings.cs" "source\App.cs" >>"%APPDIR%\build.log" 2>&1
if errorlevel 1 goto failed
popd
:launch
start "" "%APPDIR%\PiggyBuddy-v2.0.exe"
exit /b 0
:failed
popd
echo.
echo Could not build the app. Please share build.log for troubleshooting.
echo %APPDIR%\build.log
notepad "%APPDIR%\build.log"
pause
exit /b 1
:noframework
echo The Windows .NET Framework compiler was not found.
echo Please enable or repair .NET Framework 4.x, then try again.
pause
exit /b 1
