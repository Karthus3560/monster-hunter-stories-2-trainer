using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RidersToolkit;

public partial class OverlayWindow : Window
{
    readonly RoutedEventHandler _toggle, _apply, _reset, _stop;
    internal OverlayViewModel Model { get; }
    internal event Action<bool>? DismissRequested;
    internal event Action? FocusLeft;
    bool _allowClose;

    internal OverlayWindow(TrainerViewModel session, RoutedEventHandler toggle,
        RoutedEventHandler apply, RoutedEventHandler reset, RoutedEventHandler stop)
    {
        _toggle=toggle; _apply=apply; _reset=reset; _stop=stop;
        Model=new(session); DataContext=Model;
        InitializeComponent();
        InputBindings.Add(new KeyBinding(new Command(_=>OverlaySearch.Focus()),new KeyGesture(Key.K,ModifierKeys.Control)));
        Closing+=OnClosing; Deactivated+=(_,_)=>FocusLeft?.Invoke();
    }
    void OnCategory(object sender,RoutedEventArgs e)
    {
        if(sender is RadioButton {Tag:string category}) { Model.Category=category; OverlayScroll?.ScrollToTop(); }
    }
    void OnToggle(object sender,RoutedEventArgs e)=>_toggle(sender,e);
    void OnApply(object sender,RoutedEventArgs e)=>_apply(sender,e);
    void OnReset(object sender,RoutedEventArgs e)=>_reset(sender,e);
    void OnStop(object sender,RoutedEventArgs e)=>_stop(sender,e);
    void OnDismiss(object sender,RoutedEventArgs e)=>DismissRequested?.Invoke(true);
    void OnDrag(object sender,MouseButtonEventArgs e)
    {
        if(DismissButton.IsMouseOver) return;
        if(e.ButtonState==MouseButtonState.Pressed) DragMove();
    }
    void OnOverlayKey(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape) {e.Handled=true;DismissRequested?.Invoke(true);}
        // The main scoped F12 loop continues while this popup has focus.
    }
    void OnClosing(object? sender,CancelEventArgs e)
    {
        if(_allowClose) return;
        e.Cancel=true;DismissRequested?.Invoke(true);
    }
    internal void CloseForShutdown() {_allowClose=true;Close();}
    internal void SelectCategory(string category)
    {
        foreach(var tab in MainWindow.VisualChildren<RadioButton>(this))
            if(Equals(tab.Tag,category)){tab.IsChecked=true;return;}
    }
}

internal sealed class OverlayViewModel : Observable
{
    internal static readonly string[] QuickIds=["riderHp","monstieHp","kinship","damage","battleSpeed","kinshipLevel","retreat","radar","gatherRange","walkSpeed","mountSpeed","loot","noConsume"];
    public TrainerViewModel Session {get;}
    public ObservableCollection<Feature> Controls {get;}=[];
    string _category="Quick",_search="";
    public string Category {get=>_category;set{if(Set(ref _category,value)){_search="";Notify(nameof(Search));Refresh();}}}
    public string Search {get=>_search;set{if(Set(ref _search,value))Refresh();}}
    internal OverlayViewModel(TrainerViewModel session){Session=session;Refresh();}
    void Refresh()
    {
        // A typed search spans every remaining control, independent of the selected shortcut tab.
        IEnumerable<Feature> source=Session.Features;
        if(!string.IsNullOrWhiteSpace(Search)) source=source.Where(f=>(f.Title+" "+f.Description).Contains(Search.Trim(),StringComparison.OrdinalIgnoreCase));
        else source=Category=="Quick"?QuickIds.Select(id=>Session.Features.Single(f=>f.Id==id)):source.Where(f=>f.Category==Category);
        Controls.Clear();foreach(var item in source)Controls.Add(item);
    }
}
