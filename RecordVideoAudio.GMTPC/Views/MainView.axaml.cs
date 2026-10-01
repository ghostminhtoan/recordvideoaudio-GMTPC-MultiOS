
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using RecordVideoAudio.GMTPC.Services;
using RecordVideoAudio.GMTPC.ViewModels;

namespace RecordVideoAudio.GMTPC.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void OnRecordKeyKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var (vk, name) = KeyBindingHelper.FromAvaloniaKey(e.Key);
            if (vk != 0 && !string.IsNullOrEmpty(name))
            {
                vm.RecordVkCode = vk;
                vm.RecordKeyName = name;
                e.Handled = true;
            }
        }
    }

    private void OnRecordKeyTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(tb.Text))
        {
            var (vk, name) = KeyBindingHelper.ParseKey(tb.Text);
            if (vk != 0 && !string.IsNullOrEmpty(name))
            {
                vm.RecordVkCode = vk;
                vm.RecordKeyName = name;
            }
        }
    }

    private void OnPauseKeyKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var (vk, name) = KeyBindingHelper.FromAvaloniaKey(e.Key);
            if (vk != 0 && !string.IsNullOrEmpty(name))
            {
                vm.PauseVkCode = vk;
                vm.PauseKeyName = name;
                e.Handled = true;
            }
        }
    }

    private void OnPauseKeyTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(tb.Text))
        {
            var (vk, name) = KeyBindingHelper.ParseKey(tb.Text);
            if (vk != 0 && !string.IsNullOrEmpty(name))
            {
                vm.PauseVkCode = vk;
                vm.PauseKeyName = name;
            }
        }
    }
}