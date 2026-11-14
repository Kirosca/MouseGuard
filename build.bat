@echo off
setlocal
cd /d "%~dp0"

echo [1/2] Compiling MouseGuard.exe (WPF + Per-Monitor DPI V2)...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /platform:anycpu /win32icon:app.ico /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /r:WindowsBase.dll,PresentationCore.dll,PresentationFramework.dll,System.Xaml.dll,System.Windows.Forms.dll,System.Drawing.dll,System.dll,System.Core.dll /out:MouseGuard.exe MouseGuard.cs

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Failed to compile MouseGuard.exe
    exit /b %ERRORLEVEL%
)

echo [2/2] Compiling SimulateMouseMover.exe (Test Tool)...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:exe /optimize+ /platform:anycpu /out:SimulateMouseMover.exe SimulateMouseMover.cs

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Failed to compile SimulateMouseMover.exe
    exit /b %ERRORLEVEL%
)

echo.
echo ===================================================
echo  Build Succeeded!
echo  - MouseGuard.exe
echo  - SimulateMouseMover.exe
echo ===================================================
endlocal
