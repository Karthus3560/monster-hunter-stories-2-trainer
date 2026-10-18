using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;

namespace RidersToolkit;

public partial class MainWindow : Window
{
    readonly TrainerViewModel _vm=new();
    readonly AutomaticTrainerEngine _engine;
    readonly OverlayController _overlay;
    readonly BorderlessGameWindow _gameDisplay=new(new NativeGameWindowApi());
    readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(50)};
    readonly HashSet<int> _down=new();
    readonly bool _autoConnect;
    bool _connecting,_automaticConnectionPaused;
    DateTime _nextConnection;
    string? _lastConnectionError;
    bool _backupThisSession, _closing;
    int _ticks;
    string? _identityHash;
    DateTime? _launchRequested;
    bool _changingGameData;

    public MainWindow(bool autoConnect=true)
    {
        _autoConnect=autoConnect;
        InitializeComponent(); DataContext=_vm;
        UpdateThemeLabel();ThemeManager.BindLocalBrushes(this);
        _engine=new(_vm.Features.ToList());
        _overlay=new(this,_vm,()=>_engine.Pid,_autoConnect,OnToggleFeature,OnApplyFeature,OnResetMultiplier,OnStopAll,Notice);
        _engine.Fault+=(title,detail)=> { Log(title,detail); Notice(detail); UpdateSession(); };
        Storage.Discover();
        _vm.SavePath=Storage.SaveDirectory ?? "Save folder not identified; check that Steam has created a save.";
        if(Storage.GameDirectory is not null)
        {
            try { var info=Storage.InspectGame(); _identityHash=info.Hash; _vm.VersionLabel="Installed version "+info.Version; }
            catch(Exception ex) { Notice(ex.Message); }
        }
        RefreshBackups(); RefreshProfiles(); RefreshLuckStatus(true);
        foreach(var feature in _vm.Features) feature.PropertyChanged+=(_,a)=> { if(a.PropertyName is nameof(Feature.Active) or nameof(Feature.Bound)) UpdateSession(); };
        InputBindings.Add(new KeyBinding(new Command(_=>SearchBox.Focus()),new KeyGesture(Key.K,ModifierKeys.Control)));
        _timer.Tick+=OnTick; _timer.Start();
        Log("Automatic trainer ready","Waiting for Stories 2. No value searches or manual setup.");
        FitToDesktop();
        SizeChanged+=(_,_)=>UpdateSceneLayout();UpdateSceneLayout();
        SourceInitialized += (_,_) => WindowLayout.ApplyCaption(this);
        UpdateSession();
    }
    void UpdateSceneLayout()=>CoastScene.Visibility=Height>=640?Visibility.Visible:Visibility.Collapsed;
    void FitToDesktop()
    {
        var bounds = WindowLayout.Fit(SystemParameters.WorkArea);
        MinWidth = Math.Min(760, bounds.Width);
        MinHeight = Math.Min(480, bounds.Height);
        Width = bounds.Width; Height = bounds.Height;
        Left = bounds.Left; Top = bounds.Top;
    }

    StableEditorWindow? _studio;
    AppUpdateWindow? _updatesWindow;
    void OnAppUpdates(object sender,RoutedEventArgs e)
    {
        if(_updatesWindow is not null){WindowLayout.Reveal(_updatesWindow);return;}
        _updatesWindow=new AppUpdateWindow(this,PrepareAppUpdate,!_autoConnect);
        _updatesWindow.Closed+=(_,_)=>_updatesWindow=null;
        WindowLayout.Reveal(_updatesWindow);
    }
    internal Window PreviewUpdates(){OnAppUpdates(this,new RoutedEventArgs());return _updatesWindow!;}
    void PrepareAppUpdate()
    {
        if(_changingGameData||_vm.Busy||_connecting)throw new InvalidOperationException("Wait for the current operation to finish before restarting to update.");
        if(_studio is not null)throw new InvalidOperationException("Finish your work and close Egg Studio before restarting to update.");
        StopAll();
        if(_engine.RestorationIssue is {} issue)throw new InvalidOperationException("Live restoration needs attention before updating: "+issue);
        _automaticConnectionPaused=true;
    }
    void OnOpenEditor(object sender,RoutedEventArgs e)
    {
        try
        {
            if(_studio is not null){WindowLayout.Reveal(_studio);return;}
            var catalog=new NativeCatalog(Storage.GameDirectory??throw new InvalidOperationException("Refresh Steam detection first."));
            var studio=new StableEditorWindow(this,catalog);
            studio.Closed+=(_,_)=>{if(ReferenceEquals(_studio,studio))_studio=null;};
            _studio=studio;WindowLayout.Reveal(studio);
        }
        catch(Exception ex)
        {
            var failed=_studio;_studio=null;failed?.Close();
            Log("Egg Studio could not open",ex.ToString());
            Notice("Egg Studio could not open: "+ex.Message);
        }
    }
    void OnOpenOverlay(object sender,RoutedEventArgs e)=>_overlay.Toggle();
    void UpdateThemeLabel()=>ThemeButton.Content="Theme: "+ThemeManager.Label;
    void OnThemeChange(object sender,RoutedEventArgs e)
    {
        try
        {
            ThemeManager.Change(ThemeManager.Next(ThemeManager.Current));
            UpdateThemeLabel();Notice("Theme changed. Your choice will be remembered.");
        }
        catch(Exception ex){Notice("Could not save the theme: "+ex.Message);}
    }
    void OnBorderlessGame(object sender,RoutedEventArgs e)
    {
        try
        {
            if(_gameDisplay.Active)
            {
                _gameDisplay.Restore();Notice("Previous game window restored.");
            }
            else
            {
                if(!_autoConnect)throw new InvalidOperationException("Display controls are disabled in interface previews.");
                if(_engine.Pid<=0)throw new InvalidOperationException("Connect to Stories 2 first. Choose Windowed in the game, then press Borderless fullscreen.");
                using var game=System.Diagnostics.Process.GetProcessById(_engine.Pid);
                if(!string.Equals(game.MainModule?.FileName,Storage.InstalledGame?.Executable,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("The connected game identity changed. Reconnect first.");
                _gameDisplay.Enable(game.MainWindowHandle);
                Notice("Borderless fullscreen enabled. Return to the game and press Alt+W. Click Restore game window to undo.");
            }
        }
        catch(Exception ex){Notice(ex.Message);}
        BorderlessButton.Content=_gameDisplay.Active?"Restore game window":"Borderless fullscreen";
    }
    internal Window PreviewAddEgg()
    {
        var catalog=new NativeCatalog(Storage.GameDirectory??throw new InvalidOperationException("Game installation not found."));
        // Screenshot-only empty-carton window: no game connection or writes.
        var preview=new AddEggWindow(this,()=>null,catalog,[],null,_=>{});preview.Show();return preview;
    }
    internal Window PreviewStudio(){OnOpenEditor(this,new RoutedEventArgs());return _studio??throw new InvalidOperationException(_vm.Notice);}
    internal OverlayWindow PreviewOverlay() {_overlay.Toggle();return _overlay.Window;}
    internal static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T:DependencyObject=>Descendants<T>(parent);

    int _mascotTip;
    void OnMascotTap(object sender, RoutedEventArgs e)
    {
        string[] tips = ["Navirou’s tip: F12 stops every automatic control.",
            "Navirou’s tip: Alt+W opens your quick controls over the game. Esc returns to play.",
            "Navirou’s tip: Ctrl+K jumps straight to search.",
            "Navirou’s tip: Reset switches a speed or damage control off and returns it to normal.",
            "Navirou’s tip: Rainbow hunt applies to fresh eggs and newly generated areas."];
        Notice(tips[_mascotTip++ % tips.Length]);
    }

    Window? _sessionWindow;
    void OnSessionDetails(object sender, RoutedEventArgs e)
    {
        if (_sessionWindow is not null) { _sessionWindow.Activate(); return; }
        var panel = new StackPanel { Margin = new Thickness(20) };
        void Label(string text, bool heading = false)
        {
            panel.Children.Add(new TextBlock { Text = text, FontSize = heading ? 17 : 12,
                FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, heading ? 10 : 8),
                Foreground = (Brush)FindResource(heading ? "Mint" : "Muted") });
        }
        Label("Kinship Studio", true); Label("By Ghaith Saad Jawad"); Label("Your session", true); Label(_vm.ConnectionStatus); Label(_vm.VersionLabel);
        Label(_vm.InstallationLabel); Label(_vm.ActiveCount + " active · " + _vm.LocatedCount + " ready");
        var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 18) };
        var folder = new Button { Content = "Open game folder", Margin = new Thickness(0, 0, 8, 8) };
        folder.Click += OnOpenGame; actions.Children.Add(folder);
        var refresh = new Button { Content = "Refresh Steam libraries", Margin = new Thickness(0, 0, 0, 8) };
        refresh.Click += (a,b) => { OnRefreshSteam(a,b); _sessionWindow?.Close(); };
        actions.Children.Add(refresh); panel.Children.Add(actions);
        Label("Recent activity", true);
        foreach (var item in _vm.RecentActivity) { Label(item.Time + "  ·  " + item.Title); Label(item.Detail); }
        var bounds = WindowLayout.Fit(SystemParameters.WorkArea, 460, 580);
        _sessionWindow = new Window { Owner = this, Title = "Kinship Studio · Session", Content = new ScrollViewer {
            Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Width = bounds.Width, Height = bounds.Height, Left = bounds.Left, Top = bounds.Top,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        _sessionWindow.Closed += (_,_) => _sessionWindow = null;
        _sessionWindow.SourceInitialized += (_,_) => WindowLayout.ApplyCaption(_sessionWindow!);
        ThemeManager.BindLocalBrushes(_sessionWindow);_sessionWindow.Show();
    }

    internal void PreviewPage(string page)
    {
        if(new[]{"Battle","Inventory","Exploration","Movement","Progression","Backups","Profiles","Research","Luck"}.Contains(page)) _vm.Navigate(page);
    }
    internal async Task<int> RunUiChecks()
    {
        var results=new List<string>();int failures=0;
        void Check(string title,bool passed) {results.Add((passed?"PASS  ":"FAIL  ")+title);if(!passed)failures++;}
        void Navigate(string page)
        {
            var button=Descendants<Button>(this).First(b=>Equals(b.Tag,page));
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));UpdateLayout();
        }
        var work = SystemParameters.WorkArea;
        Check("Native minimize, maximize and close remain enabled", WindowStyle == WindowStyle.SingleBorderWindow && ResizeMode == ResizeMode.CanResize);
        Check("Initial window fits the desktop work area", Left >= work.Left && Top >= work.Top && Left + Width <= work.Right + 1 && Top + Height <= work.Bottom + 1);
        foreach (var area in new[] { new Rect(0,0,1366,728), new Rect(0,0,800,560), new Rect(-1280,0,1280,680), new Rect(0,0,640,440) })
        {
            var fitted = WindowLayout.Fit(area);
            Check("Caption stays inside work area " + area.Width, area.Contains(fitted));
        }
        double previousWidth = Width, previousHeight = Height;
        var wideCards=Descendants<ContentPresenter>(FeatureDeck).Where(c=>c.Content is Feature).Take(2).ToArray();
        Check("Wide control deck displays two cards side by side",wideCards.Length==2&&Math.Abs(wideCards[0].TranslatePoint(new Point(),FeatureDeck).Y-wideCards[1].TranslatePoint(new Point(),FeatureDeck).Y)<1&&wideCards[1].TranslatePoint(new Point(),FeatureDeck).X>wideCards[0].TranslatePoint(new Point(),FeatureDeck).X);
        Width = MinWidth; Height = MinHeight; UpdateLayout();
        await Task.Delay(60);UpdateLayout();
        var compactCards=Descendants<ContentPresenter>(FeatureDeck).Where(c=>c.Content is Feature).Take(2).ToArray();
        Check("Compact control deck stacks its cards without horizontal overflow",compactCards.Length==2&&Math.Abs(compactCards[0].TranslatePoint(new Point(),FeatureDeck).X-compactCards[1].TranslatePoint(new Point(),FeatureDeck).X)<1&&compactCards[1].TranslatePoint(new Point(),FeatureDeck).Y>compactCards[0].TranslatePoint(new Point(),FeatureDeck).Y&&compactCards.All(c=>c.ActualWidth<=FeatureDeck.ActualWidth));
        var stop = Descendants<Button>(this).Single(b => Equals(b.Content,"Disable all     F12"));
        var stopBounds = stop.TransformToAncestor((Visual)Content).TransformBounds(new Rect(stop.RenderSize));
        Check("Emergency stop remains visible at minimum size", stop.IsVisible && stopBounds.Bottom <= ((FrameworkElement)Content).ActualHeight);
        Check("Compact header keeps launch and connect visible", Descendants<Button>(this).Any(b=>b.IsVisible && Equals(b.Content,_vm.LaunchLabel)) && Descendants<Button>(this).Any(b=>b.IsVisible && Equals(b.Content,_vm.ConnectLabel)));
        bool FitsClient(FrameworkElement element)
        {
            var rectangle = element.TransformToAncestor((Visual)Content).TransformBounds(new Rect(element.RenderSize));
            var client = (FrameworkElement)Content;
            return rectangle.Left >= 0 && rectangle.Top >= 0 && rectangle.Right <= client.ActualWidth + 1 && rectangle.Bottom <= client.ActualHeight + 1;
        }
        Check("Launch and connection buttons stay inside the minimum window", Descendants<Button>(this).Where(b=>Equals(b.Content,_vm.LaunchLabel)||Equals(b.Content,_vm.ConnectLabel)).All(FitsClient));
        Check("Theme selector and author credit remain visible at minimum size",FitsClient(ThemeButton)&&Descendants<TextBlock>(this).Where(t=>t.Text=="By Ghaith Saad Jawad").All(FitsClient));
        Width = previousWidth; Height = previousHeight; UpdateLayout();
        var sessionButton = Descendants<Button>(this).Single(b => Equals(b.Content,"Session details"));
        OnSessionDetails(sessionButton,new RoutedEventArgs());
        Check("Session details opens inside the desktop", _sessionWindow is not null && work.Contains(new Rect(_sessionWindow.Left,_sessionWindow.Top,_sessionWindow.Width,_sessionWindow.Height)));
        var firstSession = _sessionWindow; OnSessionDetails(sessionButton,new RoutedEventArgs());
        Check("Session details reuses its existing window", ReferenceEquals(firstSession,_sessionWindow));
        _sessionWindow?.Close(); Check("Session details closes cleanly",_sessionWindow is null);
        OnMascotTap(MascotCard,new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left));
        Check("Navirou offers a useful shortcut tip",_vm.Notice.Contains("Navirou’s tip") && _vm.Notice.Contains("F12"));
        Navigate("Inventory");Check("Ten automatic inventory controls",_vm.VisibleFeatures.Count==10);
        Navigate("Exploration");Check("Six simple exploration controls",_vm.VisibleFeatures.Count==6&&_vm.VisibleFeatures.All(f=>!f.IsNumeric)&&_vm.VisibleFeatures.Single(f=>f.Id=="retreat").Hotkey=="F9");
        Navigate("Inventory");
        _vm.Search="bottle";Check("Search matches titles and descriptions",_vm.VisibleFeatures.Count==2&&_vm.VisibleFeatures.Any(f=>f.Id=="caps")&&_vm.VisibleFeatures.Any(f=>f.Id=="facility"));
        Navigate("Backups");Check("Navigation clears search and shows backups",_vm.Search==""&&_vm.ShowBackups&&!_vm.ShowFeatures);
        Navigate("Profiles");Check("Profiles page",_vm.ShowProfiles);
        Check("App update control is available without a game connection",AppUpdatesButton.IsVisible&&AppUpdatesButton.IsEnabled);
        OnAppUpdates(AppUpdatesButton,new RoutedEventArgs());
        Check("Update preview opens without network or installation",_updatesWindow is {IsVisible:true}&&Descendants<TextBlock>(_updatesWindow).Any(t=>t.Text=="Updates replace the installed app."));
        var firstUpdate=_updatesWindow;OnAppUpdates(AppUpdatesButton,new RoutedEventArgs());
        Check("Repeated update clicks reuse the same dialog",ReferenceEquals(firstUpdate,_updatesWindow));
        _updatesWindow?.Close();
        Navigate("Research");Check("Editor page is separate from automatic switches",_vm.ShowResearch&&!_vm.ShowFeatures);
        Check("Sidebar opens offline Egg Studio directly without connecting to the game",_studio is {IsVisible:true}&&_engine.Pid==0);
        var firstStudio=_studio;
        OnOpenEditor(this,new RoutedEventArgs());
        Check("Repeated Egg Studio clicks reuse its existing window",ReferenceEquals(firstStudio,_studio));
        if(_studio is not null)
        {
            _studio.WindowState=WindowState.Minimized;
            OnOpenEditor(this,new RoutedEventArgs());
            Check("Clicking Egg Studio restores a minimized editor",ReferenceEquals(firstStudio,_studio)&&_studio.WindowState==WindowState.Normal&&_studio.IsVisible);
            _studio.Hide();OnOpenEditor(this,new RoutedEventArgs());
            Check("Clicking Egg Studio reveals a hidden editor",ReferenceEquals(firstStudio,_studio)&&_studio.IsVisible);
            _studio.Left=SystemParameters.VirtualScreenLeft-2000;_studio.Top=SystemParameters.VirtualScreenTop-2000;
            OnOpenEditor(this,new RoutedEventArgs());
            Check("An off-screen Egg Studio returns inside the desktop",work.Contains(new Rect(_studio.Left,_studio.Top,_studio.Width,_studio.Height)));
            Topmost=true;OnOpenEditor(this,new RoutedEventArgs());
            Check("Egg Studio stays above an always-on-top main window",_studio.Topmost);
            Topmost=false;OnOpenEditor(this,new RoutedEventArgs());
        }
        Check("Egg studio opens with nine gene selectors and nine Rainbow buttons",_studio is not null&&Descendants<ComboBox>(_studio).Count()==23&&Descendants<Button>(_studio).Count(b=>Equals(b.Content,"Rainbow"))==9);
        Check("Disconnected egg studio cannot apply or undo",_studio is not null&&Descendants<Button>(_studio).Where(b=>Equals(b.Content,"Apply preview")||Equals(b.Content,"Undo last edit")).All(b=>!b.IsEnabled));
        Check("Add egg stays disabled until a loaded solo save is refreshed",_studio is not null&&Descendants<Button>(_studio).Single(b=>Equals(b.Content,"Add egg…")).IsEnabled==false);
        Check("Egg Studio offers offline save-slot selection and an Open save action",_studio is not null&&Descendants<Button>(_studio).Any(b=>Equals(b.Content,"Open save"))&&Descendants<TextBlock>(_studio).Any(t=>t.Text.StartsWith("Offline save editor")));
        foreach(var check in CheckAppearance())Check(check.Title,check.Passed);
        Descendants<Button>(_studio!).Single(b=>Equals(b.Content,"Refresh owned")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check("Offline Studio cannot refresh or enable Add before opening a saved slot",_studio is not null&&!Descendants<Button>(_studio).Single(b=>Equals(b.Content,"Add egg…")).IsEnabled&&Descendants<TextBlock>(_studio).Any(t=>t.Text.Contains("choose Open save first")));
        _studio?.Close();Check("Egg studio closes cleanly",_studio is null);
        Navigate("Research");
        Check("Sidebar creates a fresh offline editor after closing the previous one",_studio is {IsVisible:true}&&!ReferenceEquals(firstStudio,_studio));
        _studio?.Close();
        Check("Refreshing an empty carton enables Add egg and previews slot one without any writes",EggAdditionTests.EmptyCartonStudioReady(this));
        if(Storage.GameDirectory is not null)
        {
            var catalog=new NativeCatalog(Storage.GameDirectory);var sample=catalog.Species[0];byte[] previewBytes=new byte[StableEditor.EggBytes];BitConverter.GetBytes(sample.Id).CopyTo(previewBytes,0);
            var template=new EditorSubject("Example egg · interface check",true,0,0,8,previewBytes,[],"interface-only");bool added=false;
            var addWindow=new AddEggWindow(this,()=>null,catalog,[template],template,_=>added=true);addWindow.Show();addWindow.UpdateLayout();
            Check("Add egg dialog fits the desktop and starts with Apply disabled",work.Contains(new Rect(addWindow.Left,addWindow.Top,addWindow.Width,addWindow.Height))&&!Descendants<Button>(addWindow).Single(b=>Equals(b.Content,"Add egg")).IsEnabled);
            Descendants<Button>(addWindow).Single(b=>Equals(b.Content,"Preview egg")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check("Disconnected Add egg cannot create a preview or invoke a write",!added&&!Descendants<Button>(addWindow).Single(b=>Equals(b.Content,"Add egg")).IsEnabled&&Descendants<TextBlock>(addWindow).Any(t=>t.Text.StartsWith("Open a saved game")));
            addWindow.Close();
            var emptyWindow=new AddEggWindow(this,()=>null,catalog,[],null,_=>added=true);emptyWindow.Show();emptyWindow.UpdateLayout();
            Check("Empty carton offers New egg and the full species picker without a template",Descendants<ComboBox>(emptyWindow).Any(c=>c.SelectedItem is EggSourceOption {Subject:null}&&c.SelectionBoxItem?.ToString()=="New egg · species starter genes")&&Descendants<ComboBox>(emptyWindow).Any(c=>c.Items.Count==catalog.Species.Length));
            Descendants<Button>(emptyWindow).Single(b=>Equals(b.Content,"Preview egg")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check("Empty-carton preview still requires a live connection",!added&&!Descendants<Button>(emptyWindow).Single(b=>Equals(b.Content,"Add egg")).IsEnabled&&Descendants<TextBlock>(emptyWindow).Any(t=>t.Text.StartsWith("Open a saved game")));
            emptyWindow.Close();
            Check("Installed tables provide valid species starter eggs with native no-bonus state",catalog.Species.All(s=>{var egg=catalog.StarterEgg(s.Id);return egg.Length==StableEditor.EggBytes&&BitConverter.ToInt32(egg)==s.Id&&egg[0x2C]==255&&Enumerable.Range(0,9).All(i=>catalog.Genes.Any(g=>g.Id==BitConverter.ToUInt16(egg,8+i*4)));}));
        }

        Navigate("Exploration");var prayer=_vm.Features.Single(f=>f.Id=="prayerBuff");var prayerChoice=Descendants<ComboBox>(this).Single(c=>c.IsVisible&&ReferenceEquals(c.DataContext,prayer));prayerChoice.SelectedValue="8";
        Check("Prayer selector shows eight native effects and shares its chosen value",prayer.Target=="8"&&prayer.ChoiceOptions.Length==8);Check("Prayer selected text is a readable effect name",prayerChoice.SelectionBoxItem?.ToString()==prayer.ChoiceLabel);prayer.Target="1";
        Navigate("Battle");var defense=_vm.Features.Single(f=>f.Id=="damageTaken");Check("Defense slider includes zero, quarter, half and normal damage",defense.Minimum==0&&defense.Maximum==1&&defense.SliderStep==.25);

        Navigate("Luck");Check("Rainbow egg and guaranteed rare den options",_vm.ShowLuck&&EggMode.Items.Count==5&&DenMode.Items.Count==6);
        if(Storage.GameDirectory is not null)
        {
            string path=GameDataMods.ArchivePath(Storage.GameDirectory),before=Storage.Hash(path);
            Check("Apply is visible beside the preset",Descendants<Button>(this).Any(b=>Equals(b.Content,"Apply Rainbow hunt")));
            Descendants<Button>(this).First(b=>Equals(b.Content,"Preview only")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check("Rainbow preset selects quality, rare dens and no empty pulls",_vm.EggMode==4&&_vm.DenMode==4&&_vm.NoLostEggs);
            var end=DateTime.UtcNow.AddSeconds(15);while(_vm.Busy&&DateTime.UtcNow<end)await Task.Delay(40);
            Check("Preview creates verified archive",!_vm.Busy&&_vm.LuckPreview.StartsWith("Ready:")&&_vm.LuckPreview.Contains("2,598"));
            Check("Preview leaves game unchanged",Storage.Hash(path)==before);
        }
        Navigate("Movement");Check("Movement sliders show values and reset controls",_vm.VisibleFeatures.Count==5&&Descendants<Slider>(this).Count(s=>s.IsVisible)==5&&Descendants<Button>(this).Count(b=>b.IsVisible&&Equals(b.Content,"Reset"))==5);
        var movement=_vm.VisibleFeatures[0];movement.Multiplier=2.25;Check("Slider values stay synchronized",movement.Target=="2.25"&&movement.MultiplierLabel=="2.25×");
        var slider=Descendants<Slider>(this).Single(s=>s.IsVisible&&ReferenceEquals(s.DataContext,movement));UpdateLayout();Slider.IncreaseSmall.Execute(null,slider);Check("Native slider keyboard command updates multiplier",movement.Multiplier==2.5&&slider.Value==2.5);
        OnResetMultiplier(Descendants<Button>(this).First(b=>b.IsVisible&&Equals(b.Content,"Reset")&&ReferenceEquals(b.DataContext,movement)),new RoutedEventArgs());Check("Reset returns to normal and switches off",movement.Multiplier==1&&!movement.Active);
        Navigate("Battle");Check("Ten battle controls include EXP, attack and defense sliders",_vm.VisibleFeatures.Count==10&&_vm.VisibleFeatures.Count(f=>f.IsSlider)==3);
        Navigate("Inventory");var loot=_vm.Features.Single(f=>f.Id=="loot");var choices=Descendants<ComboBox>(this).Single(c=>c.IsVisible&&ReferenceEquals(c.DataContext,loot));choices.SelectedItem="5";Check("Loot offers exactly 2×, 5× and 10×",loot.Target=="5"&&loot.LootOptions.SequenceEqual(new[]{"2","5","10"}));Navigate("Battle");
        Check("No Locate button or search setup remains",!Descendants<Button>(this).Any(b=>Equals(b.Content,"Locate")||Equals(b.Content,"Link this control")));
        var rider=_vm.Features[0];var toggle=Descendants<System.Windows.Controls.Primitives.ToggleButton>(this).First(b=>ReferenceEquals(b.DataContext,rider));
        Check("Disconnected controls start off and disabled",!toggle.IsEnabled&&!rider.Active);
        toggle.IsChecked=true;OnToggleFeature(toggle,new RoutedEventArgs());
        Check("Disconnected toggle rejected without a memory search",toggle.IsChecked==false&&!rider.Active&&_vm.Notice.Contains("automatic connection"));
        Check("Checks never attach to the real game",_engine.Memory is null);
        foreach(var check in await CheckOverlay())Check(check.Title,check.Passed);
        results.Add($"{results.Count-failures}/{results.Count} passed. No game or save files were changed.");
        File.WriteAllLines(Path.Combine(Storage.Root,"ui-test-report.txt"),results);return failures==0?0:1;
    }
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is T match) yield return match;
            foreach(var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    public void ReportError(Exception ex) { _engine.StopAll(); Log("Controls stopped",ex.Message); Notice(ex.Message); }
    void Notice(string text) { _vm.Notice=text; }
    void Log(string title,string detail)
    {
        _vm.RecentActivity.Insert(0,new(DateTime.Now.ToString("HH:mm"),title,detail));
        while(_vm.RecentActivity.Count>4) _vm.RecentActivity.RemoveAt(4);
        try { File.AppendAllText(Path.Combine(Storage.Root,"activity.log"),$"{DateTimeOffset.Now:O}\t{title}\t{detail.Replace('\n',' ')}{Environment.NewLine}"); } catch(IOException) { }
    }
    void UpdateSession()
    {
        bool connected=_engine.Memory?.IsAlive==true;
        _vm.ConnectionStatus=connected?"Connected · PID "+_engine.Pid:"Waiting for your game";
        _vm.ConnectLabel=connected?"Disconnect":"Connect to game";
        _vm.ActiveCount=_vm.Features.Count(f=>f.Active).ToString();
        _vm.LocatedCount=_vm.Features.Count(f=>f.Bound).ToString();
        _vm.HeroStatus=connected?"Automatic controls connected · switch on what you need":"Waiting for Stories 2 · connects automatically";
        bool running=Storage.GameDirectory is not null&&GameDataMods.IsGameRunning(Storage.GameDirectory);
        if(running || _launchRequested is not null && (DateTime.UtcNow-_launchRequested.Value).TotalSeconds>45) _launchRequested=null;
        _vm.LaunchLabel=running?"Game running":_launchRequested is not null?"Steam is starting…":"Launch game";
        _vm.CanLaunch=Storage.InstalledGame is not null&&!running&&_launchRequested is null&&!_vm.Busy;
        _vm.InstallationLabel=Storage.GameDirectory ?? "Game not detected in Steam libraries";
        _vm.FooterStatus=connected?"Automatic connection · battle and inventory structures verified · controls start off":Storage.GameDirectory is not null?"Steam installation detected · "+Storage.GameDirectory:"Stories 2 not detected · refresh Steam libraries";
    }
    void OnRefreshSteam(object sender,RoutedEventArgs e)
    {
        if(_vm.Busy || _engine.Memory is not null) { Notice("Disconnect and finish the current operation before refreshing the installation."); return; }
        try
        {
            Storage.Discover(); _identityHash=null;
            _vm.SavePath=Storage.SaveDirectory ?? "A single Steam save folder was not found.";
            if(Storage.GameDirectory is not null) { var info=Storage.InspectGame(); _identityHash=info.Hash; _vm.VersionLabel="Installed version "+info.Version; }
            else _vm.VersionLabel="Game installation not found";
            RefreshLuckStatus(); UpdateSession(); RefreshBackups();
            Notice(Storage.InstalledGame is null?"Steam libraries checked. Install Stories 2 in Steam, then refresh again.":"Stories 2 found in your Steam library. Launch game is ready.");
        }
        catch(Exception ex) { Notice(ex.Message); }
    }
    void OnLaunchGame(object sender,RoutedEventArgs e)
    {
        try
        {
            if(_vm.Busy) return;
            var game=Storage.InstalledGame ?? throw new InvalidOperationException("Refresh Steam libraries to find Stories 2 first.");
            if(GameDataMods.IsGameRunning(game.GameDirectory)) { Notice("Stories 2 is already running. Load a solo save, then connect."); return; }
            if(_launchRequested is not null) return;
            using var started=Process.Start(SteamInstallation.LaunchInfo(game));
            _launchRequested=DateTime.UtcNow; UpdateSession();
            Log("Launch requested","Asked Steam to start Monster Hunter Stories 2.");
            Notice("Steam is starting Stories 2. Load your solo save, then select Connect to game.");
        }
        catch(Exception ex) { Notice("Could not launch the game: "+ex.Message); }
    }
    LuckSettings LuckSelection()=>new(_vm.EggMode,_vm.DenMode,_vm.NoLostEggs,_vm.FreeCrafting,_vm.FreeForging,_vm.GenesLevelOne);
    void OnRainbowPreset(object sender,RoutedEventArgs e)
    {
        if(_vm.Busy) return;
        _vm.EggMode=4; _vm.DenMode=4; _vm.NoLostEggs=true;
        OnPreviewLuck(sender,e);
    }
    void OnApplyRainbow(object sender,RoutedEventArgs e)
    {
        if(_vm.Busy) return;
        _vm.EggMode=4; _vm.DenMode=4; _vm.NoLostEggs=true;
        OnInstallLuck(sender,e);
    }
    void RefreshLuckStatus(bool loadSettings=false)
    {
        try
        {
            _vm.LuckStatus=GameDataMods.Status(Storage.GameDirectory);
            if(loadSettings&&GameDataMods.InstalledSettings(Storage.GameDirectory) is {} settings)
            { _vm.EggMode=settings.Eggs; _vm.DenMode=settings.Dens; _vm.NoLostEggs=settings.NoLostEggs; _vm.FreeCrafting=settings.FreeCrafting;_vm.FreeForging=settings.FreeForging;_vm.GenesLevelOne=settings.GenesLevelOne; }
            _vm.RainbowAction=_vm.LuckStatus;
        }
        catch(Exception ex) { _vm.LuckStatus=ex.Message; }
    }
    async void OnPreviewLuck(object sender,RoutedEventArgs e)
    {
        if(_vm.Busy) return;
        try
        {
            var game=Storage.GameDirectory ?? throw new InvalidOperationException("Find Stories 2 using Refresh Steam libraries first.");
            var settings=LuckSelection(); _vm.Busy=true; UpdateSession();
            _vm.LuckPreview="Reading your installed tables and checking the changes…";
            var plan=await Task.Run(()=>GameDataMods.Build(GameDataMods.Original(game),settings));
            _vm.LuckPreview=$"Ready: {plan.EggGroups} egg probability groups, {plan.DenGroups} den patterns and {plan.LostGroups} empty-pull values changed. All 2,598 archive entries passed verification.\n\n{plan.Example}\n\nPreview only · game files unchanged. In-game behavior still needs testing.";
            _vm.RainbowAction="Preview only · game files unchanged. Press Apply Rainbow hunt to install this preset.";
            Log("Luck preview verified","Built from original data; no game files changed.");
        }
        catch(Exception ex) { _vm.LuckPreview=ex.Message; Notice(ex.Message); }
        finally { _vm.Busy=false; UpdateSession(); }
    }
    async void OnInstallLuck(object sender,RoutedEventArgs e)
    {
        if(_vm.Busy) return;
        var settings=sender is Button {Tag:"Restore"}?new LuckSettings(0,0,false):LuckSelection();
        try
        {
            var game=Storage.GameDirectory ?? throw new InvalidOperationException("Refresh Steam libraries to find Stories 2 first.");
            if(GameDataMods.IsGameRunning(game)) throw new InvalidOperationException("Close Stories 2 before applying or restoring luck settings.");
            _vm.Busy=true; _changingGameData=true; UpdateSession();
            _vm.LuckPreview="Preparing a verified backup and game-data update…";
            await Task.Run(()=> { Storage.Backup(); GameDataMods.Install(game,settings); });
            RefreshBackups(); RefreshLuckStatus(true);
            _vm.LuckPreview=settings.IsStock?"Original archive restored byte for byte. Launch the game to use its original game data.":"Luck settings installed and verified. Launch Stories 2 and enter a newly generated area or nest to test them. Existing eggs and already generated dens may keep their previous results.";
            Log(settings.IsStock?"Original game data restored":"Luck settings applied","Original archive backed up outside Steam; save backup verified.");
            Notice(_vm.LuckPreview);
        }
        catch(Exception ex) { _vm.LuckPreview=ex.Message; Notice(ex.Message); RefreshLuckStatus(); _vm.RainbowAction="Not applied · "+ex.Message; Log("Luck settings not applied",ex.Message); }
        finally { _changingGameData=false; _vm.Busy=false; UpdateSession(); }
    }
    void OnNavigate(object sender,RoutedEventArgs e)
    {
        if(sender is Button {Tag:string category})
        {
            if(category=="Backups")try{RefreshBackups();}catch(Exception ex){Notice("Could not refresh backups: "+ex.Message);}
            _vm.Navigate(category);MainScroll.ScrollToTop();
            if(category=="Research")OnOpenEditor(sender,e);
        }
    }
    void OnResetMultiplier(object sender,RoutedEventArgs e)
    {
        if(sender is FrameworkElement {DataContext:Feature feature})
        {
            try {_engine.SetActive(feature,false);feature.Multiplier=1;UpdateSession();Notice(feature.Title+" restored to normal.");}
            catch(Exception ex){Notice(ex.Message);}
        }
    }
    void OnOpenGame(object sender,RoutedEventArgs e) => OpenFolder(Storage.GameDirectory);
    static void OpenFolder(string? path)
    {
        if(path is null || !Directory.Exists(path)) throw new DirectoryNotFoundException("This folder is not available.");
        Process.Start(new ProcessStartInfo("explorer.exe"){Arguments=$"\"{path}\"",UseShellExecute=true});
    }
    void OnSource(object sender,RoutedEventArgs e)
    {
        if(sender is Button {Tag:string url} && Uri.TryCreate(url,UriKind.Absolute,out var uri) && uri.Scheme=="https") Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
    }
    async void OnConnect(object sender,RoutedEventArgs e)
    {
        if(_vm.Busy) return;
        if(_engine.Memory is not null)
        {
            _automaticConnectionPaused=true; _engine.Detach(); _backupThisSession=false;
            Log("Disconnected","Automatic reconnection paused until you connect again."); UpdateSession();return;
        }
        _automaticConnectionPaused=false; await ConnectAsync(false);
    }
    async Task ConnectAsync(bool automatic)
    {
        if(_connecting||_vm.Busy||_closing) return;
        _nextConnection=DateTime.UtcNow.AddSeconds(5);
        Process? selected=null;
        try
        {
            if(Storage.GameDirectory is null||_identityHash!=Storage.KnownHash) return;
            var expected=Path.Combine(Storage.GameDirectory,"game.exe");
            foreach(var process in Process.GetProcessesByName("game"))
            {
                try { if(string.Equals(process.MainModule?.FileName,expected,StringComparison.OrdinalIgnoreCase)) {selected=process;break;} } catch { }
                process.Dispose();
            }
            if(selected is null) { if(!automatic) Notice("Start Stories 2. This trainer connects automatically at the title screen.");return; }
            _connecting=true;_vm.Busy=true;
            var processToConnect=selected;selected=null;
            var connection=await Task.Run(()=>AutomaticConnection.Open(processToConnect,Storage.GameDirectory));
            if(_closing) {connection.Dispose();return;}
            _engine.Attach(connection);_backupThisSession=false;_lastConnectionError=null;
            Log("Automatic connection verified","Battle and inventory signatures matched. All switches are off.");
            Notice("Connected automatically. Switch on a control; it waits for battle or a loaded solo save when needed.");
        }
        catch(Exception ex)
        {
            _engine.Detach();
            if(_lastConnectionError!=ex.Message) {Notice(ex.Message);Log("Waiting for compatible game",ex.Message);_lastConnectionError=ex.Message;}
        }
        finally {selected?.Dispose();_connecting=false;_vm.Busy=false;UpdateSession();}
    }
    bool EnsureBackup()
    {
        if(_backupThisSession) return true;
        try { var backup=Storage.Backup(); _backupThisSession=true; RefreshBackups(); Log("Automatic backup verified",backup.Created+" · "+backup.Size); return true; }
        catch(Exception ex) { Notice("The first change needs a verified save backup: "+ex.Message); return false; }
    }
    void OnApplyFeature(object sender,RoutedEventArgs e)
    {
        if(_vm.Busy) return;
        if(sender is not FrameworkElement {DataContext:Feature feature}) return;
        try { if(!EnsureBackup()) return; var value=_engine.Apply(feature); Log(feature.Title+" updated",$"Applied {value:N0}. Saving in-game may preserve this change."); Notice($"{feature.Title} set to {value:N0}."); }
        catch(Exception ex) { Notice(ex.Message); }
    }
    void OnToggleFeature(object sender,RoutedEventArgs e)
    {
        if(sender is FrameworkElement {DataContext:Feature feature} control)
        {
            if(!_vm.Busy) Toggle(feature);
            control.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,(bool?)feature.Active);
        }
    }
    void Toggle(Feature feature)
    {
        try
        {
            if(feature.Active) { _engine.SetActive(feature,false); if(_engine.RestorationIssue is {} issue){Log("Restore needs attention",issue);Notice(issue);}else Log(feature.Title+" stopped","The trainer no longer maintains this value."); return; }
            if(_engine.Memory?.IsAlive!=true) throw new InvalidOperationException("Start Stories 2 and wait for automatic connection.");
            feature.CheckedTarget();
            if(!EnsureBackup()) return;
            _engine.SetActive(feature,true);
            Log(feature.Title+" armed",feature.Bound?"Enabled. Structures refresh automatically.":feature.Waiting+" · activates automatically when ready.");
            Notice(feature.Bound?feature.Title+" enabled.":feature.Title+" armed · "+feature.Waiting+". No manual search is needed.");
        }
        catch(Exception ex) { feature.Active=false; Notice(ex.Message); }
    }
    void OnStopAll(object sender,RoutedEventArgs e) => StopAll();
    void StopAll() { _engine.StopAll(); if(_engine.RestorationIssue is {} issue){Log("Restore needs attention",issue);Notice(issue);return;} Log("All controls stopped","Reversible values and the field item instruction restored when still owned."); Notice("All controls stopped. Saved edits remain; hunt and workshop settings have their own restore button."); }
    void OnTick(object? sender,EventArgs e)
    {
        if(_closing) return;
        _overlay.Tick();
        if(!_vm.Busy) _engine.Tick();
        if(++_ticks%20==0)
        {
            UpdateSession();
            if(_autoConnect&&!_automaticConnectionPaused&&!_connecting&&!_vm.Busy&&_engine.Memory is null&&DateTime.UtcNow>=_nextConnection) _=ConnectAsync(true);
        }
        if(!_autoConnect) return;
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out var foregroundPid);
        bool permitted=foregroundPid==(uint)Environment.ProcessId || (_engine.Pid>0 && foregroundPid==(uint)_engine.Pid);
        foreach(var key in Enumerable.Range(0x70,12))
        {
            bool down=(Native.GetAsyncKeyState(key)&0x8000)!=0;
            if(down && _down.Add(key) && permitted)
            {
                if(key==0x7B) { StopAll(); continue; }
                if(_vm.Busy) continue;
                var name=((Native.GetAsyncKeyState(0x11)&0x8000)!=0?"Ctrl+":"")+"F"+(key-0x70+1);
                var matches=_vm.Features.Where(f=>f.Hotkey==name).ToList();
                if(matches.Count==1) Toggle(matches[0]);
                else if(matches.Count>1) Notice(name+" is assigned more than once. Choose unique hotkeys in Profiles & hotkeys.");
            }
            if(!down) _down.Remove(key);
        }
    }
    void RefreshBackups() { _vm.Backups.Clear(); foreach(var backup in Storage.Backups()) _vm.Backups.Add(backup); _vm.BackupSummary=$"{_vm.Backups.Count} verified backup record(s) · stored outside Steam"; }
    void RefreshProfiles() { _vm.ProfileNames.Clear(); foreach(var profile in Storage.Profiles()) _vm.ProfileNames.Add(profile); }
    void OnBackup(object sender,RoutedEventArgs e)
    {
        if(_vm.Busy) return;
        try { var backup=Storage.Backup(); RefreshBackups(); Log("Save backup verified",backup.Created+" · "+backup.Size); Notice("Backup created and hash-verified. Your original saves were not changed."); }
        catch(Exception ex) { Notice(ex.Message); }
    }
    void OnRestore(object sender,RoutedEventArgs e)
    {
        if(_vm.Busy) return;
        if(sender is not FrameworkElement {DataContext:BackupInfo backup}) return;
        if(Storage.GameRunning()) { Notice("Close the game before restoring a save backup."); return; }
        var confirm=MessageBox.Show(this,$"Restore the backup from {backup.Created}?\n\nYour current saves will be backed up first. Check Steam Cloud sync before starting the game.","Restore save backup",MessageBoxButton.OKCancel,MessageBoxImage.Question);
        if(confirm!=MessageBoxResult.OK) return;
        try { Storage.Restore(backup); _engine.Detach(); _backupThisSession=false; RefreshBackups(); Log("Backup restored",backup.Created); Notice("Backup restored. Check Steam Cloud before loading your game."); }
        catch(Exception ex) { Notice(ex.Message); }
    }
    void ValidateHotkeys()
    {
        var keys=_vm.Features.Where(f=>f.Hotkey!="None").Select(f=>f.Hotkey).ToList();
        if(keys.Distinct().Count()!=keys.Count) throw new ArgumentException("Assign each hotkey to only one control.");
        if(keys.Any(k=>!_vm.HotkeyOptions.Contains(k))) throw new ArgumentException("Use F1–F11, Ctrl+F1–F11, or None. F12 is reserved for Disable all.");
    }
    void OnSaveProfile(object sender,RoutedEventArgs e)
    {
        try
        {
            ValidateHotkeys(); foreach(var feature in _vm.Features) feature.CheckedTarget();
            var name=ProfileName.Text.Trim();
            Storage.SaveProfile(new(name,_vm.ExperienceMultiplier,_vm.Features.Select(f=>new ProfileFeature(f.Id,f.Target,f.Hotkey,f.Favorite)).ToList()));
            RefreshProfiles(); ProfilePicker.SelectedItem=name; Log("Profile saved",name); Notice("Profile saved. Controls will still start off when loaded.");
        }
        catch(Exception ex) { Notice(ex.Message); }
    }
    void OnLoadProfile(object sender,RoutedEventArgs e)
    {
        try
        {
            if(ProfilePicker.SelectedItem is not string name) throw new ArgumentException("Choose a saved profile first.");
            var profile=Storage.LoadProfile(name);
            bool legacyAttack=profile.Features?.Any(f=>f.Id=="damage"&&Numbers.Parse(f.Target)>100&&Numbers.Parse(f.Target)<=30000)==true;
            if(legacyAttack)profile=profile with {Features=profile.Features!.Select(f=>f.Id=="damage"&&Numbers.Parse(f.Target)>100?f with {Target="100"}:f).ToList()};
            // Validate the entire file before changing any current setting.
            if(!double.IsFinite(profile.ExperienceMultiplier) || profile.ExperienceMultiplier is <1 or >20 || profile.Features is null) throw new InvalidDataException("The profile multiplier is invalid.");
            var keys=new HashSet<string>(); var ids=new HashSet<string>();
            foreach(var item in profile.Features)
            {
                if(!ids.Add(item.Id)) throw new InvalidDataException("The profile repeats a control.");
                var feature=_vm.Features.FirstOrDefault(f=>f.Id==item.Id); if(feature is null) continue;
                var value=Numbers.Parse(item.Target); if(value<feature.Minimum || value>feature.Maximum) throw new InvalidDataException("A profile value is outside its allowed range.");
                Numbers.Encode(value,feature.Kind);
                if(!_vm.HotkeyOptions.Contains(item.Hotkey) || (item.Hotkey!="None"&&!keys.Add(item.Hotkey))) throw new InvalidDataException("The profile has invalid or duplicate hotkeys.");
            }
            _engine.StopAll();
            foreach(var item in profile.Features) { var feature=_vm.Features.FirstOrDefault(f=>f.Id==item.Id); if(feature is null) continue; feature.Target=item.Target; feature.Hotkey=item.Hotkey; feature.Favorite=item.Favorite; }
            _vm.ExperienceMultiplier=1; ProfileName.Text=profile.Name; Log("Profile loaded",profile.Name+" · all controls off"); Notice(legacyAttack?"Profile loaded · old massive attack converted to 100×. All controls are off.":"Targets and hotkeys loaded. All controls are off.");
        }
        catch(Exception ex) { Notice(ex.Message); }
    }
    void OnClosing(object? sender,System.ComponentModel.CancelEventArgs e)
    {
        if(_changingGameData) { e.Cancel=true; Notice("Finishing the game-data update. Close the trainer when it completes."); return; }
        _closing=true; _studio?.Close(); _timer.Stop(); _overlay.Dispose();
        try{_gameDisplay.Restore();}catch(Exception ex){Log("Display restoration skipped",ex.Message);}
        _engine.Dispose();
    }
}

