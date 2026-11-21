using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace DeckIsland.Native;

// The picture Windows shows for an app in its Start menu and notifications, found from the app's user model id: the one the toast
// names. It works for programs without a package (Cursor, Chrome), whose toast gives no logo of its own.
internal static class ShellIcons
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Size { public int Width, Height; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(Size size, int flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);

    // A PNG of the app's picture, or null when Windows has none for it.
    public static byte[]? ForApp(string appUserModelId, int size)
    {
        if (appUserModelId.Length == 0) return null;
        IntPtr handle = IntPtr.Zero;
        try
        {
            var factory = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(@"shell:AppsFolder\" + appUserModelId, IntPtr.Zero, ref factory, out var item);
            if (item.GetImage(new Size { Width = size, Height = size }, 0, out handle) != 0 || handle == IntPtr.Zero) return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception error) when (error is COMException or ArgumentException or IOException or InvalidOperationException or NotSupportedException) { return null; }
        finally { if (handle != IntPtr.Zero) DeleteObject(handle); }
    }
}
