using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace RecordVideoAudio.GMTPC.Views;

public partial class RegionSelectorWindow : Window
{
    private bool _isDragging;
    private Point _startPoint;
    public Action<int, int, int, int>? RegionSelected { get; set; }

    public RegionSelectorWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(OverlayCanvas);
        if (point.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _startPoint = point.Position;

            Canvas.SetLeft(SelectionBox, _startPoint.X);
            Canvas.SetTop(SelectionBox, _startPoint.Y);
            SelectionBox.Width = 0;
            SelectionBox.Height = 0;
            SelectionBox.IsVisible = true;

            InfoBadge.IsVisible = true;
            UpdateInfoBadge(_startPoint.X, _startPoint.Y, 0, 0);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;

        var currentPoint = e.GetCurrentPoint(OverlayCanvas).Position;

        double x = Math.Min(_startPoint.X, currentPoint.X);
        double y = Math.Min(_startPoint.Y, currentPoint.Y);
        double width = Math.Abs(currentPoint.X - _startPoint.X);
        double height = Math.Abs(currentPoint.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionBox, x);
        Canvas.SetTop(SelectionBox, y);
        SelectionBox.Width = width;
        SelectionBox.Height = height;

        UpdateInfoBadge(x, y, width, height);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;

        var currentPoint = e.GetCurrentPoint(OverlayCanvas).Position;
        double x = Math.Min(_startPoint.X, currentPoint.X);
        double y = Math.Min(_startPoint.Y, currentPoint.Y);
        double width = Math.Abs(currentPoint.X - _startPoint.X);
        double height = Math.Abs(currentPoint.Y - _startPoint.Y);

        if (width >= 20 && height >= 20)
        {
            RegionSelected?.Invoke((int)x, (int)y, (int)width, (int)height);
        }

        Close();
    }

    private void UpdateInfoBadge(double x, double y, double width, double height)
    {
        InfoText.Text = $"📐 {(int)width} × {(int)height}  (X: {(int)x}, Y: {(int)y})";

        double badgeX = x;
        double badgeY = y + height + 10;

        // Keep inside bounds
        if (badgeY + 40 > Bounds.Height)
        {
            badgeY = Math.Max(10, y - 40);
        }

        Canvas.SetLeft(InfoBadge, Math.Max(10, badgeX));
        Canvas.SetTop(InfoBadge, Math.Max(10, badgeY));
    }
}
