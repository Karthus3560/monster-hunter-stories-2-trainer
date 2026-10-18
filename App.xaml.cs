using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RidersToolkit;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--fixture")) { ShutdownMode = ShutdownMode.OnExplicitShutdown; Fixture.Run(); Shutdown(); return; }
        if (e.Args.Contains("--inspect-offline-saves")) {ShutdownMode=ShutdownMode.OnExplicitShutdown;Shutdown(OfflineEditorTests.InspectInstalledReadOnly());return;}
        if (e.Args.Contains("--inspect-game")) {ShutdownMode=ShutdownMode.OnExplicitShutdown;Shutdown(AutomaticTests.InspectInstalled());return;}
        if (e.Args.Contains("--test-live-health")) {ShutdownMode=ShutdownMode.OnExplicitShutdown;Shutdown(AutomaticTests.TestLiveBattle());return;}
        if (e.Args.Contains("--test-live-exploration")) {ShutdownMode=ShutdownMode.OnExplicitShutdown;Shutdown(AutomaticTests.TestLiveExploration());return;}
        if (e.Args.Contains("--apply-rainbow")) {ShutdownMode=ShutdownMode.OnExplicitShutdown;Shutdown(InstallRainbow());return;}
        if (e.Args.Contains("--self-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var code = SelfTests.Run(e.Args.Contains("--portable")); Shutdown(code); return;
        }
        DispatcherUnhandledException += (_, a) =>
        {
            if (e.Args.Contains("--ui-test"))
            {File.WriteAllText(Path.Combine(Storage.Root,"ui-test-report.txt"),"FAIL  Interface check exception: "+a.Exception);a.Handled=true;Shutdown(1);return;}
            if (MainWindow is MainWindow main) main.ReportError(a.Exception);
            a.Handled = true;
        };
        int themeIndex=Array.IndexOf(e.Args,"--theme");
        ThemeManager.Initialize(themeIndex>=0&&e.Args.Length>themeIndex+1?e.Args[themeIndex+1]:null);
        var window = new MainWindow(!e.Args.Contains("--ui-test") && !e.Args.Contains("--screenshot"));
        MainWindow = window;
        var pageIndex = Array.IndexOf(e.Args, "--page");
        if (pageIndex >= 0 && e.Args.Length > pageIndex + 1) window.PreviewPage(e.Args[pageIndex + 1]);
        var screenshot = Array.IndexOf(e.Args, "--screenshot");
        // Preview-only sizing helps verify small desktop layouts without changing user settings.
        var previewSize = Array.IndexOf(e.Args, "--preview-size");
        if (screenshot >= 0 && previewSize >= 0 && e.Args.Length > previewSize + 1)
        {
            var dimensions = e.Args[previewSize + 1].Split('x');
            if (dimensions.Length == 2 && double.TryParse(dimensions[0], out var width)
                && double.TryParse(dimensions[1], out var height))
            {
                window.Width = Math.Clamp(width, window.MinWidth, SystemParameters.WorkArea.Width);
                window.Height = Math.Clamp(height, window.MinHeight, SystemParameters.WorkArea.Height);
            }
        }
        if (e.Args.Contains("--ui-test"))
        {
            window.Loaded += async (_, _) =>
            {
                await Task.Delay(400);
                var code = await window.RunUiChecks();
                Shutdown(code);
            };
        }
        if (screenshot >= 0 && e.Args.Length > screenshot + 1)
        {
            window.Loaded += async (_, _) =>
            {
                await Task.Delay(700);
                Window preview=window;
                if(e.Args.Contains("--overlay"))
                {
                    var popup=window.PreviewOverlay();preview=popup;
                    if(pageIndex>=0&&e.Args.Length>pageIndex+1)popup.SelectCategory(e.Args[pageIndex+1]);
                }
                if(e.Args.Contains("--studio"))preview=window.PreviewStudio();
                if(e.Args.Contains("--updates"))preview=window.PreviewUpdates();
                if(e.Args.Contains("--add-egg"))preview=window.PreviewAddEgg();
                preview.UpdateLayout();
                await Task.Delay(160); // Let resource images complete their first render in a newly opened popup.
                preview.UpdateLayout();
                if(e.Args.Contains("--frame"))
                {
                    OwnWindowCapture.Save(preview,e.Args[screenshot+1]);window.Close();return;
                }
                var content = (FrameworkElement)preview.Content;
                var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(e.Args[screenshot + 1])) encoder.Save(stream);
                window.Close();
            };
        }
        window.Show();
    }
    static int InstallRainbow()
    {
        Storage.Discover();
        string report=Path.Combine(Storage.Root,"rainbow-install-report.json");
        try
        {
            string game=Storage.GameDirectory ?? throw new InvalidOperationException("Stories 2 was not found in Steam.");
            if(GameDataMods.IsGameRunning(game)) throw new InvalidOperationException("Save and close Stories 2 before applying Rainbow hunt.");
            string path=GameDataMods.ArchivePath(game),before=Storage.Hash(path);
            var backup=Storage.Backup(); var settings=(GameDataMods.InstalledSettings(game)??new LuckSettings(0,0,false)) with {Eggs=4,Dens=4,NoLostEggs=true};
            GameDataMods.Install(game,settings);
            string after=Storage.Hash(path);
            if(GameDataMods.InstalledSettings(game)!=settings || after==GameDataMods.StockHash)
                throw new IOException("Rainbow installation verification failed.");
            File.WriteAllText(report,System.Text.Json.JsonSerializer.Serialize(new{Success=true,Before=before,Installed=after,Settings=settings,SaveBackup=backup.Path,Status=GameDataMods.Status(game)},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            File.AppendAllText(Path.Combine(Storage.Root,"activity.log"),$"{DateTimeOffset.Now:O}\tLuck settings applied\tRainbow hunt installed and verified; saves and original archive backed up.{Environment.NewLine}");
            return 0;
        }
        catch(Exception ex)
        {
            File.WriteAllText(report,System.Text.Json.JsonSerializer.Serialize(new{Success=false,Error=ex.Message}));
            return 1;
        }
    }
}

internal static class Fixture
{
    // Private child-process target used by the integration tests; never a game process.
    public static void Run()
    {
        var memory = Marshal.AllocHGlobal(4096);
        try
        {
            var data = new byte[4096];
            Array.Fill(data, (byte)0xAB);
            BitConverter.GetBytes(914537).CopyTo(data, 128);
            BitConverter.GetBytes(274).CopyTo(data, 132);
            Marshal.Copy(data, 0, memory, data.Length);
            var path = Environment.GetEnvironmentVariable("RIDERS_FIXTURE_PATH")!;
            File.WriteAllText(path, $"{Environment.ProcessId}|{memory.ToInt64()}");
            var end = DateTime.UtcNow.AddSeconds(40);
            while (DateTime.UtcNow < end && !File.Exists(path + ".stop")) Thread.Sleep(50);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
}
