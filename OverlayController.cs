using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RidersToolkit;

internal sealed class OverlayController : IDisposable
{
    readonly MainWindow _main;
    readonly Func<int> _gamePid;
    readonly bool _enabled;
    readonly Action<string> _notice;
    readonly TrainerViewModel _session;
    readonly RoutedEventHandler _toggle,_apply,_reset,_stop;
    OverlayShortcut? _shortcut;
    ForegroundWatcher? _foregroundWatcher;
    OverlayWindow? _window;
    nint _returnWindow,_lastMonitor;
    bool _disposed,_placed;
    int _visibilityVersion;

    internal OverlayController(MainWindow main,TrainerViewModel session,Func<int> gamePid,bool enabled,
        RoutedEventHandler toggle,RoutedEventHandler apply,RoutedEventHandler reset,RoutedEventHandler stop,Action<string> notice)
    {
        _main=main;_session=session;_gamePid=gamePid;_enabled=enabled;
        _toggle=toggle;_apply=apply;_reset=reset;_stop=stop;_notice=notice;
        main.SourceInitialized+=OnMainSource;
    }
    void OnMainSource(object? sender,EventArgs e)
    {
        if(!_enabled||_disposed)return;
        var source=HwndSource.FromHwnd(new WindowInteropHelper(_main).Handle);
        _shortcut=new(source,Permitted,Toggle,_notice);
        _foregroundWatcher=new(()=>Tick());Tick();
    }
    bool Permitted()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out uint pid);
        return OverlayShortcut.InScope(pid,_gamePid())||IsGame(pid);
    }
    bool IsGame(uint pid)=>_enabled&&OverlayNative.IsInstalledGame(pid,Storage.InstalledGame?.Executable);
    async void OnFocusLeft()
    {
        int version=_visibilityVersion;
        await Task.Delay(120);
        if(_disposed||!_enabled||_window?.IsVisible!=true||version!=_visibilityVersion)return;
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out uint pid);
        HandleForeground(pid,IsGame(pid)||OverlayShortcut.InScope(pid,_gamePid()));
    }
    internal void HandleForeground(uint pid,bool game)
    {
        // The panel is pinned over the game until Alt+W, Esc or its close button.
        // Games can reclaim keyboard focus long after the initial activation.
        if(_window?.IsVisible==true&&OverlayShortcut.ShouldDismiss(pid,game))Dismiss(false);
    }
    internal void Tick()
    {
        if(_disposed)return;
        var foreground=Native.GetForegroundWindow();Native.GetWindowThreadProcessId(foreground,out uint pid);
        bool game=IsGame(pid)||(_gamePid()>0&&pid==(uint)_gamePid());
        bool permitted=OverlayShortcut.InScope(pid,_gamePid())||game;_shortcut?.UpdateScope(permitted);
        if(_enabled)HandleForeground(pid,game);
        var covered=game?foreground:_returnWindow;
        if(permitted&&_window?.IsVisible==true&&OverlayNative.IsWindow(covered))
        {
            var handle=new WindowInteropHelper(_window).Handle;
            // Some borderless games reassert topmost after the first activation.
            if(!OverlayNative.IsAbove(handle,covered))OverlayNative.Raise(handle);
        }
    }
    internal OverlayWindow Window
    {
        get
        {
            if(_window is not null)return _window;
            _window=new(_session,_toggle,_apply,_reset,_stop);
            _window.DismissRequested+=Dismiss;
            _window.FocusLeft+=OnFocusLeft;
            return _window;
        }
    }
    internal void Toggle()
    {
        if(_disposed)return;
        if(_window?.IsVisible==true){Dismiss(true);return;}
        var foreground=Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(foreground,out uint pid);
        _returnWindow=OverlayShortcut.InScope(pid,_gamePid())||IsGame(pid)?foreground:new WindowInteropHelper(_main).Handle;
        // No Owner: an owned popup would disappear whenever the main trainer is minimized.
        var popup=Window;
        var handle=new WindowInteropHelper(popup).EnsureHandle();
        var monitor=OverlayNative.MonitorFromWindow(_returnWindow,2);
        if(!_placed||monitor!=_lastMonitor||!OverlayNative.IsInsideWorkArea(handle,monitor))
        {
            OverlayNative.Place(popup,_returnWindow);_lastMonitor=monitor;_placed=true;
        }
        ++_visibilityVersion;
        if(popup.WindowState==WindowState.Minimized)popup.WindowState=WindowState.Normal;
        popup.Show();OverlayNative.Raise(handle);popup.Activate();
        OverlayNative.SetForegroundWindow(handle);
        OverlayNative.Raise(handle);
    }
    internal void Dismiss(bool restoreFocus)
    {
        ++_visibilityVersion;_window?.Hide();
        if(restoreFocus&&OverlayNative.IsWindow(_returnWindow))OverlayNative.SetForegroundWindow(_returnWindow);
    }
    public void Dispose()
    {
        if(_disposed)return;
        _disposed=true;_main.SourceInitialized-=OnMainSource;
        _shortcut?.Dispose();_shortcut=null;
        _foregroundWatcher?.Dispose();_foregroundWatcher=null;
        _window?.CloseForShutdown();_window=null;
    }
}

