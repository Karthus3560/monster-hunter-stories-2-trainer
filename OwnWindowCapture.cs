using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace RidersToolkit;

// Preview-only capture of this app's own window, including its native caption.
internal static class OwnWindowCapture
{
    [StructLayout(LayoutKind.Sequential)]struct Rectangle {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern bool GetWindowRect(nint window,out Rectangle rectangle);
    [DllImport("user32.dll")]static extern nint GetWindowDC(nint window);
    [DllImport("user32.dll")]static extern int ReleaseDC(nint window,nint dc);
    [DllImport("user32.dll")]static extern bool PrintWindow(nint window,nint dc,uint flags);
    [DllImport("gdi32.dll")]static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")]static extern nint CreateCompatibleBitmap(nint dc,int width,int height);
    [DllImport("gdi32.dll")]static extern nint SelectObject(nint dc,nint value);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(nint dc);
    internal static void Save(Window window,string path)
    {
        var handle=new WindowInteropHelper(window).Handle;
        if(!GetWindowRect(handle,out var rectangle))throw new IOException("Cannot measure the preview window.");
        nint source=GetWindowDC(handle),dc=CreateCompatibleDC(source),bitmap=CreateCompatibleBitmap(source,rectangle.Right-rectangle.Left,rectangle.Bottom-rectangle.Top),old=SelectObject(dc,bitmap);
        try
        {
            if(!PrintWindow(handle,dc,2))throw new IOException("Windows could not render the preview frame.");
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(Imaging.CreateBitmapSourceFromHBitmap(bitmap,0,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions())));
            using var stream=File.Create(path);encoder.Save(stream);
        }
        finally{SelectObject(dc,old);DeleteObject(bitmap);DeleteDC(dc);ReleaseDC(handle,source);}
    }
}
