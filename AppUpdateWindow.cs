using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RidersToolkit;

internal sealed class AppUpdateWindow : Window
{
    readonly TextBlock _status = new() { Text = "Checking the latest stable release…", FontSize = 15, TextWrapping = TextWrapping.Wrap, Margin = new(0, 16, 0, 10) };
    readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 0, 0, 18) };
    readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 7, Margin = new(0, 0, 0, 18), Visibility = Visibility.Collapsed };
    readonly Button _action = new() { Content = "Check again", IsEnabled = false, MinWidth = 140 };
    readonly CancellationTokenSource _cancel = new();
    readonly System.Net.Http.HttpClient _http = AppUpdates.CreateClient();
    readonly Action _installing;
    AppRelease? _release;
    string? _download;
    bool _busy;

    internal AppUpdateWindow(Window owner, Action installing, bool preview = false)
    {
        Owner = owner; _installing = installing; Title = "Kinship Studio · Updates";
        Icon = owner.Icon; Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Canvas"); SetResourceReference(ForegroundProperty, "Ink");
        var body = new StackPanel { Margin = new(26) };
        var title = new TextBlock { Text = "Ready for the next hunt?", FontSize = 23, FontWeight = FontWeights.SemiBold };
        body.Children.Add(title);
        body.Children.Add(new TextBlock { Text = $"Kinship Studio {AppUpdates.CurrentVersion}", FontSize = 12, Margin = new(0, 8, 0, 0) });
        body.Children.Add(_status); body.Children.Add(_detail); body.Children.Add(_progress);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        _action.SetResourceReference(StyleProperty, "Primary"); _action.Click += OnAction; buttons.Children.Add(_action);
        var close = new Button { Content = "Close", Margin = new(10, 0, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close(); buttons.Children.Add(close); body.Children.Add(buttons); Content = body;
        ThemeManager.BindLocalBrushes(this);
        SourceInitialized += (_, _) => WindowLayout.ApplyCaption(this);
        Closed += (_, _) => { _cancel.Cancel(); _http.Dispose(); _cancel.Dispose(); };
        if (preview)
        {
            _status.Text = "Updates replace the installed app.";
            _detail.Text = "Your profiles, theme and save backups stay where they are. The portable ZIP is still available for manual use.";
            _action.IsEnabled = true; _action.Content = "Check for updates";
        }
        else Loaded += async (_, _) => await Check();
    }

    async Task Check()
    {
        _busy = true; _action.IsEnabled = false;
        _status.Text = "Checking the latest stable release…";
        _detail.Text = "Connecting to this project's GitHub releases. Nothing installs automatically.";
        try
        {
            _release = await new AppUpdates(_http).CheckAsync(AppUpdates.CurrentVersion, _cancel.Token);
            _status.Text = _release is null ? "You’re up to date." : $"Kinship Studio {_release.Version} is available.";
            bool installed = AppUpdates.IsInstalledCopy(Environment.ProcessPath!);
            _detail.Text = _release is null ? "You have the latest published stable version." : installed
                ? "This upgrades the existing installation and keeps your profiles, theme and save backups. Download first, then restart when ready."
                : "This is a portable copy. Setup installs or upgrades the main app and creates its shortcut. Your portable folder remains for manual use; your settings and backups are shared.";
            _action.Content = _release is null ? "Check again" : "Download update";
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { return; }
        catch (Exception ex) { _status.Text = "Couldn’t check for updates."; _detail.Text = ex.Message; _action.Content = "Try again"; }
        finally { _busy = false; if (IsVisible) _action.IsEnabled = true; }
    }

    async void OnAction(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_release is null) { await Check(); return; }
        if (_download is not null)
        {
            try
            {
                AppUpdates.VerifyFile(_download, _release);
                _installing(); // Restores trainer-owned live changes before starting Setup.
                _ = Process.Start(AppUpdates.InstallCommand(_download)) ?? throw new IOException("Windows could not start the update installer.");
                Owner.Close();
            }
            catch (Exception ex) { _status.Text = "The update could not start."; _detail.Text = ex.Message; }
            return;
        }
        _busy = true; _action.IsEnabled = false; _progress.Visibility = Visibility.Visible;
        _status.Text = "Downloading your update…"; _detail.Text = "The app keeps running until you choose Restart & update.";
        try
        {
            var progress = new Progress<double>(value => { if (IsVisible) _progress.Value = value; });
            _download = await new AppUpdates(_http).DownloadAsync(_release, Path.Combine(Storage.Root, "Updates"), progress, _cancel.Token);
            _status.Text = "Your update is ready.";
            _detail.Text = "SHA-256 verified against GitHub release metadata. This checks download integrity; it is separate from a Windows publisher signature. Restart closes the trainer, upgrades the installed app and opens it again.";
            _action.Content = "Restart & update";
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { return; }
        catch (Exception ex) { _status.Text = "The download could not finish."; _detail.Text = ex.Message; _action.Content = "Retry download"; }
        finally { _busy = false; if (IsVisible) _action.IsEnabled = true; }
    }
}
