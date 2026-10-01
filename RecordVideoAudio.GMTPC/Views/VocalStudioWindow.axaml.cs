using Avalonia.Controls;
using Avalonia.Interactivity;

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
}
