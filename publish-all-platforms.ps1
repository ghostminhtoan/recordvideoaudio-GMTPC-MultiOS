# Script đóng gói toàn bộ các nền tảng vào chung một thư mục dist
$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot
$distDir = Join-Path $rootDir "dist"

# Đóng tiến trình cũ nếu đang mở để tránh bị lock file
Get-CimInstance Win32_Process -Filter "name like 'RecordVideoAudio%'" -ErrorAction SilentlyContinue | Invoke-CimMethod -MethodName Terminate -ErrorAction SilentlyContinue | Out-Null
Stop-Process -Name "RecordVideoAudio*" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   PACKAGING MULTIOS APPS - RECORD VIDEO AUDIO GMTPC      " -ForegroundColor Yellow
Write-Host "   (Self-Contained Single-File Executable Packaging)      " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

# Tạo thư mục dist duy nhất nếu chưa có
if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

$winDir = Join-Path $distDir "windows"
$linuxDir = Join-Path $distDir "linux"
$androidDir = Join-Path $distDir "android"

# Dọn dẹp các file dll / pdb runtime cũ rải rác ngoài thư mục gốc dist
Get-ChildItem -Path $distDir -File -Filter "*.dll" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $distDir -File -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

function Safe-CopyExecutable {
    param([string]$Source, [string]$Destination)
    try {
        if (Test-Path $Destination) {
            $oldFile = "$Destination.old"
            Remove-Item $oldFile -Force -ErrorAction SilentlyContinue
            Move-Item $Destination $oldFile -Force -ErrorAction SilentlyContinue
        }
        Copy-Item $Source $Destination -Force
        Remove-Item "$Destination.old" -Force -ErrorAction SilentlyContinue
    } catch {
        Write-Warning "Không thể ghi đè $($Destination): $_"
    }
}

# 1. Xuất bản Windows x64 (Self-Contained Single-File)
Write-Host "`n[1/3] Đóng gói nền tảng Windows (x64) - Single-File Self-Contained..." -ForegroundColor Green
dotnet publish "$rootDir\RecordVideoAudio.GMTPC.Desktop\RecordVideoAudio.GMTPC.Desktop.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -o $winDir

if (Test-Path "$winDir\RecordVideoAudio.GMTPC.Desktop.exe") {
    Safe-CopyExecutable "$winDir\RecordVideoAudio.GMTPC.Desktop.exe" "$distDir\RecordVideoAudio.GMTPC.exe"
}

# Sao chép ffmpeg phụ trợ vào thư mục phụ windows nội bộ (không để rải rác ở gốc dist)
$vibeDir = "C:\Users\Admin\AppData\Local\vibe"
if (Test-Path "$vibeDir\ffmpeg.exe") {
    Copy-Item "$vibeDir\ffmpeg.exe", "$vibeDir\*.dll" $winDir -Force -ErrorAction SilentlyContinue
}

# 2. Xuất bản Linux x64 (Self-Contained Single-File)
Write-Host "`n[2/3] Đóng gói nền tảng Linux (x64) - Single-File Self-Contained..." -ForegroundColor Green
dotnet publish "$rootDir\RecordVideoAudio.GMTPC.Desktop\RecordVideoAudio.GMTPC.Desktop.csproj" `
    -c Release -r linux-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -o $linuxDir

if (Test-Path "$linuxDir\RecordVideoAudio.GMTPC.Desktop") {
    Safe-CopyExecutable "$linuxDir\RecordVideoAudio.GMTPC.Desktop" "$distDir\RecordVideoAudio.GMTPC-linux"
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

# Xóa triệt để các file dll / pdb rác nếu vô tình sinh ra tại gốc dist và thư mục con
Get-ChildItem -Path $distDir -File -Filter "*.dll" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $distDir -File -Filter "*.pdb" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
if (Test-Path "$distDir\ffmpeg.exe") {
    Remove-Item -Force "$distDir\ffmpeg.exe" -ErrorAction SilentlyContinue
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "   DANH SÁCH FILE CHẠY TRONG THƯ MỤC CHUNG (DIST):       " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
Get-ChildItem -Path $distDir -File | Select-Object Name, @{Name="Kích thước (MB)";Expression={[math]::Round($_.Length / 1MB, 2)}}, LastWriteTime | Format-Table -AutoSize
