using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Squiggle.UI.Services;

namespace Squiggle.UI.Windows;

/// <summary>
/// Full-screen overlay showing a frozen shot of the desktop; the user drags out the region
/// they want. Returns the cropped bitmap, or null when cancelled.
/// </summary>
public partial class ScreenRegionPicker : Window
{
    private WriteableBitmap? _screenshot;
    private Point _dragStart;
    private bool _dragging;

    public ScreenRegionPicker()
    {
        InitializeComponent();
    }

    public static ScreenRegionPicker? TryCreate()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        WriteableBitmap shot;
        try
        {
            shot = ScreenCapture.CaptureVirtualScreen();
        }
        catch
        {
            return null;
        }

        var bounds = ScreenCapture.VirtualScreenBounds;
        var picker = new ScreenRegionPicker
        {
            _screenshot = shot,
            Position = new PixelPoint(bounds.X, bounds.Y),
        };

        picker.screenImage.Source = shot;
        picker.Opened += (_, _) => picker.LayoutForScreen(bounds);
        return picker;
    }

    private void LayoutForScreen(PixelRect bounds)
    {
        // Window size is in device-independent units; the capture is in physical pixels.
        var scale = RenderScaling <= 0 ? 1 : RenderScaling;
        Width = bounds.Width / scale;
        Height = bounds.Height / scale;

        screenImage.Width = Width;
        screenImage.Height = Height;
        dimLayer.Width = Width;
        dimLayer.Height = Height;

        Canvas.SetLeft(hintBox, 20);
        Canvas.SetTop(hintBox, 20);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(null);
            return;
        }

        if (e.Key == Key.Enter && _screenshot != null)
        {
            e.Handled = true;
            Close(_screenshot);
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        _dragStart = e.GetPosition(rootCanvas);
        _dragging = true;
        selectionRect.IsVisible = true;
        hintBox.IsVisible = false;
        UpdateSelection(_dragStart);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
            UpdateSelection(e.GetPosition(rootCanvas));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging || _screenshot == null)
            return;

        _dragging = false;
        var end = e.GetPosition(rootCanvas);

        var scale = RenderScaling <= 0 ? 1 : RenderScaling;
        var x = (int)Math.Round(Math.Min(_dragStart.X, end.X) * scale);
        var y = (int)Math.Round(Math.Min(_dragStart.Y, end.Y) * scale);
        var w = (int)Math.Round(Math.Abs(end.X - _dragStart.X) * scale);
        var h = (int)Math.Round(Math.Abs(end.Y - _dragStart.Y) * scale);

        // A click without a drag reads as "cancel" rather than a 0x0 crop
        if (w < 4 || h < 4)
        {
            Close(null);
            return;
        }

        try
        {
            Close(ScreenCapture.Crop(_screenshot, new PixelRect(x, y, w, h)));
        }
        catch
        {
            Close(null);
        }
    }

    private void UpdateSelection(Point current)
    {
        var x = Math.Min(_dragStart.X, current.X);
        var y = Math.Min(_dragStart.Y, current.Y);

        Canvas.SetLeft(selectionRect, x);
        Canvas.SetTop(selectionRect, y);
        selectionRect.Width = Math.Abs(current.X - _dragStart.X);
        selectionRect.Height = Math.Abs(current.Y - _dragStart.Y);
    }
}
