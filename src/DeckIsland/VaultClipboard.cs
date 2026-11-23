using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZXing;
using ZXing.Common;

namespace DeckIsland;

internal static class VaultClipboard
{
    private const string Marker = "DeckIsland.Vault.Copy";
    private static readonly uint MarkerId = RegisterClipboardFormat(Marker);
    private static DispatcherTimer? _expiry;
    private static Guid _pending;

    static VaultClipboard()
    {
        if (Application.Current is { } app)
            app.Exit += (_, _) => { if (_expiry is not null) TryClear(_pending); };
    }

    internal static void Copy(string text, bool expire)
    {
        if (expire && MarkerId == 0) throw new ExternalException();
        var tag = Guid.NewGuid();
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        foreach (string name in new[] { "ExcludeClipboardContentFromMonitorProcessing", "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
            data.SetData(name, new MemoryStream(new byte[4]), false);
        if (expire) data.SetData(Marker, new MemoryStream(tag.ToByteArray()), false);
        Clipboard.SetDataObject(data, copy: true);
        _expiry?.Stop();
        _expiry = null;
        if (!expire) return;

        _pending = tag;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _expiry = timer;
        timer.Tick += (_, _) =>
        {
            if (!TryClear(tag)) { timer.Interval = TimeSpan.FromMilliseconds(200); return; }
            timer.Stop();
            _expiry = null;
        };
        timer.Start();
    }

    private static bool TryClear(Guid tag)
    {
        if (!OpenClipboard(0)) return false;
        try
        {
            nint handle = GetClipboardData(MarkerId);
            if (handle == 0 || GlobalSize(handle) < 16) return true;
            nint data = GlobalLock(handle);
            if (data == 0) return false;
            var bytes = new byte[16];
            try { Marshal.Copy(data, bytes, 0, bytes.Length); }
            finally { GlobalUnlock(handle); }
            return new Guid(bytes) != tag || EmptyClipboard();
        }
        finally { CloseClipboard(); }
    }

    internal static string? ReadQr()
    {
        var image = Clipboard.GetImage();
        if (image is null || (long)image.PixelWidth * image.PixelHeight > 24_000_000) return null;
        var bitmap = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, PossibleFormats = [BarcodeFormat.QR_CODE] }
        };
        return reader.Decode(pixels, bitmap.PixelWidth, bitmap.PixelHeight, RGBLuminanceSource.BitmapFormat.BGRA32)?.Text;
    }

    [DllImport("user32", EntryPoint = "RegisterClipboardFormatW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint RegisterClipboardFormat(string format);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint hwnd);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();
    [DllImport("user32")] private static extern nint GetClipboardData(uint format);
    [DllImport("kernel32")] private static extern nuint GlobalSize(nint handle);
    [DllImport("kernel32")] private static extern nint GlobalLock(nint handle);
    [DllImport("kernel32")] private static extern int GlobalUnlock(nint handle);
}
