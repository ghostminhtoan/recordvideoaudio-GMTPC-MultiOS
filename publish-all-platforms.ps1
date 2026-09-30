# Script đóng gói toàn bộ các nền tảng vào chung một thư mục dist
$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot
$distDir = Join-Path $rootDir "dist"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   PACKAGING MULTIOS APPS - RECORD VIDEO AUDIO GMTPC      " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

# Tạo thư mục dist duy nhất
if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

$winDir = Join-Path $distDir "windows"
$linuxDir = Join-Path $distDir "linux"
$androidDir = Join-Path $distDir "android"

# 1. Xuất bản Windows x64
Write-Host "`n[1/3] Đóng gói nền tảng Windows (x64)..." -ForegroundColor Green
dotnet publish "$rootDir\RecordVideoAudio.GMTPC.Desktop\RecordVideoAudio.GMTPC.Desktop.csproj" -c Release -r win-x64 --self-contained false -o $winDir
if (Test-Path "$winDir\RecordVideoAudio.GMTPC.Desktop.exe") {
    Copy-Item "$winDir\RecordVideoAudio.GMTPC.Desktop.exe" "$distDir\RecordVideoAudio.GMTPC.exe" -Force
}

# 2. Xuất bản Linux x64
Write-Host "`n[2/3] Đóng gói nền tảng Linux (x64)..." -ForegroundColor Green
dotnet publish "$rootDir\RecordVideoAudio.GMTPC.Desktop\RecordVideoAudio.GMTPC.Desktop.csproj" -c Release -r linux-x64 --self-contained false -o $linuxDir
if (Test-Path "$linuxDir\RecordVideoAudio.GMTPC.Desktop") {
    Copy-Item "$linuxDir\RecordVideoAudio.GMTPC.Desktop" "$distDir\RecordVideoAudio.GMTPC-linux" -Force
}

# 3. Thu thập gói Android APK
Write-Host "`n[3/3] Thu thập gói cài đặt Android (.apk)..." -ForegroundColor Green
if (-not (Test-Path $androidDir)) {
    New-Item -ItemType Directory -Path $androidDir -Force | Out-Null
}
$apkSources = Get-ChildItem -Path "$rootDir\RecordVideoAudio.GMTPC.Android\bin\" -Recurse -Filter "*.apk" | Sort-Object LastWriteTime -Descending
if ($apkSources.Count -gt 0) {
    $latestApk = $apkSources[0]
    Copy-Item $latestApk.FullName "$androidDir\RecordVideoAudio.GMTPC.apk" -Force
    Copy-Item $latestApk.FullName "$distDir\RecordVideoAudio.GMTPC.apk" -Force
    Write-Host "  -> Đã sao chép APK: $($latestApk.Name)" -ForegroundColor Gray
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "   DANH SÁCH FILE CHẠY TRONG THƯ MỤC CHUNG (DIST):       " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
Get-ChildItem -Path $distDir | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
