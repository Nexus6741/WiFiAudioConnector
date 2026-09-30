@echo off
chcp 65001 >nul
echo [WiFiAudioConnector] 正在编译构建...

set CSC_PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC_PATH%" (
    echo [错误] 未在系统找到 .NET Framework 编译器: %CSC_PATH%
    pause
    exit /b 1
)

"%CSC_PATH%" /target:winexe /win32icon:app.ico /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /r:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,WindowsBase.dll,PresentationCore.dll,PresentationFramework.dll,System.Xaml.dll /out:WiFiAudioConnector.exe WiFiAudioConnector.cs

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ===================================================
    echo  编译成功！已生成 WiFiAudioConnector.exe
    echo ===================================================
) else (
    echo.
    echo [错误] 编译失败，请检查报错信息。
    pause
    exit /b 1
)
