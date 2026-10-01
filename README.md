# Record Video Audio - MultiOS - GMTPC 🎥🎙️

Ứng dụng quay phim màn hình và phòng thu âm thanh (Vocal Studio & Karaoke FX) chuyên nghiệp, đa nền tảng (**Windows, Linux, Android**) phát triển trên nền tảng **Avalonia UI (.NET 10)**.

Thiết kế theo phong cách **Cyberpunk / Modern Dark** đặc trưng của hệ sinh thái GMTPC: giao diện sắc nét tương phản cao, đèn LED neon tinh tế, font số Monospace kỹ thuật số, đồng hồ thời gian thực và đồng hồ đo âm lượng VU Meter phần cứng chuẩn xác.

---

## 🌟 TÍNH NĂNG NỔI BẬT

### 1. Quay Màn Hình Chuyên Nghiệp (Screen Recording)
- **4 Chế độ ghi hình linh hoạt**:
  - 🖥️ **Toàn màn hình (Full Screen)**: Quay toàn bộ màn hình với FPS cực cao.
  - 📐 **Vùng tùy chọn (Custom Area)**: Chọn vùng tọa độ và kích thước khung hình cần quay.
  - 🪟 **Cửa sổ phần mềm (Active Window)**: Khóa cố định quay đúng 1 ứng dụng hoặc trò chơi cụ thể.
  - 📷 **Webcam PiP (Picture-in-Picture)**: Lồng khung hình camera với 3 kích thước (*Small, Medium, Large*) và 4 góc màn hình tùy chọn.
- **Bộ mã hóa Video chuẩn công nghiệp**:
  - **H.264 / AVC**: Tương thích 100% với mọi thiết bị xem lại, máy cấu hình yếu, mạng xã hội.
  - **HEVC / H.265**: Chuẩn nén thế hệ mới, giảm 40-50% dung lượng ở cùng độ phân giải 2K / 4K.
- **Tăng tốc phần cứng đa nền tảng (Hardware Acceleration)**:
  - Tự động nhận diện và kích hoạt: **NVIDIA NVENC**, **Intel QuickSync (QSV)**, **AMD AMF**, **Linux VAAPI**, **Android MediaCodec**, hoặc **Software CPU**.
- **4 Cơ chế điều khiển Bitrate chuyên sâu (Rate Control Modes)**:
  - **CRF (Constant Rate Factor)**: Tối ưu tự động giữa chất lượng và dung lượng file cho CPU.
  - **CQP (Constant Quantization Parameter)**: Chuẩn vàng cho GPU Card rời (NVENC/QSV/AMF), chống drop frame và giật lag tuyệt đối.
  - **CBR (Constant Bitrate)**: Khóa trần băng thông cố định cho livestream và phát trực tuyến.
  - **VBR (Variable Bitrate)**: Tùy biến Target Bitrate và Max Bitrate linh hoạt.
- **Tùy chỉnh tốc độ khung hình**: 24 FPS, 30 FPS, 60 FPS, 120 FPS.
- **Định dạng Container an toàn**:
  - **MKV (Matroska)**: Khuyến nghị hàng đầu, chống hỏng file (Crash-Proof) khi máy tính bị mất điện đột ngột hoặc tắt ứng dụng ngang.
  - **MP4 (FastStart)**: Cấu hình moov atom ở đầu file, chia sẻ mạng xã hội (YouTube, TikTok, Facebook) xem được ngay.
- **Thanh Mini Floating Bar**: Thu nhỏ thành thanh nổi tiện lợi khi đang quay, ghim góc màn hình.

---

### 2. Audio Mixer & Ma Trận Track Chuẩn OBS Studio
- **Đồng hồ VU Meter phần cứng thời gian thực**: Đo cường độ âm thanh Loa và Micro độc lập với tần số làm tươi cao (16+ FPS).
- **Hệ thống ma trận 3 Track âm thanh độc lập**:
  - **Track 1**: Trộn chung (Mix Loa + Micro) để phát lại trực tiếp trên mọi phần mềm xem video mà không cần chọn luồng.
  - **Track 2**: Chỉ tiếng Micro độc lập (thuận tiện cho việc lồng tiếng, lọc tạp âm và mix vocal trong Premiere, CapCut, DaVinci Resolve).
  - **Track 3**: Chỉ tiếng Loa / Game / Âm thanh máy tính độc lập (thuận tiện tinh chỉnh âm lượng nhạc nền không ảnh hưởng giọng nói).
