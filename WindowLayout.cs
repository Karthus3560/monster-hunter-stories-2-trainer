using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace RidersToolkit;

// WPF uses device-independent units, including the native Windows caption.
internal static class WindowLayout
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    static int ColorRef(string key)
    {
        var color=((SolidColorBrush)Application.Current.FindResource(key)).Color;
        return color.R|(color.G<<8)|(color.B<<16);
    }

    internal static bool ApplyCaption(Window window)
    {
        int enabled = 1;
        // Unsupported Windows versions simply keep their standard caption appearance.
        var handle=new WindowInteropHelper(window).Handle;if(handle==0)return false;
        DwmSetWindowAttribute(handle,20,ref enabled,sizeof(int));
        int caption=ColorRef("Caption"),text=ColorRef("CaptionInk");
        int captionResult=DwmSetWindowAttribute(handle,35,ref caption,sizeof(int));
        int textResult=DwmSetWindowAttribute(handle,36,ref text,sizeof(int));
        DwmSetWindowAttribute(handle,34,ref caption,sizeof(int));
        return captionResult==0&&textResult==0;
    }

    internal static void Reveal(Window window)
    {
        // Activate alone does not restore a hidden or minimized modeless editor.
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Topmost = window.Owner?.Topmost ?? false;
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(desktop, new Rect(window.Left, window.Top, window.Width, window.Height));
        if (visible.IsEmpty || visible.Width < 120 || visible.Height < 80)
        {
            window.WindowState = WindowState.Normal;
            var fit = Fit(SystemParameters.WorkArea, window.Width, window.Height);
            window.Left = fit.Left; window.Top = fit.Top; window.Width = fit.Width; window.Height = fit.Height;
        }
        window.Activate();
    }

    internal static Rect Fit(Rect workArea, double width = 1020, double height = 730)
    {
        var w = Math.Min(width, Math.Max(1, workArea.Width - Math.Min(32, workArea.Width / 10)));
        var h = Math.Min(height, Math.Max(1, workArea.Height - Math.Min(32, workArea.Height / 10)));
        return new Rect(workArea.Left + (workArea.Width - w) / 2,
            workArea.Top + (workArea.Height - h) / 2, w, h);
    }
}
