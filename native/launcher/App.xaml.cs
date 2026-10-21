using System.Windows;
using KinshipNative;
namespace KinshipLauncher;
public partial class App : Application
{
 Mutex? uiMutex;
 protected override void OnStartup(StartupEventArgs e){base.OnStartup(e);Session.Package=Path.Combine(AppContext.BaseDirectory,"modules");
  if(e.Args.Length==5&&e.Args[0]=="--serve-module"){try{Service.Serve(int.Parse(e.Args[1]),long.Parse(e.Args[2]),Session.Canonical(e.Args[3]),e.Args[4]);Shutdown(0);}catch(Exception error){Session.Log(error.ToString());Shutdown(1);}return;}
  if(e.Args.Length==4&&(e.Args[0]=="--check-install"||e.Args[0]=="--check-remove")){string message="";try{LauncherInstallPolicy.Check(e.Args[1],e.Args[0]=="--check-install"?e.Args[3]:null,e.Args[0]=="--check-remove");}catch(Exception error){message=error.Message;}File.WriteAllText(e.Args[2],message);Shutdown(message.Length==0?0:1);return;}
  if(e.Args.Length>0&&e.Args[0]!="--render-preview"&&e.Args[0]!="--attach-existing"&&e.Args[0]!="--play"){Shutdown(2);return;}
  uiMutex=new Mutex(true,"Local\\KinshipStudio.NativeLauncher.UI",out bool first);if(!first){MessageBox.Show("Kinship Studio is already open.","Kinship Studio",MessageBoxButton.OK,MessageBoxImage.Information);Shutdown();return;}
  var window=new MainWindow();MainWindow=window;window.Show();
  if(e.Args.Length==2&&e.Args[0]=="--render-preview")window.RenderPreview(e.Args[1]);
  if(e.Args.Length==1&&e.Args[0]=="--attach-existing")window.AttachAfterDetection=true;
  if(e.Args.Length==1&&e.Args[0]=="--play")window.PlayAfterDetection=true;
 }
}