public sealed class TrainerViewModel : Observable
{
    public ObservableCollection<Feature> Features { get; }=Catalog.Create();
    public ObservableCollection<Feature> VisibleFeatures { get; }=[];
    public ObservableCollection<Activity> RecentActivity { get; }=[];
    public ObservableCollection<BackupInfo> Backups { get; }=[];
    public ObservableCollection<string> ProfileNames { get; }=[];
    public string[] HotkeyOptions { get; }=["None","F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","Ctrl+F1","Ctrl+F2","Ctrl+F3","Ctrl+F4","Ctrl+F5","Ctrl+F6","Ctrl+F7","Ctrl+F8","Ctrl+F9","Ctrl+F10","Ctrl+F11"];
    string _page="Battle",_search="",_notice="";
    string _status="Waiting for your game",_connect="Connect to game",_version="Inspecting installed game",_active="0",_located="0",_hero="Ready when you are",_footer="Waiting for a connection",_save="",_backupSummary="";
    bool _busy;
    double _multiplier=1;
    int _featureColumns=2,_eggMode=1,_denMode=1;
    bool _noLostEggs,_canLaunch,_freeCrafting,_freeForging,_genesLevelOne;
    public bool FreeCrafting {get=>_freeCrafting;set=>Set(ref _freeCrafting,value);}
    public bool FreeForging {get=>_freeForging;set=>Set(ref _freeForging,value);}
    public bool GenesLevelOne {get=>_genesLevelOne;set=>Set(ref _genesLevelOne,value);}
    string _launchLabel="Launch game",_installation="Checking Steam libraries",_luckStatus="Reading game data",_luckPreview="Choose your settings, then preview the changes. Applying requires the game to be closed. No settings are installed automatically.";
    public TrainerViewModel() => RefreshFeatures();
    public string PageTitle => _page switch {"Backups"=>"Save backups","Profiles"=>"Profiles & hotkeys","Research"=>"Egg & gene studio","Luck"=>"Hunt & workshop",_=>_page+" controls"};
    public bool ShowFeatures => _page is "Battle" or "Inventory" or "Exploration" or "Movement" or "Progression";
    public bool ShowBackups => _page=="Backups";
    public bool ShowProfiles => _page=="Profiles";
    public bool ShowResearch => _page=="Research";
    public bool ShowLuck => _page=="Luck";
    public int EggMode {get=>_eggMode;set=>Set(ref _eggMode,value);}
    public int DenMode {get=>_denMode;set=>Set(ref _denMode,value);}
    public bool NoLostEggs {get=>_noLostEggs;set=>Set(ref _noLostEggs,value);}
    public bool CanLaunch {get=>_canLaunch;set=>Set(ref _canLaunch,value);}
    public string LaunchLabel {get=>_launchLabel;set=>Set(ref _launchLabel,value);}
    public string InstallationLabel {get=>_installation;set=>Set(ref _installation,value);}
    public string LuckStatus {get=>_luckStatus;set=>Set(ref _luckStatus,value);}
    public string LuckPreview {get=>_luckPreview;set=>Set(ref _luckPreview,value);}
    string _rainbowAction="Close Stories 2, then press Apply Rainbow hunt to install.";
    public string RainbowAction {get=>_rainbowAction;set=>Set(ref _rainbowAction,value);}
    public string SectionTitle => _page switch {"Movement"=>"Explore at your own pace.","Exploration"=>"Make every outing count.","Inventory"=>"Less grinding. More exploring.","Progression"=>"Grow at your own pace.",_=>"Give your party an edge."};
    public string SectionSubtitle => "Switch on to arm. Applies automatically when its game context is ready.";
    public string BattleNav => "Battle";
    public string InventoryNav => "Inventory";
    public string ExplorationNav => "Exploration";
    public string MovementNav => "Movement";
    public string ProgressionNav => (_page=="Progression"?"●  ":"     ")+"Progression";
    public void Navigate(string page)
    {
        _page=page; _search="";
        foreach(var name in new[]{nameof(PageTitle),nameof(ShowFeatures),nameof(ShowBackups),nameof(ShowProfiles),nameof(ShowResearch),nameof(ShowLuck),nameof(SectionTitle),nameof(Search),nameof(BattleNav),nameof(InventoryNav),nameof(ExplorationNav),nameof(MovementNav),nameof(ProgressionNav)}) Notify(name);
        RefreshFeatures();
    }
    void RefreshFeatures() { VisibleFeatures.Clear(); foreach(var f in Features.Where(f=>f.Category==_page && (string.IsNullOrWhiteSpace(_search)||(f.Title+" "+f.Description).Contains(_search,StringComparison.OrdinalIgnoreCase)))) VisibleFeatures.Add(f); }
    public string Search { get=>_search; set { if(Set(ref _search,value)) RefreshFeatures(); } }
    public bool Busy { get=>_busy;set { if(Set(ref _busy,value)) Notify(nameof(NotBusy)); } }
    public bool NotBusy => !Busy;
    public int FeatureColumns { get=>_featureColumns;set=>Set(ref _featureColumns,value); }
    public double ExperienceMultiplier { get=>_multiplier;set { if(Set(ref _multiplier,value)) Notify(nameof(ExperienceLabel)); } }
    public string ExperienceLabel => $"{ExperienceMultiplier:N0}× newly observed EXP";
    public string Notice { get=>_notice;set { if(Set(ref _notice,value)) Notify(nameof(HasNotice)); } }
    public bool HasNotice => !string.IsNullOrEmpty(Notice);
    public string ConnectionStatus { get=>_status;set=>Set(ref _status,value); }
    public string ConnectLabel { get=>_connect;set=>Set(ref _connect,value); }
    public string VersionLabel { get=>_version;set=>Set(ref _version,value); }
    public string ActiveCount { get=>_active;set=>Set(ref _active,value); }
    public string LocatedCount { get=>_located;set=>Set(ref _located,value); }
    public string HeroStatus { get=>_hero;set=>Set(ref _hero,value); }
    public string FooterStatus { get=>_footer;set=>Set(ref _footer,value); }
    public string SavePath { get=>_save;set=>Set(ref _save,value); }
    public string BackupSummary { get=>_backupSummary;set=>Set(ref _backupSummary,value); }
}
