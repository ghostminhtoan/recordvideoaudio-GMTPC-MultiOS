using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RecordVideoAudio.GMTPC.ViewModels;

namespace RecordVideoAudio.GMTPC.Views;

public partial class VocalStudioWindow : Window
{
    public VocalStudioWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnHoldRecordPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.StartLiveAudioHoldCapture();
            }
        }
    }

    private void OnHoldRecordPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.StopLiveAudioHoldCapture();
        }
    }

    private void OnHoldRecordPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.StopLiveAudioHoldCapture();
        }
    }
}
