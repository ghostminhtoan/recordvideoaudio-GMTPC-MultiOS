using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace RecordVideoAudio.GMTPC.Views;

public partial class FloatingMiniBarWindow : Window
{
    public FloatingMiniBarWindow()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    public void PositionAtTopRight()
    {
        var screen = Screens.Primary;
        if (screen != null)
        {
            var workingArea = screen.WorkingArea;
            Position = new PixelPoint((int)(workingArea.X + workingArea.Width - 380), (int)(workingArea.Y + 20));
        }
    }
}