internal sealed class OverlayShortcut : IDisposable
{
    internal const int Id=0x5726,Message=0x0312;
    internal const uint Modifiers=0x4001,Key=0x57; // Alt + W, no key repeat.
    readonly HwndSource _source;
    readonly Func<bool> _permitted;
    readonly Action _toggle;
    readonly Action<string> _notice;
    bool _disposed,_reported;
    DateTime _nextTry;
    internal bool Registered {get;private set;}
    internal OverlayShortcut(HwndSource source,Func<bool> permitted,Action toggle,Action<string> notice)
    {_source=source;_permitted=permitted;_toggle=toggle;_notice=notice;source.AddHook(Hook);}
    internal static bool InScope(uint pid,int gamePid)=>pid>0&&(pid==(uint)Environment.ProcessId||(gamePid>0&&pid==(uint)gamePid));
    internal static bool ShouldDismiss(uint pid,bool game)=>pid>0&&pid!=(uint)Environment.ProcessId&&!game;
    internal void UpdateScope(bool permitted)
    {
        if(_disposed)return;
        if(!permitted){Release();return;}
        if(Registered||DateTime.UtcNow<_nextTry)return;
        Registered=OverlayNative.RegisterHotKey(_source.Handle,Id,Modifiers,Key);
        if(Registered){_reported=false;return;}
        _nextTry=DateTime.UtcNow.AddSeconds(2);
        if(!_reported){_reported=true;_notice("Alt+W is in use by another app. Use Quick overlay in the trainer, or free that shortcut.");}
    }
    nint Hook(nint hwnd,int message,nint wParam,nint lParam,ref bool handled)
    {
        if(message==Message&&wParam==Id&&Registered&&!_disposed)
        {handled=true;if(_permitted())_toggle();}
        return 0;
    }
    void Release(){if(!Registered)return;OverlayNative.UnregisterHotKey(_source.Handle,Id);Registered=false;}
    public void Dispose(){if(_disposed)return;Release();_source.RemoveHook(Hook);_disposed=true;}
}

// Foreground notifications run in this process; no game or keyboard injection.
internal sealed class ForegroundWatcher : IDisposable
{
    delegate void Callback(nint hook,uint eventId,nint window,int objectId,int childId,uint thread,uint time);
    [DllImport("user32.dll")]static extern nint SetWinEventHook(uint min,uint max,nint module,Callback callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]static extern bool UnhookWinEvent(nint hook);
    readonly Callback _callback;
    nint _hook;
    internal ForegroundWatcher(Action changed)
    {
        _callback=(_,_,_,_,_,_,_)=>changed();
        _hook=SetWinEventHook(3,3,0,_callback,0,0,0);
        // Tick remains a fallback if Windows cannot install the notification.
    }
    public void Dispose(){if(_hook==0)return;UnhookWinEvent(_hook);_hook=0;GC.KeepAlive(_callback);}
}