- **🦆 Tự giảm âm lượng Loa khi nói (Speaker Auto Ducking)**:
  - Tự động hạ nhỏ âm lượng loa/nhạc nền xuống **-14dB (20%)** khi micro phát hiện giọng nói hoặc tiếng hát.
  - Thuật toán Envelope DSP làm mượt với **Fast Attack (~20ms)** và **Smooth Release (~400ms)**: Không gây hiện tượng giật tiếng hay méo âm (pumping artifacts).
  - Tự động phục hồi 100% âm lượng ban đầu khi người dùng ngừng nói.
  - Checkbox Bật/Tắt tức thì ngay tại Channel 1: Speaker Input và trong Studio FX.
- **Bù trừ độ trễ đồng bộ (Audio Sync Offset)**:
  - Tinh chỉnh bù trừ lệch tiếng Loa và Micro từ `-500ms` đến `+1000ms`.
  - Tăng/giảm khuếch đại âm lượng độc lập từ `-50.0 dB` đến `+50.0 dB`.

---

### 3. Vocal Studio & Karaoke FX Rack (Bàn Điều Khiển Độc Lập)
Cửa sổ Vocal Studio hoạt động **hoàn toàn độc lập** với MainWindow:
- Có biểu tượng riêng trên thanh Taskbar.
- Khi Minimize cửa sổ Studio, cửa sổ chính MainWindow vẫn hoạt động bình thường, không bị ẩn hay tắt.
- Toàn bộ tab điều khiển được phủ màu **Cyan công nghệ cao** sáng rõ ở mọi trạng thái.

#### Gồm 4 Tab tính năng chuyên sâu:
1. 🎤 **KARAOKE & KHÔNG GIAN (ECHO & REVERB)**:
   - **Karaoke Stereo Echo**: Mô phỏng tiếng vang lặp lại đặc trưng của dàn âm thanh phòng hát Karaoke gia đình và sân khấu chuyên nghiệp. Tùy chỉnh độ trễ Delay (50ms - 500ms), độ vang vọng Feedback (0 - 90%), tỷ lệ trộn Wet Mix (0 - 100%).
   - **Schroeder-Moorer Studio Reverb**: Tạo âm vang không gian phòng hòa nhạc (Concert Hall) hoặc phòng thu âm (Studio Plate) giúp giọng hát dày dặn, mượt mà và bay bổng hơn. Tùy chỉnh Room Size (10 - 95%), Hấp thụ dải cao Damping (0 - 100%), Wet Mix (0 - 100%).
2. 🎵 **AUTO-TUNE & ĐỔI GIỌNG (PITCH SHIFTER)**:
   - **Auto-Tune Pitch Correction**: Nắn chỉnh cao độ giọng hát theo chuẩn phòng thu thời gian thực. Hỗ trợ 12 cung nốt (Key C đến B), Scale (Chromatic, Major, Minor), và tốc độ can thiệp giọng nói Speed (từ 0ms Hard Robot kiểu Travis Scott đến 100ms nhẹ nhàng tự nhiên).
   - **Real-Time Pitch Shifter**: Dịch chuyển cao độ từ `-12` đến `+12` bán âm (giọng trầm nam, giọng cao nữ, giọng hoạt hình) với giải thuật pha liên tục (Continuous Phase Overlap-Add) không bị ngắt quãng hay giật cục âm thanh.
