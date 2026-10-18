using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;

namespace RidersToolkit;

public partial class MainWindow
{
    internal async Task<List<(string Title,bool Passed)>> CheckOverlay()
    {
        var results=new List<(string,bool)>();
        void Check(string title,bool passed)=>results.Add((title,passed));
        var popup=PreviewOverlay();popup.UpdateLayout();await Task.Delay(80);popup.UpdateLayout();
        Check("Overlay is a compact topmost popup without a taskbar button",popup.IsVisible&&popup.Topmost&&!popup.ShowInTaskbar&&popup.WindowStyle==WindowStyle.None&&popup.Width<=440&&popup.Owner is null);
        Check("Overlay uses an opaque compositor window instead of a layered transparent window",!popup.AllowsTransparency&&OverlayNative.IsOpaque(new WindowInteropHelper(popup).Handle));
        await Task.Delay(450);_overlay.HandleForeground(12345,true);
        Check("Game reclaiming focus after the old grace period does not hide the overlay",popup.IsVisible);
        var pip=VisualChildren<Image>(popup).Single();
        Check("Overlay mascot resource loads at its reserved square size",pip.Source is System.Windows.Media.Imaging.BitmapSource bitmap&&bitmap.PixelWidth>0&&Math.Abs(pip.ActualWidth-40)<1&&Math.Abs(pip.ActualHeight-40)<1);
        Check("Overlay shares the main trainer's exact feature objects",ReferenceEquals(popup.Model.Session,_vm)&&popup.Model.Controls.All(f=>ReferenceEquals(f,_vm.Features.Single(x=>x.Id==f.Id))));
        Check("Quick tab contains the common battle and exploration switches",popup.Model.Controls.Count==13&&popup.Model.Controls.Any(f=>f.Id=="retreat")&&popup.Model.Controls.Any(f=>f.Id=="kinship"));
        Check("Stealth is absent from the main catalog and overlay shortcuts",_vm.Features.All(f=>f.Id!="stealth")&&!OverlayViewModel.QuickIds.Contains("stealth"));
        string oldNotice=_vm.Notice;OnBorderlessGame(BorderlessButton,new RoutedEventArgs());
        Check("Display helper cannot change a game window during interface previews",!_gameDisplay.Active&&_vm.Notice.Contains("disabled in interface previews"));Notice(oldNotice);
        string originalSearch=_vm.Search;_vm.Search="bottle";
        popup.Model.Search="Flying speed";popup.UpdateLayout();
        Check("Overlay search reaches controls outside the current tab",popup.Model.Controls.Count==1&&popup.Model.Controls[0].Id=="flySpeed"&&_vm.Search=="bottle");
        popup.SelectCategory("Movement");popup.UpdateLayout();
        Check("Overlay movement tab clears its search and shows all five speeds",popup.Model.Search==""&&popup.Model.Controls.Count==5&&VisualChildren<Slider>(popup).Count(s=>s.IsVisible)==5);
        var walk=_vm.Features.Single(f=>f.Id=="walkSpeed");string oldWalk=walk.Target;
        var slider=VisualChildren<Slider>(popup).Single(s=>ReferenceEquals(s.DataContext,walk));
        slider.Value=2.25;
        Check("Overlay sliders immediately update the main trainer values",walk.Target=="2.25"&&ReferenceEquals(popup.Model.Controls.Single(f=>f.Id==walk.Id),walk));
        VisualChildren<Button>(popup).Single(b=>Equals(b.Content,"Reset")&&ReferenceEquals(b.DataContext,walk)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check("Overlay Reset uses the same safe restoration path",walk.Multiplier==1&&!walk.Active);
        walk.Target=oldWalk;
        popup.SelectCategory("Inventory");popup.UpdateLayout();
        var loot=_vm.Features.Single(f=>f.Id=="loot");string oldLoot=loot.Target;
        var choice=VisualChildren<ComboBox>(popup).Single(c=>c.IsVisible&&ReferenceEquals(c.DataContext,loot));choice.SelectedItem="10";
        Check("Overlay loot selector and main trainer stay synchronized",loot.Target=="10"&&loot.LootOptions.SequenceEqual(new[]{"2","5","10"}));loot.Target=oldLoot;
        popup.SelectCategory("Battle");popup.UpdateLayout();
        var rider=_vm.Features.Single(f=>f.Id=="riderHp");
        var toggle=VisualChildren<ToggleButton>(popup).Single(t=>ReferenceEquals(t.DataContext,rider));
        Check("Disconnected overlay switches are disabled",!toggle.IsEnabled&&!rider.Active);
        toggle.IsChecked=true;toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check("Overlay cannot bypass connection or backup checks",!rider.Active&&toggle.IsChecked==false&&_engine.Memory is null&&_vm.Notice.Contains("automatic connection"));
        VisualChildren<Button>(popup).Single(b=>Equals(b.Content,"Stop all   F12")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check("Overlay Stop all reaches the existing stop routine",_vm.Features.All(f=>!f.Active)&&_vm.Notice.StartsWith("All controls stopped"));
        popup.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(popup),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        Check("Esc hides the popup without closing the trainer",!popup.IsVisible&&!_closing);
        var originalState=WindowState;WindowState=WindowState.Minimized;
        _overlay.Toggle();popup.UpdateLayout();
        Check("Overlay opens while the main trainer is minimized",WindowState==WindowState.Minimized&&popup.IsVisible&&popup.WindowState==WindowState.Normal);
        _overlay.Toggle();Check("Repeated overlay toggle hides the same popup",!popup.IsVisible&&ReferenceEquals(popup,_overlay.Window));
        WindowState=originalState;_vm.Search=originalSearch;
        foreach(var work in new[]{new Rect(0,0,1920,1040),new Rect(-1280,0,1280,680),new Rect(0,0,800,560)})
            foreach(double dpi in new[]{1.0,1.25,1.5,2.0})Check($"Overlay stays in the work area at {work.Width}px / {dpi:0.##} DPI scale",work.Contains(OverlayNative.Bounds(work,dpi)));
        // Exercise native window APIs only against a disposable window owned
        // by this test process. The real game is never selected or resized.
        var stub=new Window{Title="Kinship display fixture",Width=640,Height=420,Left=80,Top=80,ShowInTaskbar=false,WindowStyle=WindowStyle.SingleBorderWindow,ResizeMode=ResizeMode.CanResize};
        stub.Show();stub.UpdateLayout();var api=new NativeGameWindowApi();var handle=new WindowInteropHelper(stub).Handle;
        var original=api.Capture(handle);var display=new BorderlessGameWindow(api);
        try
        {
            display.Enable(handle);await Task.Delay(80);var full=api.Capture(handle);
            stub.Topmost=true;
            _overlay.Toggle();popup.UpdateLayout();OverlayNative.Raise(new WindowInteropHelper(popup).Handle);
            Check("Native borderless test window fills its monitor while the topmost overlay opens",full.Bounds==original.Screen&&popup.IsVisible&&popup.Topmost);
            Check("Overlay is actually above a topmost borderless fixture in native window order",OverlayNative.IsAbove(new WindowInteropHelper(popup).Handle,handle));
            OverlayNative.Raise(handle);_overlay.Tick();
            Check("Overlay regains its place after a borderless window reasserts topmost",OverlayNative.IsAbove(new WindowInteropHelper(popup).Handle,handle)&&popup.IsVisible);
            stub.Topmost=false;
            _overlay.Dismiss(false);display.Restore();var restored=api.Capture(handle);
            Check("Native borderless restoration recovers the fixture's exact window and style",restored.Bounds==original.Bounds&&restored.Style==original.Style&&!display.Active);
        }
        finally{stub.Close();Activate();}
        Check("Overlay shortcut scope excludes unrelated apps and missing game PIDs",OverlayShortcut.InScope((uint)Environment.ProcessId,0)&&OverlayShortcut.InScope(12345,12345)&&!OverlayShortcut.InScope(54321,12345)&&!OverlayShortcut.InScope(0,0));
        Check("Game focus keeps the overlay pinned; only unrelated apps dismiss it",!OverlayShortcut.ShouldDismiss(12345,true)&&OverlayShortcut.ShouldDismiss(12345,false)&&!OverlayShortcut.ShouldDismiss((uint)Environment.ProcessId,false));
        Check("Overlay identifies an exact executable independently of a trainer connection",OverlayNative.IsInstalledGame((uint)Environment.ProcessId,Environment.ProcessPath)&&!OverlayNative.IsInstalledGame((uint)Environment.ProcessId,"C:\\not-the-game\\game.exe")&&!OverlayNative.IsInstalledGame(0,Environment.ProcessPath));
        int fired=0;bool permitted=true;string problem="";
        var source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        using(var shortcut=new OverlayShortcut(source,()=>permitted,()=>{fired++;_overlay.Toggle();},text=>problem=text))
        {
            shortcut.UpdateScope(true);
            Check("Windows accepts Alt+W or reports an explicit shortcut conflict",shortcut.Registered||problem.Contains("in use"));
            if(shortcut.Registered)
            {
                OverlayNative.PostMessage(source.Handle,OverlayShortcut.Message,OverlayShortcut.Id,0);await Task.Delay(70);
                Check("Actual WM_HOTKEY message opens the overlay window",fired==1&&popup.IsVisible);
                permitted=false;OverlayNative.PostMessage(source.Handle,OverlayShortcut.Message,OverlayShortcut.Id,0);await Task.Delay(70);
                Check("Hotkey messages outside the allowed focus scope are ignored",fired==1);
                shortcut.UpdateScope(false);Check("Alt+W is released when leaving the game and trainer",!shortcut.Registered);
                bool released=OverlayNative.RegisterHotKey(source.Handle,OverlayShortcut.Id+1,OverlayShortcut.Modifiers,OverlayShortcut.Key);
                Check("Released Alt+W can be registered again",released);
                if(released)OverlayNative.UnregisterHotKey(source.Handle,OverlayShortcut.Id+1);
                _overlay.Dismiss(false);
            }
        }
        _overlay.Toggle();_overlay.HandleForeground(12345,false);
        Check("Switching to an unrelated app dismisses the pinned overlay",!popup.IsVisible);
        Check("Overlay checks leave the game disconnected and all switches off",_engine.Memory is null&&_vm.Features.All(f=>!f.Active));
        return results;
    }
}
