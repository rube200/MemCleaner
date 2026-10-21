using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MemCleaner;

internal static unsafe class IconRenderer
{
    private const uint Green = 0xFF2E7D32;
    private const uint Amber = 0xFFC77700;
    private const uint Red = 0xFFC62828;
    private const uint TextColor = 0x00FFFFFF;
    private const uint OpaqueAlpha = 0xFF000000;
    private const int AlphaShift = 24;

    private const int AmberFromPercent = 60;
    private const int RedFromPercent = 80;

    private const int FallbackSize = 16;
    private const ushort BitsPerPixel = 32;
    private const string FontFace = "Segoe UI";
    private const double TwoDigitFontScale = 0.78;
    private const double ThreeDigitFontScale = 0.56;
    private const int ThreeDigits = 3;
    private const int CornerRadiusDivisor = 5;
    private const int MinCornerRadius = 2;
    private const int MaskRowAlignmentBits = 16;
    private const int MaskRowAlignmentBytes = 2;

    private const DRAW_TEXT_FORMAT TextFormat =
        DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_SINGLELINE |
        DRAW_TEXT_FORMAT.DT_NOCLIP;

    private static HFONT _font;
    private static int _fontHeight;

    public static HICON Create(int percent, HWND window)
    {
        var size = IconSize(window);
        var dc = PInvoke.CreateCompatibleDC();
        var color = CreateColorBitmap(dc, size, out var pixels);
        var mask = CreateOpaqueMask(size);
        try
        {
            if (dc.IsNull || color.IsNull || mask.IsNull)
                return default;

            var oldBitmap = PInvoke.SelectObject(dc, color);
            DrawPercent(dc, pixels, size, percent);
            MakeOpaque(pixels);
            RoundCorners(pixels, size, CornerRadius(size));
            PInvoke.SelectObject(dc, oldBitmap);

            var info = new ICONINFO { fIcon = true, hbmMask = mask, hbmColor = color };
            return PInvoke.CreateIconIndirect(&info);
        }
        finally
        {
            _ = PInvoke.DeleteObject(color);
            _ = PInvoke.DeleteObject(mask);
            _ = PInvoke.DeleteDC(dc);
        }
    }

