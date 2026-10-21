using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace KinshipNative;
internal record Proof(long Offset,string Bytes);
internal record Slot(long Table,int Index,long Expected);
internal record PackageManifest(Dictionary<string,string> Files,List<Proof> Proofs,List<Slot> BattleSlots,List<Slot> IconSlots,string TextHash,int TextSize,string TextNeedle);
internal static class Session
{
 public const string GameHash="AC73BBA1019F09DE83CA8466C4856B3937E9D63972F5FBCA2BCBC670437B7DA9";
 public static string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KinshipStudioNative");
 public static string Data=>Path.Combine(Root,"data");
 public static string Package=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","modules"));
 public static string Hash(string path){using var f=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(f));}
 public static void Ordinary(string path){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked paths are not supported.");}
 public static void Log(string text){Directory.CreateDirectory(Data);File.AppendAllText(Path.Combine(Data,"service.log"),DateTimeOffset.Now.ToString("O")+" "+text+Environment.NewLine);}
 [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] static extern nint CreateFileW(string path,uint access,uint share,nint security,uint disposition,uint flags,nint template);
 [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetFinalPathNameByHandleW(nint file,StringBuilder path,uint size,uint flags);
 public static string Canonical(string path){
  nint file=CreateFileW(path,0,7,0,3,0x02000000,0);if(file==new nint(-1))throw new IOException("Cannot resolve the selected path.");
  try{var text=new StringBuilder(32768);uint count=GetFinalPathNameByHandleW(file,text,(uint)text.Capacity,0);if(count==0||count>=text.Capacity)throw new IOException("Cannot resolve the selected path.");string value=text.ToString();return value.StartsWith(@"\\?\UNC\")?@"\\"+value[8..]:value.StartsWith(@"\\?\")?value[4..]:value;}finally{CloseHandle(file);}
 }
 public static bool Running(string game){string target=Path.Combine(Canonical(game),"game.exe");return Process.GetProcessesByName("game").Any(p=>{using(p){try{return string.Equals(Canonical(p.MainModule?.FileName??throw new IOException("Process image unavailable.")),target,StringComparison.OrdinalIgnoreCase);}catch{return true;}}});}
 public static void ValidateGame(string game,bool requireClosed){Ordinary(game);var exe=Path.Combine(game,"game.exe");Ordinary(exe);if(Hash(exe)!=GameHash)throw new IOException("Choose the supported Monster Hunter Stories 2 game folder. This executable version is not supported.");if(requireClosed&&Running(game))throw new IOException("Close Monster Hunter Stories 2 before changing the installation.");}
 public static string Folder(int pid,long creation)=>Path.Combine(Root,"sessions",$"{pid}-{creation}");
 [StructLayout(LayoutKind.Sequential)] struct Mbi{public nint BaseAddress,AllocationBase;public uint AllocationProtect;public ushort PartitionId;public nuint RegionSize;public uint State,Protect,Type;}
 [DllImport("kernel32",SetLastError=true)] static extern nint OpenProcess(uint access,bool inherit,int pid);
 [DllImport("kernel32")] static extern bool ReadProcessMemory(nint h,nint address,byte[] bytes,nuint size,out nuint read);
 [DllImport("kernel32")] static extern nuint VirtualQueryEx(nint h,nint address,out Mbi info,nuint size);
 [DllImport("kernel32")] static extern bool CloseHandle(nint h);
 static byte[] Read(nint h,long address,int count){var bytes=new byte[count];if(!ReadProcessMemory(h,(nint)address,bytes,(nuint)count,out var got)||got!=(nuint)count)throw new IOException("A required game memory read failed.");return bytes;}
 static long Text(nint h,PackageManifest m){var needle=Convert.FromHexString(m.TextNeedle);var found=new HashSet<long>();
  for(long address=65536;address<0x7fffffff0000;){
   if(VirtualQueryEx(h,(nint)address,out var info,(nuint)Marshal.SizeOf<Mbi>())==0)break;
   long start=info.BaseAddress,length=(long)info.RegionSize;if(length<=0||start+length<=address)break;
   if(info.State==0x1000&&info.Type==0x20000&&(info.Protect&0x100)==0&&((info.Protect&0xff)==4||(info.Protect&0xff)==8))for(long off=0;off<length;off+=1048576){
    int size=(int)Math.Min(1048576+needle.Length,length-off);var bytes=new byte[size];if(!ReadProcessMemory(h,(nint)(start+off),bytes,(nuint)size,out var got))continue;
    for(int cursor=0;cursor<(int)got;){int index=bytes.AsSpan(cursor,(int)got-cursor).IndexOf(needle);if(index<0)break;long at=start+off+cursor+index;
     try{if(Convert.ToHexString(SHA256.HashData(Read(h,at,m.TextSize)))==m.TextHash)found.Add(at);}catch(IOException){}cursor+=index+1;
    }
   }address=start+length;
  }if(found.Count>1)throw new IOException("Ambiguous original menu text; no modules loaded.");return found.Count==1?found.Single():0;
 }
 public static PackageManifest Manifest(){var m=JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(Path.Combine(Package,"manifest.json")))!;foreach(var (name,hash) in m.Files){if(Path.GetFileName(name)!=name||Hash(Path.Combine(Package,name))!=hash)throw new IOException("Installed module verification failed. Repair Kinship Studio with its installer.");}return m;}
 public static int Prepare(int pid,long creation,string game,string? stagingFolder=null,Action<string>? status=null,CancellationToken cancellation=default){
  game=Canonical(game);ValidateGame(game,false);using var process=Process.GetProcessById(pid);
  var main=process.MainModule;
  if(main==null||process.StartTime.ToFileTimeUtc()!=creation||!string.Equals(main.FileName,Path.Combine(game,"game.exe"),StringComparison.OrdinalIgnoreCase)||main.ModuleMemorySize!=86138880)throw new IOException("Game session identity changed.");
  if(Process.GetProcessesByName("RidersToolkit").Any())throw new IOException("Close the desktop trainer before using the installed Studio.");
  if(process.Modules.Cast<ProcessModule>().Any(m=>m.ModuleName is "KinshipNativeBattleV2.dll" or "KinshipCampIcon.dll"))throw new IOException("A Studio module is already loaded. No duplicate was loaded.");
  var m=Manifest();var folder=stagingFolder??Folder(pid,creation);Directory.CreateDirectory(folder);Ordinary(folder);
  if(File.Exists(Path.Combine(folder,"package-attempt.json")))throw new IOException("This game session already had an automatic load attempt.");
  nint h=OpenProcess(0x410,false,pid);if(h==0)throw new IOException("Cannot inspect the game session.");long text=0;
  try{
   status?.Invoke("Waiting for the game's menus to become available…");
   for(int tries=0;tries<45;tries++){cancellation.ThrowIfCancellationRequested();if(process.HasExited)return 2;text=Text(h,m);if(text!=0)break;Thread.Sleep(3000);}
   if(text==0)throw new IOException("Original menu text did not become available. Restart the game to retry.");
   long image=process.MainModule!.BaseAddress;
   foreach(var proof in m.Proofs){var expected=Convert.FromHexString(proof.Bytes);if(!Read(h,image+proof.Offset,expected.Length).SequenceEqual(expected))throw new IOException("Unsupported game routines; no modules loaded.");}
   foreach(var slot in m.BattleSlots.Concat(m.IconSlots))if(BitConverter.ToInt64(Read(h,image+slot.Table+slot.Index*8,8))!=image+slot.Expected)throw new IOException("Another mod owns a required menu callback.");
  }finally{CloseHandle(h);}
  status?.Invoke("Creating a verified save backup…");
  var saves=Service.ResolveSaves(game);if(saves!=null){var store=new BackupStore(Path.Combine(Data,"backups"),saves,()=>Running(game));store.Create();Service.Index(store);}
  foreach(var name in m.Files.Keys){File.Copy(Path.Combine(Package,name),Path.Combine(folder,name),false);if(Hash(Path.Combine(folder,name))!=m.Files[name])throw new IOException("Session module copy failed verification.");}
  Prefs.Text(Path.Combine(folder,"session.guard"),$"KSNSESSION1 {pid} {creation} {text:x}");
  bool resume=!File.Exists(Path.Combine(Data,"autoload.enabled"))||File.ReadAllText(Path.Combine(Data,"autoload.enabled")).Trim()=="1";
  if(resume)foreach(var file in new[]{"last.bin","last.bin.previous"})try{var values=Prefs.Decode(File.ReadAllBytes(Path.Combine(Data,file)));Prefs.Text(Path.Combine(folder,"resume-settings.txt"),pid+" "+string.Join(' ',values));break;}catch(IOException){}catch(InvalidDataException){}
  Prefs.Text(Path.Combine(folder,"package-attempt.json"),JsonSerializer.Serialize(new{pid,creation,Time=DateTimeOffset.UtcNow}));Log("Verified game session prepared "+pid);return 0;
 }
}
