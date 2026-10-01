using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RecordVideoAudio.GMTPC.Services;
using RecordVideoAudio.GMTPC.ViewModels;

namespace RecordVideoAudio.GMTPC.Views;

public partial class VocalStudioControl : UserControl
{
    public VocalStudioControl()
    {
        InitializeComponent();

        var holdSpeakerBorder = this.FindControl<Border>("HoldSpeakerMeasureBorder") ?? this.FindControl<Border>("HoldMeasureBorder");
        if (holdSpeakerBorder != null)
        {
            holdSpeakerBorder.AddHandler(InputElement.PointerPressedEvent, (s, e) =>
            {
                if (e.GetCurrentPoint(holdSpeakerBorder).Properties.IsLeftButtonPressed)
                {
                    if (DataContext is MainViewModel vm)
                    {
                        vm.StartLiveAudioHoldCapture(LatencyMeasurementTarget.SpeakerAndMic);
                    }
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

            holdSpeakerBorder.AddHandler(InputElement.PointerReleasedEvent, (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.StopLiveAudioHoldCapture();
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

            holdSpeakerBorder.AddHandler(InputElement.PointerCaptureLostEvent, (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.StopLiveAudioHoldCapture();
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        }

        var holdWirelessBorder = this.FindControl<Border>("HoldWirelessMeasureBorder");
        if (holdWirelessBorder != null)
        {
            holdWirelessBorder.AddHandler(InputElement.PointerPressedEvent, (s, e) =>
            {
                if (e.GetCurrentPoint(holdWirelessBorder).Properties.IsLeftButtonPressed)
                {
                    if (DataContext is MainViewModel vm)
                    {
                        vm.StartLiveAudioHoldCapture(LatencyMeasurementTarget.WirelessHeadphone);
                    }
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

            holdWirelessBorder.AddHandler(InputElement.PointerReleasedEvent, (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.StopLiveAudioHoldCapture();
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

            holdWirelessBorder.AddHandler(InputElement.PointerCaptureLostEvent, (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.StopLiveAudioHoldCapture();
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        }
    }
}
