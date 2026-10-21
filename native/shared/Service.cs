using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace KinshipNative;
internal record HostConfig(string GameDirectory,string SaveDirectory);
internal record RestoreRequest(string BackupId,int ProcessId,long CreationTime);
internal static class Service
{
 public static string? ResolveSaves(string game){
  // Adopt the user's previous verified account, otherwise use the active Steam account.
  var config=Path.Combine(Session.Root,"host.json");
  try{var old=JsonSerializer.Deserialize<HostConfig>(File.ReadAllText(config));if(old!=null&&string.Equals(Path.GetFullPath(old.GameDirectory),game,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(old.SaveDirectory)){Session.Ordinary(old.SaveDirectory);return old.SaveDirectory;}}catch(IOException){}catch(JsonException){}
  string steam=(string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath",null)??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam");
  string users=Path.Combine(steam,"userdata");if(!Directory.Exists(users))return null;
  var candidates=Directory.EnumerateDirectories(users).Select(p=>Path.Combine(p,"1277400","remote")).Where(p=>Directory.Exists(p)&&BackupStore.Allowed.Any(n=>File.Exists(Path.Combine(p,n)))).ToArray();
  uint active=unchecked((uint)(int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess","ActiveUser",0)??0));
  var chosen=candidates.SingleOrDefault(p=>string.Equals(p,Path.Combine(users,active.ToString(),"1277400","remote"),StringComparison.OrdinalIgnoreCase))??(candidates.Length==1?candidates[0]:null);
  if(chosen!=null){Session.Ordinary(chosen);Prefs.Text(config,JsonSerializer.Serialize(new HostConfig(game,chosen)));}return chosen;
 }
 public static void Index(BackupStore store)=>Prefs.Text(Path.Combine(Session.Data,"backup-index.tsv"),string.Join('\n',store.List().Select(b=>$"{b.Id}\t{b.Date}\t{b.Summary}")));
 static void Request(string path,BackupStore store,int pid,long creation){
  var words=File.ReadAllText(path).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);var response=Path.Combine(Path.GetDirectoryName(path)!,"response.tsv");
  if(words.Length!=7||words[0]!="KSNREQUEST1"||words[1]!=pid.ToString()||words[2]!=creation.ToString()||!ulong.TryParse(words[3],out var request)||!int.TryParse(words[4],out int action))throw new InvalidDataException("Invalid session request.");
  var guard=File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!,"session.guard")).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
  if(guard.Length!=4||guard[0]!="KSNSESSION1"||guard[1]!=words[1]||guard[2]!=words[2])throw new InvalidDataException("Request guard mismatch.");
  try{string result;
   switch(action){
    case 1:store.Create();result="Backup created and verified.";break;
    case 2:store.Verify(words[5]);result="Selected backup passed verification.";break;
    case 3:store.Verify(words[5]);Prefs.Text(Path.Combine(Session.Data,"restore-pending.json"),JsonSerializer.Serialize(new RestoreRequest(words[5],pid,creation)));result="Restore queued. Exit the game to restore this backup.";break;
    case 4:File.Delete(Path.Combine(Session.Data,"restore-pending.json"));result="Queued restore cancelled.";break;
    default:throw new InvalidDataException("Unknown backup action.");
   }Index(store);Prefs.Text(response,$"{request}\tOK\t{result}");
  }catch(Exception e){Prefs.Text(response,$"{request}\tERROR\t{e.Message.Replace('\n',' ').Replace('\r',' ').Replace('\t',' ')}");}
  File.Move(path,path+".handled",true);
 }
 public static void Serve(int pid,long creation,string game,string? moduleFolder=null){
  using var process=Process.GetProcessById(pid);if(process.StartTime.ToFileTimeUtc()!=creation||!string.Equals(process.MainModule?.FileName,Path.Combine(game,"game.exe"),StringComparison.OrdinalIgnoreCase))throw new IOException("Session changed.");
  using var mutex=new Mutex(true,$"Local\\KinshipStudioSession-{pid}-{creation}",out bool first);if(!first)return;
  string folder=Session.Folder(pid,creation);
  if(moduleFolder!=null){
   folder=Session.Canonical(moduleFolder);
   if(!process.Modules.Cast<ProcessModule>().Any(m=>m.ModuleName=="KinshipNativeBattleV2.dll"&&string.Equals(Session.Canonical(m.FileName),Path.Combine(folder,"KinshipNativeBattleV2.dll"),StringComparison.OrdinalIgnoreCase)))throw new IOException("The requested module folder does not belong to this game session.");
   var guard=File.ReadAllText(Path.Combine(folder,"session.guard")).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
   if(guard.Length!=4||guard[0]!="KSNSESSION1"||guard[1]!=pid.ToString()||guard[2]!=creation.ToString())throw new IOException("Module folder session guard mismatch.");
  }
  string request=Path.Combine(folder,"request.txt");BackupStore? store=null;long nextIndex=0;
  while(!process.HasExited){
   try{
    if(store==null){var saves=ResolveSaves(game);if(saves!=null)store=new BackupStore(Path.Combine(Session.Data,"backups"),saves,()=>Session.Running(game));}
    if(store!=null&&Environment.TickCount64>nextIndex){Index(store);nextIndex=Environment.TickCount64+10000;}
    if(File.Exists(request)){if(store==null){var words=File.ReadAllText(request).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);if(words.Length==7)Prefs.Text(Path.Combine(folder,"response.tsv"),$"{words[3]}\tERROR\tNo single Steam save account found. Save the game and retry.");File.Move(request,request+".handled",true);}else Request(request,store,pid,creation);}
   }catch(Exception e){Session.Log("Backup request: "+e.Message);if(File.Exists(request))File.Move(request,request+".rejected",true);}
   Thread.Sleep(500);
  }
  var pendingPath=Path.Combine(Session.Data,"restore-pending.json");
  if(store!=null&&File.Exists(pendingPath)){
   var pending=JsonSerializer.Deserialize<RestoreRequest>(File.ReadAllText(pendingPath));
   if(pending!=null&&pending.ProcessId==pid&&pending.CreationTime==creation){
    try{string safety=store.Restore(pending.BackupId);Prefs.Text(Path.Combine(Session.Data,"restore-result.txt"),"Restore completed. Previous saves retained in "+safety);File.Move(pendingPath,pendingPath+".done",true);Index(store);Session.Log("Requested post-exit restore verified.");}
    catch(Exception e){Prefs.Text(Path.Combine(Session.Data,"restore-result.txt"),"Restore failed: "+e.Message);File.Move(pendingPath,pendingPath+".failed",true);Session.Log("Post-exit restore failed: "+e.Message);}
   }
  }Session.Log("Session backup helper exited.");
 }
 [STAThread] static int Main(string[] args){try{
  if(args.Length==5&&args[0]=="--serve-module"){Serve(int.Parse(args[1]),long.Parse(args[2]),Session.Canonical(args[3]),args[4]);return 0;}
  if(args.Length==4&&(args[0]=="--check-install"||args[0]=="--check-remove")){string message="";try{InstallerPolicy.Check(args[1],args[0]=="--check-install"?args[3]:null,args[0]=="--check-remove");}catch(Exception e){message=e.Message;}File.WriteAllText(args[2],message);return message.Length==0?0:1;}
  if(args.Length!=4||(args[0]!="--prepare"&&args[0]!="--serve"))return 2;
  int pid=int.Parse(args[1]);long creation=long.Parse(args[2]);string game=Session.Canonical(args[3]);
  if(args[0]=="--prepare")return Session.Prepare(pid,creation,game);Serve(pid,creation,game);return 0;
 }catch(Exception e){Session.Log(e.ToString());return 1;}}
}