    private static int IconSize(HWND window)
    {
        var size = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)
            ? PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, PInvoke.GetDpiForWindow(window))
            : PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
        return size > 0 ? size : FallbackSize;
    }

    private static HBITMAP CreateColorBitmap(HDC dc, int size, out Span<uint> pixels)
    {
        var bitmapInfo = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = size,
                biHeight = -size,
                biPlanes = 1,
                biBitCount = BitsPerPixel,
                biCompression = (uint)BI_COMPRESSION.BI_RGB
            }
        };

        void* bits;
        var bitmap = PInvoke.CreateDIBSection(dc, &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS, &bits, default, 0);
        pixels = bitmap.IsNull ? default : new Span<uint>(bits, size * size);
        return bitmap;
    }

    private static HBITMAP CreateOpaqueMask(int size)
    {
        Span<byte> maskBits =
            stackalloc byte[(size + MaskRowAlignmentBits - 1) / MaskRowAlignmentBits * MaskRowAlignmentBytes * size];
        maskBits.Clear();
        fixed (byte* maskPointer = maskBits)
        {
            return PInvoke.CreateBitmap(size, size, 1, 1, maskPointer);
        }
    }

    private static void DrawPercent(HDC dc, Span<uint> pixels, int size, int percent)
    {
        var background = BackgroundColor(percent);
        var text = percent.ToString();
        var oldFont = PInvoke.SelectObject(dc, Font(FontHeight(size, text)));
        _ = PInvoke.SetBkMode(dc, BACKGROUND_MODE.TRANSPARENT);
        _ = PInvoke.SetTextColor(dc, (COLORREF)TextColor);

        var area = new RECT { right = size, bottom = size };
        DrawText(dc, pixels, background, text, area);
        if (InkBounds(pixels, size) is { } ink)
        {
            var (shiftX, shiftY) = CenteringShift(ink, size);
            if (shiftX != 0 || shiftY != 0)
                DrawText(dc, pixels, background, text,
                    new RECT { left = shiftX, top = shiftY, right = size + shiftX, bottom = size + shiftY });
        }

        PInvoke.SelectObject(dc, oldFont);
    }

    private static uint BackgroundColor(int percent)
    {
        return percent switch
        {
            < AmberFromPercent => Green,
            < RedFromPercent => Amber,
            _ => Red
        };
    }

    private static int FontHeight(int size, string text)
    {
        return -(int)Math.Round(size * (text.Length >= ThreeDigits ? ThreeDigitFontScale : TwoDigitFontScale));
    }

    private static HFONT Font(int height)
    {
        switch (_font.IsNull)
        {
            case false when height == _fontHeight:
                return _font;
            case false:
                _ = PInvoke.DeleteObject(_font);
                break;
        }

        _font = CreateFont(height);
        _fontHeight = height;
        return _font;
    }

    private static HFONT CreateFont(int height)
    {
        fixed (char* face = FontFace)
        {
            return PInvoke.CreateFont(height, 0, 0, 0, (int)FONT_WEIGHT.FW_BOLD, 0, 0, 0, FONT_CHARSET.DEFAULT_CHARSET,
                FONT_OUTPUT_PRECISION.OUT_DEFAULT_PRECIS, FONT_CLIP_PRECISION.CLIP_DEFAULT_PRECIS,
                FONT_QUALITY.ANTIALIASED_QUALITY, 0, face);
        }
    }

    private static void DrawText(HDC dc, Span<uint> pixels, uint background, string text, RECT area)
    {
        pixels.Fill(background);
        fixed (char* chars = text)
        {
            _ = PInvoke.DrawText(dc, chars, -1, &area, TextFormat);
        }

        _ = PInvoke.GdiFlush();
    }

    private static RECT? InkBounds(ReadOnlySpan<uint> pixels, int size)
    {
        var ink = new RECT { left = size, top = size, right = 0, bottom = 0 };
        for (var y = 0; y < size; y++)
        {
            var row = pixels.Slice(y * size, size);
            for (var x = 0; x < size; x++)
            {
                if (row[x] >> AlphaShift != 0)
                    continue;

                ink.left = Math.Min(ink.left, x);
                ink.right = Math.Max(ink.right, x + 1);
                ink.top = Math.Min(ink.top, y);
                ink.bottom = Math.Max(ink.bottom, y + 1);
            }
        }

        return ink.right > ink.left ? ink : null;
    }

    private static (int X, int Y) CenteringShift(RECT ink, int size)
    {
        return ((size - (ink.right - ink.left)) / 2 - ink.left, (size - (ink.bottom - ink.top)) / 2 - ink.top);
    }

    private static void MakeOpaque(Span<uint> pixels)
    {
        foreach (ref var pixel in pixels)
            pixel |= OpaqueAlpha;
    }

    private static int CornerRadius(int size)
    {
        return Math.Max(MinCornerRadius, size / CornerRadiusDivisor);
    }

    private static void RoundCorners(Span<uint> pixels, int size, int radius)
    {
        var limit = 4 * radius * radius;
        for (var y = 0; y < radius; y++)
        {
            var dy = 2 * (radius - y) - 1;
            var top = y * size;
            var bottom = (size - 1 - y) * size;
            for (var x = 0; x < radius; x++)
            {
                var dx = 2 * (radius - x) - 1;
                if (dx * dx + dy * dy <= limit)
                    break;

                var right = size - 1 - x;
                pixels[top + x] = 0;
                pixels[top + right] = 0;
                pixels[bottom + x] = 0;
                pixels[bottom + right] = 0;
            }
        }
    }
}