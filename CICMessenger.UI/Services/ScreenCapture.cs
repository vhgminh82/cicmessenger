using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CICMessenger.UI.Services;

/// <summary>
/// Grabs the desktop straight from GDI into an Avalonia bitmap. Done with P/Invoke rather
/// than System.Drawing/WinForms so the app doesn't pull in that whole stack just to take
/// a screenshot.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ScreenCapture
{
    const int SM_XVIRTUALSCREEN = 76;
    const int SM_YVIRTUALSCREEN = 77;
    const int SM_CXVIRTUALSCREEN = 78;
    const int SM_CYVIRTUALSCREEN = 79;

    const int SRCCOPY = 0x00CC0020;
    const int CAPTUREBLT = 0x40000000;
    const uint BI_RGB = 0;
    const uint DIB_RGB_COLORS = 0;

    [DllImport("user32.dll")] static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr ho);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy,
                                                       IntPtr hdcSrc, int x1, int y1, int rop);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage,
                                          out IntPtr ppvBits, IntPtr hSection, uint offset);

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    public static PixelRect VirtualScreenBounds => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SM_CYVIRTUALSCREEN));

    /// <summary>
    /// Captures the whole virtual desktop (all monitors) as a bitmap.
    /// </summary>
    public static WriteableBitmap CaptureVirtualScreen()
    {
        var bounds = VirtualScreenBounds;
        int width = Math.Max(1, bounds.Width);
        int height = Math.Max(1, bounds.Height);

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr dib = IntPtr.Zero;
        IntPtr oldObj = IntPtr.Zero;

        try
        {
            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    // negative height gives a top-down bitmap, matching Avalonia's row order
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB
                }
            };

            dib = CreateDIBSection(screenDc, ref bmi, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                throw new InvalidOperationException("CreateDIBSection failed.");

            oldObj = SelectObject(memDc, dib);
            if (!BitBlt(memDc, 0, 0, width, height, screenDc, bounds.X, bounds.Y, SRCCOPY | CAPTUREBLT))
                throw new InvalidOperationException("BitBlt failed.");

            var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
                                             PixelFormat.Bgra8888, AlphaFormat.Opaque);

            using (var buffer = bitmap.Lock())
            {
                int srcStride = width * 4;
                for (int y = 0; y < height; y++)
                {
                    unsafe
                    {
                        Buffer.MemoryCopy(
                            (void*)(bits + y * srcStride),
                            (void*)(buffer.Address + y * buffer.RowBytes),
                            buffer.RowBytes,
                            srcStride);
                    }
                }
            }

            return bitmap;
        }
        finally
        {
            if (oldObj != IntPtr.Zero) SelectObject(memDc, oldObj);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// Copies a rectangular region out of a captured bitmap.
    /// </summary>
    public static WriteableBitmap Crop(WriteableBitmap source, PixelRect region)
    {
        var bounds = new PixelRect(0, 0, source.PixelSize.Width, source.PixelSize.Height);
        region = region.Intersect(bounds);
        if (region.Width <= 0 || region.Height <= 0)
            throw new ArgumentException("Empty crop region.", nameof(region));

        var target = new WriteableBitmap(new PixelSize(region.Width, region.Height),
                                         new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        using var src = source.Lock();
        using var dst = target.Lock();

        unsafe
        {
            for (int y = 0; y < region.Height; y++)
            {
                var srcRow = (byte*)src.Address + (region.Y + y) * src.RowBytes + region.X * 4;
                var dstRow = (byte*)dst.Address + y * dst.RowBytes;
                Buffer.MemoryCopy(srcRow, dstRow, dst.RowBytes, region.Width * 4);
            }
        }

        return target;
    }
}
