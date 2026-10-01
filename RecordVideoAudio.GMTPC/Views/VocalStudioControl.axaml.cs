using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RecordVideoAudio.GMTPC.ViewModels;

namespace RecordVideoAudio.GMTPC.Views;

public partial class VocalStudioControl : UserControl
{
    public VocalStudioControl()
    {
        InitializeComponent();

        var holdBorder = this.FindControl<Border>("HoldMeasureBorder");
        if (holdBorder != null)
        {
            holdBorder.AddHandler(InputElement.PointerPressedEvent, (s, e) =>
            {
                if (e.GetCurrentPoint(holdBorder).Properties.IsLeftButtonPressed)
                {
                    if (DataContext is MainViewModel vm)
                    {
                        vm.StartLiveAudioHoldCapture();
                    }
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

            holdBorder.AddHandler(InputElement.PointerReleasedEvent, (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.StopLiveAudioHoldCapture();
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

            holdBorder.AddHandler(InputElement.PointerCaptureLostEvent, (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.StopLiveAudioHoldCapture();
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        }
    }
}