internal static class OverlayNative
{
    [StructLayout(LayoutKind.Sequential)]internal struct PixelRect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]internal struct MonitorInfo {public int Size;public PixelRect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool RegisterHotKey(nint window,int id,uint modifiers,uint key);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool UnregisterHotKey(nint window,int id);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")]internal static extern nint MonitorFromWindow(nint window,uint flags);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]static extern bool GetWindowRect(nint window,out PixelRect rect);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool PostMessage(nint window,int message,nint wParam,nint lParam);
    [DllImport("kernel32.dll")]static extern nint OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)][return:MarshalAs(UnmanagedType.Bool)]static extern bool QueryFullProcessImageName(nint process,uint flags,StringBuilder name,ref uint length);
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]static extern bool CloseHandle(nint handle);
    [DllImport("user32.dll")]static extern nint GetWindow(nint window,uint command);
    [DllImport("user32.dll")]static extern nint GetWindowLongPtrW(nint window,int index);
    internal static bool IsOpaque(nint window)=>(GetWindowLongPtrW(window,-20).ToInt64()&0x80000)==0;

    internal static bool IsInstalledGame(uint pid,string? installed)
    {
        if(pid==0||string.IsNullOrWhiteSpace(installed))return false;
        var process=OpenProcess(0x1000,false,pid);if(process==0)return false;
        try{uint size=(uint)Math.Max(1024,installed.Length+1);var path=new StringBuilder((int)size);return QueryFullProcessImageName(process,0,path,ref size)&&string.Equals(path.ToString(),installed,StringComparison.OrdinalIgnoreCase);}
        finally{CloseHandle(process);}
    }
    internal static bool IsAbove(nint popup,nint other)
    {
        for(int i=0;i<1024&&(other=GetWindow(other,3))!=0;i++)if(other==popup)return true;
        return false;
    }

    static MonitorInfo Info(nint monitor)
    {
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(monitor,ref info))throw new InvalidOperationException("The overlay could not read the current screen dimensions.");
        return info;
    }
    internal static Rect Bounds(Rect work,double dpi)
    {
        var width=Math.Min(440*dpi,Math.Max(1,work.Width-32));
        var height=Math.Min(650*dpi,Math.Max(1,work.Height-32));
        return new(work.Right-width-Math.Min(16,work.Width/4),work.Top+Math.Min(16,work.Height/4),width,height);
    }
    internal static void Place(Window popup,nint anchor)
    {
        var hwnd=new WindowInteropHelper(popup).EnsureHandle();
        var work=Info(MonitorFromWindow(anchor,2)).Work;
        // Move to the game's monitor first, then measure this WPF window's per-monitor DPI.
        SetWindowPos(hwnd,-1,work.Left+16,work.Top+16,0,0,0x0011);
        var dpi=Math.Max(96,GetDpiForWindow(hwnd))/96.0;
        var bounds=Bounds(new(work.Left,work.Top,work.Right-work.Left,work.Bottom-work.Top),dpi);
        popup.Width=bounds.Width/dpi;popup.Height=bounds.Height/dpi;
        SetWindowPos(hwnd,-1,(int)bounds.Left,(int)bounds.Top,(int)bounds.Width,(int)bounds.Height,0x0010);
    }
    internal static void Raise(nint window)=>SetWindowPos(window,-1,0,0,0,0,0x0013);
    internal static bool IsInsideWorkArea(nint hwnd,nint monitor)
    {
        var work=Info(monitor).Work;
        return GetWindowRect(hwnd,out var rect)&&rect.Left>=work.Left&&rect.Right<=work.Right&&rect.Top>=work.Top&&rect.Bottom<=work.Bottom;
    }
}
