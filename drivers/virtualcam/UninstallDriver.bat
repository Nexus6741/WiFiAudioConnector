@echo off
chcp 65001 >nul
:: BatchGotAdmin
:-------------------------------------
REM  --> Check for permissions
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"

REM --> If error flag set, we do not have admin.
if '%errorlevel%' NEQ '0' (
    echo [提示] 正在请求管理员权限以卸载虚拟摄像头组件...
    goto UACPrompt
) else ( goto gotAdmin )

:UACPrompt
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin_vcam.vbs"
    set params = %*:"=""
    echo UAC.ShellExecute "cmd.exe", "/c %~s0 %params%", "", "runas", 1 >> "%temp%\getadmin_vcam.vbs"

    "%temp%\getadmin_vcam.vbs"
    del "%temp%\getadmin_vcam.vbs"
    exit /B

:gotAdmin
    pushd "%CD%"
    CD /D "%~dp0"
    echo [提示] 正在卸载「手机无线摄像头」DirectShow 驱动组件...
    regsvr32 /u /s "UnityCaptureFilter32.dll"
    regsvr32 /u /s "UnityCaptureFilter64.dll"
    echo ====================================================
    echo  已成功注销虚拟摄像头组件！
    echo ====================================================
    timeout /t 2 >nul
:--------------------------------------
