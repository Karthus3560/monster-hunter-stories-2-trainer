using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RidersToolkit;

internal readonly record struct WindowPixels(int Left,int Top,int Right,int Bottom)
{
    public int Width=>Right-Left;public int Height=>Bottom-Top;
}
internal sealed record GameWindowSnapshot(nint Handle,int Pid,long Started,uint Style,WindowPixels Bounds,WindowPixels Screen);
internal interface IGameWindowApi
{
    GameWindowSnapshot Capture(nint window);
    void Style(nint window,uint style);
    void Bounds(nint window,WindowPixels bounds);
}
// Window chrome and placement only. No game-memory, config, input or render hooks.
internal sealed class BorderlessGameWindow(IGameWindowApi api)
{
    GameWindowSnapshot? _original,_applied;
    internal bool Active=>_original is not null;
    static bool SameOwner(GameWindowSnapshot a,GameWindowSnapshot b)=>a.Handle==b.Handle&&a.Pid==b.Pid&&a.Started==b.Started;
    internal void Enable(nint window)
    {
        if(Active)throw new InvalidOperationException("Restore the current game window first.");
        var before=api.Capture(window);
        if((before.Style&0x00C00000)!=0x00C00000)throw new InvalidOperationException("Choose Windowed in the game’s display settings first, then press Borderless fullscreen. Exclusive fullscreen cannot show this desktop overlay.");
        if((before.Style&0x61000000)!=0)throw new InvalidOperationException("Restore the game to a normal, visible window first.");
        if(before.Screen.Width<320||before.Screen.Height<240)throw new InvalidOperationException("The game’s monitor dimensions are unavailable.");
        uint style=(before.Style&~0x00CF0000u)|0x80000000u;
        try
        {
            api.Style(window,style);api.Bounds(window,before.Screen);
            var after=api.Capture(window);
            if(!SameOwner(before,after)||after.Style!=style||after.Bounds!=before.Screen)throw new InvalidOperationException("The game did not retain borderless window mode.");
            _original=before;_applied=after;
        }
        catch(Exception original)
        {
            try
            {
                var now=api.Capture(window);
                if(SameOwner(before,now)&&(now.Style==style||now.Style==before.Style)&&(now.Bounds==before.Bounds||now.Bounds==before.Screen))
                {api.Style(window,before.Style);api.Bounds(window,before.Bounds);}
                else throw new InvalidOperationException("The game window changed during setup.");
            }
            catch(Exception rollback){throw new InvalidOperationException("Borderless setup stopped. Restore Windowed mode in the game’s display settings. "+rollback.Message,original);}
            throw new InvalidOperationException("Borderless setup stopped; the previous game window was restored. "+original.Message,original);
        }
    }
    internal void Restore()
    {
        if(_original is not {} before||_applied is not {} applied)return;
        GameWindowSnapshot now;
        try{now=api.Capture(before.Handle);}
        catch(Exception ex)when(ex is InvalidOperationException or ArgumentException or Win32Exception)
        {_original=null;_applied=null;throw new InvalidOperationException("The previous game window is unavailable. Choose Windowed in the current game and press Borderless fullscreen again.",ex);}
        if(!SameOwner(before,now)||now.Style!=applied.Style||now.Bounds!=applied.Bounds)
        {_original=null;_applied=null;throw new InvalidOperationException("The game window or display mode changed. Its new settings were left in place.");}
        api.Style(before.Handle,before.Style);api.Bounds(before.Handle,before.Bounds);
        var restored=api.Capture(before.Handle);
        if(!SameOwner(before,restored)||restored.Style!=before.Style||restored.Bounds!=before.Bounds)throw new InvalidOperationException("Check the game’s display settings; its previous window could not be verified.");
        _original=null;_applied=null;
    }
}
internal sealed class NativeGameWindowApi : IGameWindowApi
{
    [StructLayout(LayoutKind.Sequential)]struct PixelRect {public int Left,Top,Right,Bottom;public readonly WindowPixels Pixels=>new(Left,Top,Right,Bottom);}
    [StructLayout(LayoutKind.Sequential)]struct MonitorInfo {public int Size;public PixelRect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]static extern nint GetWindowLongPtrW(nint window,int index);
    [DllImport("user32.dll",SetLastError=true)]static extern nint SetWindowLongPtrW(nint window,int index,nint value);
    [DllImport("user32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool GetWindowRect(nint window,out PixelRect rect);
    [DllImport("user32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool GetMonitorInfoW(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
    public GameWindowSnapshot Capture(nint window)
    {
        if(!OverlayNative.IsWindow(window))throw new InvalidOperationException("The game window closed. Start Stories 2 and try again.");
        Native.GetWindowThreadProcessId(window,out uint pid);using var process=Process.GetProcessById(checked((int)pid));
        if(!GetWindowRect(window,out var rect))throw new Win32Exception(Marshal.GetLastPInvokeError());
        var monitor=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfoW(OverlayNative.MonitorFromWindow(window,2),ref monitor))throw new Win32Exception(Marshal.GetLastPInvokeError());
        return new(window,(int)pid,process.StartTime.ToUniversalTime().Ticks,unchecked((uint)GetWindowLongPtrW(window,-16)),rect.Pixels,monitor.Monitor.Pixels);
    }
    public void Style(nint window,uint style)
    {
        Marshal.SetLastPInvokeError(0);var previous=SetWindowLongPtrW(window,-16,unchecked((nint)(int)style));
        if(previous==0&&Marshal.GetLastPInvokeError()!=0)throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    public void Bounds(nint window,WindowPixels b)
    {
        // FRAMECHANGED | NOACTIVATE | NOZORDER: retain the game’s focus and z-order.
        if(!SetWindowPos(window,0,b.Left,b.Top,b.Width,b.Height,0x34))throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
}
