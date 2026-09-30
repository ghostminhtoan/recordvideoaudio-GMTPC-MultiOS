# Workflow chuẩn dự án - record video audio - MultiOS - GMTPC

Quy chuẩn kỹ thuật, kiến trúc và luồng phát triển cho ứng dụng Record Video Audio đa nền tảng GMTPC (Windows, Linux, Android) sử dụng Avalonia UI (.NET 10).

---

## 1. NGUYÊN TẮC CỐT LÕI (CORE PRINCIPLES)
- **Ngôn ngữ phản hồi**: Luôn luôn trả lời bằng Tiếng Việt.
- **Tuân thủ First-Principles & Teamwork Preview Protocol**:
  - Không bloatware, không abstraction suy đoán (YAGNI & KISS).
  - Tối ưu hiệu năng CPU/GPU và bộ nhớ RAM.
  - Xử lý bất đồng bộ (`async/await`) chuẩn chỉ, luôn tôn trọng `CancellationToken`.
- **Giao diện đa nền tảng (Avalonia UI)**:
  - Chia sẻ 100% tầng UI XAML và ViewModel giữa Desktop (Windows, Linux) và Mobile (Android).
  - Phong cách thiết kế: Cyberpunk / Modern Dark đặc trưng của hệ sinh thái GMTPC (tương phản cao, viền led neon tinh tế, font số monospace kỹ thuật số, đồng hồ thời gian thực, đồng hồ VU meter đo âm thanh).
  - Hỗ trợ song ngữ: Tiếng Việt (VI) và Tiếng Anh (EN) chuyển đổi tức thì.
- **Tiêu chuẩn Build & Release**:
  - Biên dịch sạch sẽ: Bắt buộc `0 error, 0 warning`.
  - Tự động commit local sau khi hoàn thiện và đưa mã hash commit cho người dùng.
  - Tuyệt đối KHÔNG commit hoặc push lên GitHub nếu người dùng chưa yêu cầu rõ ràng.
  - **Quy chuẩn thư mục xuất bản (Distribution Target)**: Toàn bộ file chạy / gói cài đặt của mọi nền tảng (Windows `.exe`, Linux standalone binary, Android `.apk`) bắt buộc phải được thu thập và xuất về **CHUNG MỘT THƯ MỤC DUY NHẤT** tại:
    `r:\HDD R\ZC SYMLINK\USERS\source\repos\ghostminhtoan\record video - GMTPC\dist\`
- **Định dạng báo cáo trạng thái bắt buộc cuối câu trả lời**:
  ```text
  commit local: <mã hash>
  commit github: "không"
  path toàn bộ file chạy của các nền tảng phải ở chung một folder: <đường dẫn thư mục dist>
  ```

---

## 2. BẢN ĐỒ TÍNH NĂNG VÀ THÔNG SỐ KỸ THUẬT

### A. Định dạng Container
- **MKV (Matroska)**: Khuyến nghị chính cho quay offline và lưu trữ dài. Chống hỏng file khi ứng dụng bị tắt đột ngột hoặc mất nguồn điện. Hỗ trợ nhiều luồng âm thanh độc lập.
- **MP4 (MPEG-4 Part 14)**: Chuẩn phổ quát chia sẻ mạng xã hội, tương thích 100% Android, Windows, Mac, Web, CapCut, Premiere. Cấu hình cờ `faststart` (moov atom ở đầu file).
- *(Đã loại bỏ hoàn toàn định dạng AVI theo quyết định kỹ thuật nhằm tránh desync âm thanh và không tương thích HEVC)*.

### B. Bộ mã hóa Video (Video Codecs)
- **H.264 / AVC**: Tương thích tối đa với mọi máy yếu, mọi thiết bị xem lại.
- **HEVC / H.265**: Tối ưu dung lượng (giảm 40-50% dung lượng so với H.264 ở cùng chất lượng), tối ưu cho độ phân giải 2K/4K.

### C. Bộ mã hóa Âm thanh (Audio Codecs)
- **AAC (Advanced Audio Coding)**: Chuẩn âm thanh chất lượng cao, độ trễ thấp, tương thích toàn diện.
- **MP3 (MPEG Audio Layer III)**: Chuẩn âm thanh phổ thông cho các nhu cầu tương thích đa năng.

### D. Cơ chế điều khiển tốc độ bit (Rate Control Modes)
1. **CRF (Constant Rate Factor)**:
   - Dải giá trị: 0 – 51 (Mặc định: 23 cho H.264, 28 cho HEVC).
   - Tối ưu cho quay màn hình trên CPU (chất lượng đồng đều, dung lượng tối ưu).
2. **CQP (Constant Quantization Parameter)**:
   - Khóa cố định QP cho I, P, B frame.
   - Chế độ vàng cho GPU Hardware Encoder (NVENC, QSV, AMF, MediaCodec) để chống giật lag và không phụ thuộc thuật toán bitrate.
3. **CBR (Constant Bitrate)**:
   - Bitrate cố định (ví dụ: 4000 kbps, 8000 kbps, 15000 kbps).
   - Phù hợp livestream và các hệ thống phát yêu cầu băng thông trần không đổi.
4. **VBR (Variable Bitrate)**:
   - Cho phép đặt Bitrate trung bình (Target) và Bitrate tối đa (Max Bitrate).
   - Giúp kiểm soát dung lượng file đầu ra không vượt quá giới hạn lưu trữ.

---

## 3. KIẾN TRÚC MÃ NGUỒN (LANE ARCHITECTURE)

```
RecordVideoAudio.GMTPC/
│
├── RecordVideoAudio.GMTPC/                # [Lane 1 & 4] Core Shared & Avalonia UI
│   ├── Models/                           # Data models: RecordingProfile, RateControlMode, VideoCodec, AudioCodec
│   ├── Services/                         # Interfaces & Core Services:
│   │   ├── IRecordingEngine.cs           # Engine điều phối Start/Pause/Resume/Stop
│   │   ├── IScreenCaptureService.cs      # Trừu tượng hóa Screen Capture
│   │   ├── IAudioCaptureService.cs       # Trừu tượng hóa Audio Loopback & Mic
│   │   ├── IEncoderPipelineService.cs    # Tạo lệnh/pipeline mã hóa cho MKV/MP4
│   │   └── AppSettingsService.cs         # Lưu trữ cấu hình Portable
│   ├── ViewModels/                       # MainViewModel, RecorderViewModel, SettingsViewModel
│   ├── Views/                            # MainWindow, MainView, OverlayBarView
│   ├── Localization/                     # Song ngữ VI/EN (Languages.cs)
│   └── Styles/                           # Theme GMTPC Dark Cyberpunk
│
├── RecordVideoAudio.GMTPC.Desktop/        # [Lane 3] Chạy trên Windows & Linux
│   ├── Program.cs                        # Entrypoint Desktop
│   └── Platform/                         # Native Capture Providers:
│       ├── Windows/                      # WGC / DXGI / WASAPI Providers
│       └── Linux/                        # PipeWire Portal / PulseAudio Providers
│
├── RecordVideoAudio.GMTPC.Android/        # [Lane 3] Chạy trên Android (API 23+)
│   ├── MainActivity.cs                   # Activity
│   ├── Application.cs                    # Application class
│   └── Platform/                         # MediaProjection, AudioRecord, MediaCodec
│
└── dist/                                 # [Lane 5] Thư mục ĐÍCH duy nhất chứa toàn bộ file chạy
    ├── windows/                          # File chạy Windows
    ├── linux/                            # File chạy Linux
    └── android/                          # File gói cài đặt APK Android
```
