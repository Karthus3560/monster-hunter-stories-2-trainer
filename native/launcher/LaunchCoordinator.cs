using System.Diagnostics;
using System.Text.Json;
using KinshipNative;
namespace KinshipLauncher;
internal record LaunchUpdate(int Step,string Message);
internal record LaunchResult(int Pid,bool AlreadyLoaded,string Folder);
internal static class LaunchCoordinator
{
 public static string RuntimeRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Kinship Studio Runtime");
 public static async Task<LaunchResult> Play(string folder,IProgress<LaunchUpdate> progress,CancellationToken token)=>await Task.Run(()=>{
  progress.Report(new(0,"Checking your game and Studio files…"));Session.ValidateGame(folder,false);folder=Session.Canonical(folder);GameDiscovery.Save(folder);
  if(File.Exists(Path.Combine(folder,"dinput8.dll")))throw new IOException("An input-library mod is installed in the game folder. Remove the failed Studio startup loader before using this launcher. Other loaders should be reviewed for compatibility.");
  if(Process.GetProcessesByName("RidersToolkit").Any())throw new IOException("Close the old desktop trainer first so it does not compete with the in-game Studio.");
  var manifest=Session.Manifest();Process? game=GameDiscovery.Running(folder);
  if(game==null){progress.Report(new(1,"Starting Stories 2 through Steam…"));GameDiscovery.StartSteam();for(int i=0;i<120;i++){token.ThrowIfCancellationRequested();Thread.Sleep(1000);game=GameDiscovery.Running(folder);if(game!=null)break;}}
  if(game==null)throw new IOException("The game did not start. Open Steam, confirm Stories 2 is installed, then try again.");
  using(game){long creation=game.StartTime.ToFileTimeUtc();using var mutex=new Mutex(false,$"Local\\KinshipStudioAttach-{game.Id}-{creation}");bool owned;try{owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}if(!owned)throw new IOException("Studio is already being loaded by another launcher. Wait for it to finish.");
   try{
    using var native=new NativeSession(game,manifest);var active=native.Adopt();if(active!=null){StartHelper(game.Id,creation,folder,active);return new LaunchResult(game.Id,true,active);}
    progress.Report(new(1,"The game is running. Waiting for its menus…"));
    string runtime=Path.Combine(RuntimeRoot,"sessions",$"{game.Id}-{creation}");Directory.CreateDirectory(RuntimeRoot);Session.Ordinary(RuntimeRoot);
    if(File.Exists(Path.Combine(runtime,"load-attempt.json")))throw new IOException("Studio already had a loading attempt in this session. Restart the game before trying again.");
    if(!File.Exists(Path.Combine(runtime,"package-attempt.json"))){int result=Session.Prepare(game.Id,creation,folder,runtime,text=>progress.Report(new(2,text)),token);if(result!=0)throw new IOException("The game closed before Studio was ready.");}
    token.ThrowIfCancellationRequested();progress.Report(new(2,"Loading Studio and your custom Camp icon…"));native.Load(runtime,text=>progress.Report(new(2,text)));StartHelper(game.Id,creation,folder,runtime);Session.Log("Launcher verified Studio and icon for PID "+game.Id);return new LaunchResult(game.Id,false,runtime);
   }finally{mutex.ReleaseMutex();}
  }
 },token);
 static void StartHelper(int pid,long creation,string game,string folder){
  var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=AppContext.BaseDirectory};
  if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(App).Assembly.Location);
  foreach(var arg in new[]{"--serve-module",pid.ToString(),creation.ToString(),game,folder})start.ArgumentList.Add(arg);
  using var child=Process.Start(start)??throw new IOException("The session backup helper could not start.");
 }
}