3. 🎙️ **BỘ LỌC STUDIO (STUDIO VOCAL POLISH)**:
   - **Lọc ồn AI (RNNoise Engine)**: Triệt tiêu sạch sẽ 100% tiếng ồn quạt máy tính, tiếng gõ phím cơ, ve kêu ngoài trời và tiếng sôi xì của micro.
   - **High-Pass Filter 80Hz**: Cắt sạch tần số siêu trầm do rung bàn phím hoặc hơi thở phả vào micro.
   - **Smooth Noise Gate**: Cổng ngắt ồn thông minh có thời gian giữ (350ms Hold Time) chống nuốt chữ cuối câu.
   - **Dynamic Compressor**: Tự động ghìm âm lượng khi người dùng nói to hoặc hét vào micro, triệt tiêu 100% hiện tượng vỡ rè tiếng.
   - **De-Esser**: Khử êm dịu các âm xì chói tai 's', 'x', 'ch'.
   - **3-Band Vocal EQ**: Lựa chọn các preset âm sắc chuyên biệt (*Natural, Broadcast Warmth, Crystal Clear, Podcast Studio*).
4. ⏱️ **TỰ ĐỘNG ĐO ĐỘ TRỄ (AUTO LATENCY DETECTOR - ZERO LATENCY)**:
   - **Phương pháp 1 (Hold-To-Measure khi hát theo nhạc karaoke)**:
     - Mở bài hát karaoke trên loa/tai nghe, **nhấn giữ chuột** vào nút đo và hát theo 2-5 giây, sau đó **buông chuột ra**.
     - Thuật toán phân tích tương quan chéo đường bao năng lượng (**Energy Envelope Normalized Cross-Correlation**) sẽ so sánh 2 luồng sóng âm Loa và Micro để tính ra chính xác độ lệch mili giây (ms).
   - **Phương pháp 2 (Pulse Calibration)**:
     - Phát xung âm bíp 25ms qua loa và dùng thuật toán phản xạ sóng **Matched Filter** đo độ trễ phần cứng với độ chính xác `±1ms`.
   - **Nút "✅ Áp dụng vào Mic Offset"**: Tự động bù trừ độ lệch tiếng vào hệ thống chỉ với một click chuột.

---

### 4. Hệ Thống Phím Tắt Toàn Cục (Global Hotkeys)
Hoạt động xuyên suốt ngay cả khi đang chơi game Fullscreen hoặc chạy ẩn:
- **Phím tắt Quay / Dừng (Record/Stop)**:
  - Mặc định: `Ctrl + Alt + Shift + D5`
  - Dự phòng: `F8`
- **Phím tắt Tạm dừng / Tiếp tục (Pause/Resume)**:
  - Mặc định: `Ctrl + Alt + Shift + D8`
  - Dự phòng: `F9`
- Hỗ trợ tùy biến linh hoạt tổ hợp phím theo ý thích người dùng.

---

### 5. Song Ngữ & Quản Lý Vòng Đời Tinh Gọn
- **Chuyển đổi ngôn ngữ tức thì**: Hỗ trợ đầy đủ **Tiếng Việt (VI)** và **Tiếng Anh (EN)**.
- **Thoát sạch 100% (Clean Exit)**:
  - Tắt cửa sổ chính MainWindow là ứng dụng giải phóng toàn bộ tài nguyên (Timers, WASAPI Audio, Win32 Hooks, FFmpeg processes) và thoát dứt điểm, **tuyệt đối không chạy ngầm**.

---

## 📁 CẤU TRÚC THƯ MỤC XUẤT BẢN (DIST)

Theo quy chuẩn kỹ thuật GMTPC, toàn bộ file thực thi độc lập (Single-File Self-Contained) của mọi nền tảng bắt buộc nằm chung trong **DUY NHẤT một thư mục**:

```text
r:\HDD R\ZC SYMLINK\USERS\source\repos\ghostminhtoan\record video - GMTPC\dist\
├── RecordVideoAudio.GMTPC.exe       # Windows (x64) Single-File Self-Contained (Không cần cài .NET)
├── RecordVideoAudio.GMTPC-linux     # Linux (x64) Single-File Self-Contained
├── RecordVideoAudio.GMTPC.apk       # Android Signed APK Package
├── windows/                         # Bản Windows kèm thư viện phụ trợ
├── linux/                           # Bản Linux độc lập
└── android/                         # Gói cài đặt Android
```

---

## 🛠️ HƯỚNG DẪN XÂY DỰNG & ĐÓNG GÓI (BUILD)

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
