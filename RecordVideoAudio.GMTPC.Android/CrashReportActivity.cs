using System;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace RecordVideoAudio.GMTPC.Android;

[Activity(
    Label = "GMTPC - Báo cáo lỗi sự cố",
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    ConfigurationChanges = global::Android.Content.PM.ConfigChanges.Orientation | global::Android.Content.PM.ConfigChanges.ScreenSize,
    Exported = false)]
public class CrashReportActivity : Activity
{
    public const string ExtraErrorDetails = "extra_error_details";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        string errorDetails = Intent?.GetStringExtra(ExtraErrorDetails) ?? "Không có thông tin chi tiết về lỗi.";

        // Xây dựng giao diện bằng code thuần Android để không phụ thuộc vào XAML hay tài nguyên ngoài
        var rootLayout = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        rootLayout.SetBackgroundColor(Color.Rgb(15, 15, 22));
        rootLayout.SetPadding(40, 60, 40, 40);

        // Header Tiêu đề
        var titleText = new TextView(this)
        {
            Text = "🚨 ỨNG DỤNG GẶP SỰ CỐ (CRASH)",
            TextSize = 20,
            Typeface = Typeface.DefaultBold
        };
        titleText.SetTextColor(Color.Rgb(255, 60, 80));
        rootLayout.AddView(titleText);

        var subTitle = new TextView(this)
        {
            Text = "Đã bắt được thông tin lỗi crash chi tiết. Bạn có thể sao chép thông tin này để gửi báo cáo sửa lỗi:",
            TextSize = 13
        };
        subTitle.SetTextColor(Color.Rgb(180, 180, 200));
        var subParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        subParams.SetMargins(0, 16, 0, 24);
        subTitle.LayoutParameters = subParams;
        rootLayout.AddView(subTitle);

        // Khung cuộn chứa nội dung lỗi
        var scrollView = new ScrollView(this);
        var scrollParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1.0f);
        scrollView.LayoutParameters = scrollParams;
        scrollView.SetBackgroundColor(Color.Rgb(24, 24, 34));
        scrollView.SetPadding(24, 24, 24, 24);

        var contentText = new TextView(this)
        {
            Text = errorDetails,
            TextSize = 12,
            Typeface = Typeface.Monospace
        };
        contentText.SetTextColor(Color.Rgb(220, 220, 230));
        contentText.SetTextIsSelectable(true);
        scrollView.AddView(contentText);
        rootLayout.AddView(scrollView);

        // Hàng nút bấm chức năng
        var buttonLayout = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        var btnLayoutParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        btnLayoutParams.SetMargins(0, 30, 0, 0);
        buttonLayout.LayoutParameters = btnLayoutParams;

        // Nút Sao chép
        var copyButton = new Button(this)
        {
            Text = "📋 SAO CHÉP LỖI"
        };
        copyButton.SetBackgroundColor(Color.Rgb(0, 140, 220));
        copyButton.SetTextColor(Color.White);
        var copyParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1.0f);
        copyParams.SetMargins(0, 0, 16, 0);
        copyButton.LayoutParameters = copyParams;
        copyButton.Click += (s, e) =>
        {
            try
            {
                var clipboard = (ClipboardManager?)GetSystemService(ClipboardService);
                if (clipboard != null)
                {
                    var clip = ClipData.NewPlainText("GMTPC_Crash_Log", errorDetails);
                    clipboard.PrimaryClip = clip;
                    Toast.MakeText(this, "✅ Đã sao chép chi tiết lỗi vào bộ nhớ đệm!", ToastLength.Long)?.Show();
                }
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, $"Lỗi copy: {ex.Message}", ToastLength.Short)?.Show();
            }
        };
        buttonLayout.AddView(copyButton);

        // Nút Thoát
        var closeButton = new Button(this)
        {
            Text = "❌ THOÁT"
        };
        closeButton.SetBackgroundColor(Color.Rgb(60, 60, 75));
        closeButton.SetTextColor(Color.White);
        var closeParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1.0f);
        closeButton.LayoutParameters = closeParams;
        closeButton.Click += (s, e) =>
        {
            FinishAffinity();
            global::System.Diagnostics.Process.GetCurrentProcess().Kill();
        };
        buttonLayout.AddView(closeButton);

        rootLayout.AddView(buttonLayout);

        SetContentView(rootLayout);
    }
}

