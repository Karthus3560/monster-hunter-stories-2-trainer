using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using KinshipNative;
using System.Text.Json;
namespace KinshipLauncher;
public partial class MainWindow : Window
{
 string? folder;bool busy,closeWhenReady;int readyPid;readonly CancellationTokenSource cancellation=new();readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(2)};
 public bool AttachAfterDetection {get;set;}
 public bool PlayAfterDetection {get;set;}
 public MainWindow(){InitializeComponent();Loaded+=async(_,_)=>await Detect();Closing+=OnClosing;timer.Tick+=(_,_)=>{if(readyPid!=0&&!busy){try{using var p=Process.GetProcessById(readyPid);if(!p.HasExited)return;}catch{}readyPid=0;PlayButton.Content="Play with Studio";Status.Text="Your game has closed. Ready for the next adventure.";SetSteps(0);}};timer.Start();}
 async Task Detect(){folder=await Task.Run(GameDiscovery.Detect);ShowFolder();if(AttachAfterDetection){using var running=folder==null?null:GameDiscovery.Running(folder);if(running==null){Status.Text="Start the game first to test the existing-session connection.";return;}await Play();}else if(PlayAfterDetection)await Play();}
 void ShowFolder(){bool found=folder!=null;GameBadge.Text=found?"Game found · supported version":"Choose your game folder";GameBadge.Foreground=Brush(found?"#AAD8BE":"#E7BD76");GamePath.Text=folder??"Select the folder containing Stories 2’s game.exe";GamePath.ToolTip=folder;PlayButton.IsEnabled=VanillaButton.IsEnabled=found&&!busy;Status.Text=found?"Ready when you are. Open Camp → page 2 after Studio loads.":"Steam detection did not find a supported copy. Choose Browse to locate your game.";SetSteps(found?0:-1);}
 void BrowseClick(object sender,RoutedEventArgs e){var picker=new OpenFolderDialog{Title="Choose your Monster Hunter Stories 2 folder"};if(picker.ShowDialog(this)!=true)return;try{Session.ValidateGame(picker.FolderName,false);folder=Session.Canonical(picker.FolderName);GameDiscovery.Save(folder);ShowFolder();}catch(Exception error){Status.Text=error.Message;Status.Foreground=Brush("#F1B1A6");}}
 async void PlayClick(object sender,RoutedEventArgs e){if(readyPid!=0){FocusGame(readyPid);return;}await Play();}
 async Task Play(){if(folder==null||busy)return;busy=true;PlayButton.IsEnabled=VanillaButton.IsEnabled=BrowseButton.IsEnabled=false;PlayButton.Content="Preparing your adventure…";Status.Foreground=Brush("#A5B9BB");try{var result=await LaunchCoordinator.Play(folder,new Progress<LaunchUpdate>(u=>{Status.Text=u.Message;SetSteps(u.Step);State("loading",u.Message,0);}),cancellation.Token);readyPid=result.Pid;SetSteps(3);Status.Text=result.AlreadyLoaded?"Studio is already active. Open Camp → page 2 → Kinship Studio.":"Studio is ready. Open Camp → page 2 → Kinship Studio.";Status.Foreground=Brush("#AAD8BE");PlayButton.Content="Return to game";State("ready",Status.Text,result.Pid);}catch(OperationCanceledException){Status.Text="Loading cancelled. Your game is still running.";PlayButton.Content="Play with Studio";State("cancelled",Status.Text,0);}catch(Exception error){Session.Log(error.ToString());Status.Text=error.Message;Status.Foreground=Brush("#F1B1A6");PlayButton.Content="Try again";State("error",Status.Text,0);}finally{busy=false;PlayButton.IsEnabled=VanillaButton.IsEnabled=BrowseButton.IsEnabled=folder!=null;if(closeWhenReady){timer.Stop();Application.Current.Shutdown();}}}
 static void State(string state,string message,int pid){try{Prefs.Text(Path.Combine(Session.Data,"launcher-state.json"),JsonSerializer.Serialize(new{State=state,Message=message,Pid=pid,Time=DateTimeOffset.UtcNow}));}catch(IOException){}}
 void VanillaClick(object sender,RoutedEventArgs e){try{GameDiscovery.StartSteam();Status.Text="Starting the game through Steam without loading Studio.";}catch(Exception error){Status.Text=error.Message;}}
 void HelpClick(object sender,RoutedEventArgs e){Directory.CreateDirectory(Session.Data);Process.Start(new ProcessStartInfo(Session.Data){UseShellExecute=true});}
 void SetSteps(int current){var panels=new[]{Step0,Step1,Step2};var marks=new[]{Mark0,Mark1,Mark2};for(int i=0;i<3;i++){panels[i].Background=Brush(current>i?"#3E6657":current==i?"#997A43":"#284650");marks[i].Text=current>i?"✓":(i+1).ToString();}}
 static SolidColorBrush Brush(string color)=>new((Color)ColorConverter.ConvertFromString(color));
 void OnClosing(object? sender,CancelEventArgs e){if(busy){e.Cancel=true;closeWhenReady=true;WindowState=WindowState.Minimized;return;}timer.Stop();Application.Current.Shutdown();}
 [System.Runtime.InteropServices.DllImport("user32")] static extern bool SetForegroundWindow(nint window);
 static void FocusGame(int pid){try{using var p=Process.GetProcessById(pid);SetForegroundWindow(p.MainWindowHandle);}catch{}}
 public void RenderPreview(string file){Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,async()=>{await Task.Delay(800);UpdateLayout();var content=(FrameworkElement)Content;var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(file);png.Save(stream);timer.Stop();Application.Current.Shutdown();});}
}
