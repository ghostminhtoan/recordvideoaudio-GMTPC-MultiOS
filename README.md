# Record Video Audio - MultiOS - GMTPC 🎥🎙️

Ứng dụng quay phim màn hình, xử lý hậu kỳ và phòng thu âm thanh thời gian thực (**Vocal Studio & Karaoke FX Rack**) chuyên nghiệp, đa nền tảng (**Windows, Linux, Android**) phát triển trên nền tảng **Avalonia UI (.NET 10)**.

Được thiết kế theo ngôn ngữ **Cyberpunk / Modern Dark** đặc trưng của hệ sinh thái GMTPC: giao diện sắc nét, tương phản cao, đèn viền Neon công nghệ, font số Monospace kỹ thuật số, đồng hồ thời gian thực và đồng hồ đo VU Meter phần cứng chuẩn xác.

---

## 📑 MỤC LỤC
- [1. Bảng Thông Số Kỹ Thuật (Tech Specs Matrix)](#1-bảng-thông-số-kỹ-thuật-tech-specs-matrix)
- [2. Quay Màn Hình & Video Codecs](#2-quay-màn-hình--video-codecs)
- [3. Audio Mixer & Ma Trận 3 Track Chuẩn OBS Studio](#3-audio-mixer--ma-trận-3-track-chuẩn-obs-studio)
- [4. Tự Động Giảm Nhạc Khi Nói (Speaker Auto Ducking)](#4-tự-động-giảm-nhạc-khi-nói-speaker-auto-ducking)
- [5. Vocal Studio & Karaoke FX Rack (Bàn Điều Khiển Độc Lập)](#5-vocal-studio--karaoke-fx-rack-bàn-điều-khiển-độc-lập)
  - [Tab 1: Karaoke & Không Gian (Echo & Reverb)](#tab-1-karaoke--không-gian-echo--reverb)
  - [Tab 2: Auto-Tune & Đổi Giọng (Pitch Shifter)](#tab-2-auto-tune--đổi-giọng-pitch-shifter)
  - [Tab 3: Bộ Lọc Studio (Studio Polish & AI Denoise)](#tab-3-bộ-lọc-studio-studio-polish--ai-denoise)
  - [Tab 4: Tự Động Đo Độ Trễ (Zero Latency Detector)](#tab-4-tự-động-đo-độ-trễ-zero-latency-detector)
- [6. Quản Lý Vòng Đời & Thoát Sạch 100% (Clean Lifecycle)](#6-quản-lý-vòng-đời--thoát-sạch-100-clean-lifecycle)
- [7. Hệ Thống Phím Tắt Toàn Cục (Global Hotkeys)](#7-hệ-thống-phím-tắt-toàn-cục-global-hotkeys)
- [8. Thư Mục Xuất Bản Tập Trung (Dist Target)](#8-thư-mục-xuất-bản-tập-trung-dist-target)
- [9. Hướng Dẫn Biên Dịch & Đóng Gói (Build & Publish)](#9-hướng-dẫn-biên-dịch--đóng-gói-build--publish)

---

## 1. BẢNG THÔNG SỐ KỸ THUẬT (TECH SPECS MATRIX)

| Tiêu chí | Thông số kỹ thuật |
| :--- | :--- |
| **Nền tảng hỗ trợ** | Windows 10/11 (x64), Linux (x64 glibc), Android (API 23+) |
| **Framework & Runtime** | .NET 10.0, C# 13, Avalonia UI 11.2 (Fluent Cyberpunk Dark Theme) |
| **Phương thức đóng gói** | **Single-File Self-Contained** (Không cần cài đặt .NET Runtime rời) |
| **Container Video** | **MKV** (Chống hỏng file khi crash/mất nguồn), **MP4** (FastStart chuẩn mạng xã hội) |
| **Video Codec** | **H.264 / AVC** (Tương thích phổ quát), **HEVC / H.265** (Nén 4K siêu nét) |
| **Audio Codec** | **AAC** (192 kbps chất lượng cao), **MP3** (192 kbps tương thích đa dụng) |
| **Tăng tốc phần cứng** | NVIDIA NVENC, Intel QuickSync (QSV), AMD AMF, Linux VAAPI, Android MediaCodec |
| **Điều khiển Bitrate** | **CRF** (0-51), **CQP** (0-51 cho GPU), **CBR** (Stream), **VBR** (Dung lượng kiểm soát) |
| **Tốc độ khung hình (FPS)** | 24, 30, 60, 120 FPS |
| **Chế độ quay hình** | Full Screen, Custom Area, Active Window, Webcam PiP (3 kích thước, 4 góc) |
| **Xử lý âm thanh (DSP)** | WASAPI Loopback 32-bit Float, Auto Ducking, Echo, Reverb, Auto-Tune, Pitch Shifter, AI Denoise |
| **Độ trễ xử lý (Latency)** | Tiệm cận 0ms (Zero Latency) với công nghệ đồng bộ Cross-Correlation & Matched Filter |

---

## 2. QUAY MÀN HÌNH & VIDEO CODECS

- **4 Chế độ ghi hình linh hoạt**:
  - 🖥️ **Toàn màn hình (Full Screen)**: Quay toàn bộ không gian làm việc với độ phản hồi tức thì.
  - 📐 **Vùng tùy chọn (Custom Area)**: Chọn tọa độ `AreaX`, `AreaY`, `Width`, `Height` theo ý muốn.
  - 🪟 **Cửa sổ ứng dụng (Active Window)**: Khóa cố định quay đúng 1 ứng dụng hoặc trò chơi cụ thể.
  - 📷 **Webcam PiP (Picture-in-Picture)**: Lồng camera với 3 kích cỡ (*Small, Medium, Large*) và 4 vị trí góc màn hình (*Bottom-Right, Bottom-Left, Top-Right, Top-Left*).
- **Tăng tốc phần cứng GPU tự động**: Tự động phát hiện GPU rời (NVIDIA, Intel, AMD) để chuyển tải giải mã/mã hóa, giảm tải tối đa cho CPU.
- **Thanh Mini Floating Bar**: Thu nhỏ thành một thanh điều khiển nổi nhỏ gọn trên màn hình khi đang quay, hỗ trợ xem đồng hồ bấm giờ, dung lượng file, FPS hiện tại và nút Tạm dừng / Dừng nhanh.

---

## 3. AUDIO MIXER & MA TRẬN 3 TRACK CHUẨN OBS STUDIO

Hệ thống âm thanh được xây dựng trên lõi **Windows WASAPI Hardware Loopback** với ma trận định tuyến 3 track âm thanh độc lập:

```text
               ┌────────────────────────┐
               │  Loa / Âm thanh máy    │
               └───────────┬────────────┘
                           │ (Ducking / Gain / Offset)
                           ▼
 ┌───────────────┐   ┌───────────┐   ┌───────────────────────────┐
 │ Micro & Vocal ├──►│ Audio DSP ├──►│ Track 1: Mix Loa + Micro  │ (Xem ngay mọi trình phát)
 └───────────────┘   └───────────┘   ├───────────────────────────┤
                           │         │ Track 2: Micro độc lập    │ (Lồng tiếng / Edit vocal)
                           │         ├───────────────────────────┤
                           └────────►│ Track 3: Loa độc lập      │ (Cân chỉnh âm lượng game/nhạc)
                                     └───────────────────────────┘
```

- **Track 1**: Trộn chung (Loa + Micro) giúp xem lại video trực tiếp mà không cần cấu hình luồng âm thanh.
- **Track 2**: Chỉ tiếng Micro độc lập (đã qua lọc ồn AI và hiệu ứng phòng thu), phục vụ hậu kỳ trong Premiere, CapCut, DaVinci Resolve.
- **Track 3**: Chỉ tiếng Loa / Game / Nhạc nền độc lập, giúp chỉnh âm lượng nhạc nền mà không đè giọng nói.

---

## 4. TỰ ĐỘNG GIẢM NHẠC KHI NÓI (SPEAKER AUTO DUCKING)

- **Nguyên lý hoạt động**:
  - Theo dõi liên tục đường bao năng lượng giọng nói của Micro (`_micVoiceEnvelope`).
  - Khi phát hiện người dùng bắt đầu nói hoặc hát (`_micVoiceEnvelope > 0.012f`), âm lượng loa/nhạc nền tự động được hạ xuống **-14dB (20%)**.
  - **Làm mượt tự nhiên (Smooth Envelope Attack & Release)**:
    - **Fast Attack (~20ms)**: Âm lượng loa hạ ngay lập tức khi phát âm, không bị lọt âm đầu câu.
    - **Smooth Release (~400ms)**: Âm lượng loa từ từ phục hồi nhẹ nhàng về 100% khi dứt lời, không gây cảm giác giật cục hay méo tiếng (pumping).
- **Điều khiển tiện lợi**: Có Checkbox Bật/Tắt tức thì ngay tại Channel 1: Speaker Input trên giao diện chính và tại Tab 3 của bàn Vocal Studio.

---

## 5. VOCAL STUDIO & KARAOKE FX RACK (BÀN ĐIỀU KHIỂN ĐỘC LẬP)

Cửa sổ Vocal Studio được thiết kế hoạt động **hoàn toàn độc lập** với cửa sổ chính (MainWindow):
- Có biểu tượng taskbar riêng biệt.
- Khi người dùng **Minimize** cửa sổ Studio xuống taskbar, **cửa sổ chính MainWindow không bị ảnh hưởng, không bị ẩn hay tắt**.
- Toàn bộ tab điều khiển được cấu hình bằng màu **Cyan công nghệ cao (#00F0FF)** sáng rực rỡ ở mọi trạng thái.

### Tab 1: Karaoke & Không Gian (Echo & Reverb)
- **Karaoke Stereo Echo (Delay lặp tiếng)**:
  - Tái hiện hiệu ứng tiếng vang lặp lại của dàn Karaoke gia đình và phòng thu âm.
  - Tùy chỉnh: Độ trễ Delay (50ms - 500ms), Độ vang vọng Feedback (0% - 90%), Tỷ lệ tiếng vang Wet Mix (0% - 100%).
- **Schroeder-Moorer Studio Reverb (Âm vang không gian)**:
  - Mô phỏng không gian phòng hòa nhạc (Concert Hall) và phòng thu (Studio Plate) giúp giọng hát dày dặn, ấm áp và bay bổng.
  - Tùy chỉnh: Kích thước phòng Room Size (10% - 95%), Hấp thụ dải cao Damping (0% - 100%), Độ hòa trộn Wet Mix (0% - 100%).

### Tab 2: Auto-Tune & Đổi Giọng (Pitch Shifter)
- **Auto-Tune Pitch Correction**:
  - Tự động nắn chỉnh cao độ giọng hát theo thang âm chuẩn phòng thu.
  - Hỗ trợ đầy đủ **12 cung nốt nhạc** (Key C, Db, D, Eb, E, F, Gb, G, Ab, A, Bb, B).
  - Hỗ trợ **3 thang âm**: Chromatic (Bán âm tự do), Major (Trưởng), Minor (Thứ).
  - Tốc độ can thiệp giọng nói (Speed): Từ 0ms (Hard Robot kiểu Travis Scott) đến 100ms (nhẹ nhàng, tự nhiên).
- **Real-Time Pitch Shifter (Đổi giọng thời gian thực)**:
  - Dịch chuyển cao độ giọng nói từ **-12 bán âm** (giọng trầm, quái vật, nam trầm) đến **+12 bán âm** (giọng sóc chuột, trẻ em, nữ cao).
  - Thuật toán pha liên tục (**Continuous Phase Overlap-Add**): Chuyển đổi mượt mà, không bị đứt quãng hay rè tiếng.

### Tab 3: Bộ Lọc Studio (Studio Polish & AI Denoise)
- **Lọc ồn AI (RNNoise Engine)**: Triệt tiêu 100% tiếng ồn quạt tản nhiệt, gõ phím cơ, tiếng sôi xì micro và tiếng xe cộ ngoài đường.
- **High-Pass Filter 80Hz**: Cắt toàn bộ dải siêu trầm gây ù do rung bàn phím hoặc luồng hơi thở.
- **Smooth Noise Gate**: Cổng ngắt tiếng ồn tự động khi ngừng nói, có thời gian giữ 350ms Hold Time để không bị cụt âm cuối từ.
- **Dynamic Compressor (Chống vỡ rè)**: Tự động ghìm âm lượng khi hét to hoặc hát nốt cao, bảo vệ tai người nghe và ngăn méo tiếng 100%.
- **De-Esser**: Khử êm dịu các âm xì chói tai 's', 'x', 'ch'.
- **3-Band Vocal EQ**: Lựa chọn 4 cấu hình âm sắc: *Natural*, *Broadcast Warmth*, *Crystal Clear*, *Podcast Studio*.
- **Auto Ducking Toggle**: Bật/tắt tính năng tự giảm nhạc khi nói.

### Tab 4: Tự Động Đo Độ Trễ (Zero Latency Detector)
Giải quyết triệt để vấn đề hát karaoke bị chậm tiếng hoặc lệch nhịp so với nhạc nền:
- **Phương pháp 1 (Hold-To-Measure khi hát theo nhạc)**:
  - Mở bài hát karaoke trên loa, **nhấn và giữ chuột** vào nút đo và hát theo lời nhạc 2-5 giây, sau đó **buông chuột ra**.
  - Hệ thống tự động phân tích tương quan chéo đường bao năng lượng (**Energy Envelope Normalized Cross-Correlation**) giữa luồng Loa và luồng Micro để tìm ra chính xác độ trễ (ms).
- **Phương pháp 2 (Pulse Calibration)**:
  - Phát 1 xung âm bíp 25ms qua loa và dùng bộ lọc **Matched Filter** đo độ trễ phần cứng với độ chính xác `±1ms`.
- **Nút "✅ Áp dụng vào Mic Offset"**: Tự động bù trừ độ lệch tiếng vào hệ thống chỉ với một cú click chuột.

---

## 6. QUẢN LÝ VÒNG ĐỜI & THOÁT SẠCH 100% (CLEAN LIFECYCLE)

- Thiết lập `ShutdownMode.OnMainWindowClose` tại `App.axaml.cs`.
- Tự động đóng toàn bộ các cửa sổ phụ (`VocalStudioWindow`, `FloatingMiniBarWindow`).
- Tự động giải phóng Win32 Keyboard Hook (`UnhookWindowsHookEx`), dọn dẹp các đối tượng COM của Windows Audio (WASAPI), giải phóng Timer và dừng tiến trình FFmpeg dở dang.
- Thực hiện thoát dứt điểm `Environment.Exit(0)`: **Bảo đảm 100% không còn bất kỳ tiến trình nào chạy ngầm sau khi đóng cửa sổ chính**.

---

## 7. HỆ THỐNG PHÍM TẮT TOÀN CỤC (GLOBAL HOTKEYS)

Hoạt động xuyên suốt ngay cả khi đang chơi game Fullscreen hoặc ứng dụng đang ở chế độ nền:
- **Bắt đầu / Dừng quay (Record / Stop)**:
  - Mặc định: `Ctrl + Alt + Shift + D5`
  - Phím dự phòng: `F8`
- **Tạm dừng / Tiếp tục (Pause / Resume)**:
  - Mặc định: `Ctrl + Alt + Shift + D8`
  - Phím dự phòng: `F9`
- Hỗ trợ người dùng tự do thiết lập lại tổ hợp phím theo nhu cầu sử dụng.

---

## 8. THƯ MỤC XUẤT BẢN TẬP TRUNG (DIST TARGET)

Toàn bộ gói cài đặt và file chạy độc lập của mọi nền tảng được thu thập về **CHUNG MỘT THƯ MỤC DUY NHẤT** tại:

```text
r:\HDD R\ZC SYMLINK\USERS\source\repos\ghostminhtoan\record video - GMTPC\dist\
├── RecordVideoAudio.GMTPC.exe       # Windows (x64) Single-File Self-Contained (~47 MB)
├── RecordVideoAudio.GMTPC-linux     # Linux (x64) Single-File Self-Contained (~47 MB)
├── RecordVideoAudio.GMTPC.apk       # Android Signed APK Package (~64 MB)
├── windows/                         # Thư mục Windows độc lập
├── linux/                           # Thư mục Linux độc lập
└── android/                         # Thư mục Android độc lập
```

---

## 9. HƯỚNG DẪN BIÊN DỊCH & ĐÓNG GÓI (BUILD & PUBLISH)

### Yêu cầu môi trường
- .NET SDK 10.0 trở lên.
- PowerShell 7 hoặc Windows PowerShell 5.1.
- Android SDK (dành cho đóng gói Android APK).

### Lệnh biên dịch kiểm tra (Bắt buộc 0 Error, 0 Warning)
```powershell
dotnet build -c Release
```

### Lệnh đóng gói tự động cho toàn bộ nền tảng vào thư mục `dist/`
```powershell
powershell -ExecutionPolicy Bypass -File .\publish-all-platforms.ps1
```

---

## 📜 BẢN QUYỀN & TÁC GIẢ
- **Tác giả**: GMTPC (ghostminhtoan)
- **Email liên hệ**: ghostminhtoan@gmail.com
- **Mã nguồn GitHub**: [ghostminhtoan/recordvideoaudio-GMTPC-MultiOS](https://github.com/ghostminhtoan/recordvideoaudio-GMTPC-MultiOS)
- **Nhánh chính thức**: `main`
