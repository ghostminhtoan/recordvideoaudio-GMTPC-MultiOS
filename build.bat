@echo off
chcp 65001 > nul
setlocal enabledelayedexpansion

title GMTPC - Build & Package Multi-OS Record Video Audio

echo ==========================================================
echo    GMTPC MULTIOS BUILD SYSTEM - RECORD VIDEO AUDIO
echo    Avalonia UI (.NET 10) - Windows / Linux
echo ==========================================================
echo.
echo [1] Đóng gói TOÀN BỘ nền tảng vào thư mục dist\ (Windows & Linux) (Mặc định)
echo [2] Chỉ đóng gói Windows Single-File (.exe)
echo [3] Chỉ đóng gói Linux Single-File (standalone binary)
echo [4] Biên dịch kiểm tra lỗi toàn bộ Solution (dotnet build)
echo [5] Chạy ứng dụng ngay trên Windows (Run App)
echo [0] Thoát
echo.

set "CHOICE=1"
set /p "CHOICE=Chọn tác vụ [Mặc định: 1]: "

if "%CHOICE%"=="0" goto :eof

if "%CHOICE%"=="1" (
    echo.
    echo [*] Đang thực thi đóng gói toàn bộ nền tảng vào thư mục dist\...
    powershell -ExecutionPolicy Bypass -File "%~dp0publish-all-platforms.ps1"
    goto :done
)

if "%CHOICE%"=="2" (
    echo.
    echo [*] Đang đóng gói Windows x64 (Single-File Self-Contained)...
    set "DIST_WIN=%~dp0dist\windows"
    if not exist "%~dp0dist" mkdir "%~dp0dist"
    if not exist "!DIST_WIN!" mkdir "!DIST_WIN!"
    
    dotnet publish "%~dp0RecordVideoAudio.GMTPC.Desktop\RecordVideoAudio.GMTPC.Desktop.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o "!DIST_WIN!"
    if exist "!DIST_WIN!\RecordVideoAudio.GMTPC.Desktop.exe" (
        copy /y "!DIST_WIN!\RecordVideoAudio.GMTPC.Desktop.exe" "%~dp0dist\RecordVideoAudio.GMTPC.exe" > nul
        echo.
        echo [OK] Đã xuất bản: %~dp0dist\RecordVideoAudio.GMTPC.exe
    )
    goto :done
)

if "%CHOICE%"=="3" (
    echo.
    echo [*] Đang đóng gói Linux x64 (Single-File Self-Contained)...
    set "DIST_LINUX=%~dp0dist\linux"
    if not exist "%~dp0dist" mkdir "%~dp0dist"
    if not exist "!DIST_LINUX!" mkdir "!DIST_LINUX!"
    
    dotnet publish "%~dp0RecordVideoAudio.GMTPC.Desktop\RecordVideoAudio.GMTPC.Desktop.csproj" -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o "!DIST_LINUX!"
    if exist "!DIST_LINUX!\RecordVideoAudio.GMTPC.Desktop" (
        copy /y "!DIST_LINUX!\RecordVideoAudio.GMTPC.Desktop" "%~dp0dist\RecordVideoAudio.GMTPC-linux" > nul
        echo.
        echo [OK] Đã xuất bản: %~dp0dist\RecordVideoAudio.GMTPC-linux
    )
    goto :done
)

if "%CHOICE%"=="4" (
    echo.
    echo [*] Đang biên dịch kiểm tra lỗi toàn bộ Solution...
    dotnet build "%~dp0RecordVideoAudio.GMTPC.slnx" -c Release
    goto :done
)

if "%CHOICE%"=="5" (
    echo.
    echo [*] Đang khởi chạy ứng dụng Record Video Audio GMTPC...
    if exist "%~dp0dist\RecordVideoAudio.GMTPC.exe" (
        start "" "%~dp0dist\RecordVideoAudio.GMTPC.exe"
    ) else (
        dotnet run --project "%~dp0RecordVideoAudio.GMTPC.Desktop\RecordVideoAudio.GMTPC.Desktop.csproj" -c Release
    )
    goto :eof
)

:done
echo.
echo ==========================================================
echo    HOÀN TẤT QUY TRÌNH BUILD!
echo ==========================================================
echo Thư mục xuất bản: %~dp0dist\
echo.
pause
