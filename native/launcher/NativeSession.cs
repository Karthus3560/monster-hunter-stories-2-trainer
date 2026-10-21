using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using KinshipNative;
namespace KinshipLauncher;
internal sealed class NativeSession : IDisposable
{
 readonly Process game;nint handle;readonly PackageManifest manifest;
 public long Creation {get;} public long Image {get;} public int Pid=>game.Id;
 public NativeSession(Process process,PackageManifest package){game=process;manifest=package;Creation=process.StartTime.ToFileTimeUtc();Image=process.MainModule!.BaseAddress;handle=OpenProcess(0x410,false,process.Id);if(handle==0)throw new IOException("Windows could not access the game. Run the game and launcher with the same Windows permissions.");}
 [DllImport("kernel32",SetLastError=true)] static extern nint OpenProcess(uint access,bool inherit,int pid);
 [DllImport("kernel32")] static extern bool CloseHandle(nint handle);
 [DllImport("kernel32",SetLastError=true)] static extern bool ReadProcessMemory(nint h,nint a,byte[] data,nuint n,out nuint got);
 [DllImport("kernel32",SetLastError=true)] static extern bool WriteProcessMemory(nint h,nint a,byte[] data,nuint n,out nuint written);
 [DllImport("kernel32",SetLastError=true)] static extern nint VirtualAllocEx(nint h,nint address,nuint size,uint type,uint protect);
 [DllImport("kernel32")] static extern bool VirtualFreeEx(nint h,nint address,nuint size,uint type);
 [DllImport("kernel32",SetLastError=true)] static extern nint CreateRemoteThread(nint h,nint attributes,nuint stack,nint entry,nint arg,uint flags,nint id);
 [DllImport("kernel32")] static extern uint WaitForSingleObject(nint h,uint ms);
 [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern nint GetModuleHandleW(string name);
 [DllImport("kernel32",CharSet=CharSet.Ansi)] static extern nint GetProcAddress(nint module,string name);
 [DllImport("kernel32")] static extern bool GetModuleHandleExW(uint flags,nint address,out nint module);
 [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern uint GetModuleFileNameW(nint module,StringBuilder file,int size);
 byte[] Read(long a,int n){var data=new byte[n];if(!ReadProcessMemory(handle,(nint)a,data,(nuint)n,out var got)||got!=(nuint)n)throw new IOException("The game changed while it was being checked. Close and reopen the launcher.");return data;}
 nint Allocate(byte[] data){nint a=VirtualAllocEx(handle,0,(nuint)data.Length,0x3000,4);if(a==0)throw new IOException("Windows could not prepare the Studio loader.");if(!WriteProcessMemory(handle,a,data,(nuint)data.Length,out var written)||written!=(nuint)data.Length||!Read(a,data.Length).SequenceEqual(data)){VirtualFreeEx(handle,a,0,0x8000);throw new IOException("The Studio loader request failed verification.");}return a;}
 bool Call(nint entry,nint arg){nint thread=CreateRemoteThread(handle,0,0,entry,arg,0,0);if(thread==0)throw new IOException("Windows could not start the Studio loader.");try{return WaitForSingleObject(thread,10000)==0;}finally{CloseHandle(thread);}}
 ProcessModule[] Modules(){game.Refresh();return game.Modules.Cast<ProcessModule>().ToArray();}
 void Identity(){if(game.HasExited||game.StartTime.ToFileTimeUtc()!=Creation)throw new IOException("The game closed before Studio finished loading.");}
 public void VerifyOriginal(string folder){Identity();foreach(var proof in manifest.Proofs)if(!Read(Image+proof.Offset,proof.Bytes.Length/2).SequenceEqual(Convert.FromHexString(proof.Bytes)))throw new IOException("This game version does not match the supported Studio version.");foreach(var slot in manifest.BattleSlots.Concat(manifest.IconSlots))if(BitConverter.ToInt64(Read(Image+slot.Table+slot.Index*8,8))!=Image+slot.Expected)throw new IOException("Another mod is using a menu callback required by Studio.");
  var guard=Guard(folder);if(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Read(Convert.ToInt64(guard[3],16),manifest.TextSize)))!=manifest.TextHash)throw new IOException("The original game menu could not be verified.");
 }
 string[] Guard(string folder){var words=File.ReadAllText(Path.Combine(folder,"session.guard")).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);if(words.Length!=4||words[0]!="KSNSESSION1"||words[1]!=Pid.ToString()||words[2]!=Creation.ToString())throw new IOException("The prepared Studio session does not belong to this game.");return words;}
 public string? Adopt(){var modules=Modules();var battle=modules.Where(m=>m.ModuleName=="KinshipNativeBattleV2.dll").ToArray();var icons=modules.Where(m=>m.ModuleName=="KinshipCampIcon.dll").ToArray();if(battle.Length==0&&icons.Length==0)return null;if(battle.Length!=1||icons.Length!=1)throw new IOException("An incomplete Studio session is already loaded. Restart the game before trying again.");
  foreach(var module in battle.Concat(icons)){string name=module.ModuleName!;if(Session.Hash(module.FileName)!=manifest.Files[name])throw new IOException("A different Studio version is already loaded. Restart the game before using this version.");Guard(Path.GetDirectoryName(module.FileName)!);if(!Report(Path.GetDirectoryName(module.FileName)!,name=="KinshipCampIcon.dll"?"icon-state.json":"battle-state.json"))throw new IOException("A previous Studio session is not active. Restart the game; it will not be loaded twice.");}
  foreach(var slot in manifest.BattleSlots.Concat(manifest.IconSlots)){var owner=manifest.IconSlots.Contains(slot)?icons[0]:battle[0];long pointer=BitConverter.ToInt64(Read(Image+slot.Table+slot.Index*8,8));if(pointer<owner.BaseAddress.ToInt64()||pointer>=owner.BaseAddress.ToInt64()+owner.ModuleMemorySize)throw new IOException("The active Studio callbacks could not be verified.");}
  return Path.GetDirectoryName(battle[0].FileName);
 }
 public static bool Report(string folder,string name){try{using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,name)));var r=doc.RootElement;return r.GetProperty("installed").GetBoolean()&&(!r.TryGetProperty("failed",out var failed)||!failed.GetBoolean())&&(!r.TryGetProperty("compatible",out var compatible)||compatible.GetBoolean());}catch(IOException){return false;}catch(JsonException){return false;}}
 public void Load(string folder,Action<string> progress){VerifyOriginal(folder);nint writeHandle=OpenProcess(0x43a,false,Pid);if(writeHandle==0)throw new IOException("Windows could not prepare Studio loading. Run the game and launcher with the same Windows permissions.");CloseHandle(handle);handle=writeHandle;var marker=Path.Combine(folder,"load-attempt.json");if(File.Exists(marker))throw new IOException("Studio was already attempted in this game session. Restart the game to try again.");foreach(var (name,hash) in manifest.Files)if(Session.Hash(Path.Combine(folder,name))!=hash)throw new IOException("A prepared Studio file failed verification.");Prefs.Text(marker,JsonSerializer.Serialize(new{Pid,Creation,Time=DateTimeOffset.UtcNow}));
  string bridge=Path.Combine(folder,"KinshipLoadBridge.dll");nint local=NativeLibrary.Load(bridge);nint export=NativeLibrary.GetExport(local,"LoadRequestedModule");long remote;
  var existing=Modules().Where(m=>m.ModuleName=="KinshipLoadBridge.dll").ToArray();
  if(existing.Length>1)throw new IOException("More than one loader bridge is present.");
  if(existing.Length==1){if(Session.Hash(existing[0].FileName)!=manifest.Files["KinshipLoadBridge.dll"])throw new IOException("An unfamiliar loader bridge is already present.");remote=existing[0].BaseAddress;}
  else{
   nint function=GetProcAddress(GetModuleHandleW("kernel32.dll"),"LoadLibraryW");if(function==0||!GetModuleHandleExW(6,function,out var owner))throw new IOException("Windows loader unavailable.");var file=new StringBuilder(32768);GetModuleFileNameW(owner,file,file.Capacity);var system=Modules().Single(m=>string.Equals(m.ModuleName,Path.GetFileName(file.ToString()),StringComparison.OrdinalIgnoreCase));
   nint argument=Allocate(Encoding.Unicode.GetBytes(bridge+'\0'));bool completed=false;try{completed=Call(system.BaseAddress+(function-owner),argument);if(!completed)throw new IOException("The loader is taking too long. Restart the game before trying again.");}finally{if(completed)VirtualFreeEx(handle,argument,0,0x8000);}
   var loaded=Modules().Where(m=>MatchesModule(m.ModuleName,m.FileName,"KinshipLoadBridge.dll",bridge)).ToArray();if(loaded.Length!=1)throw new IOException("Windows could not load the Studio bridge. No game mods were loaded.");remote=loaded[0].BaseAddress;
  }
  var outcomes=new List<object>();foreach(var pair in new[]{("KinshipNativeBattleV2.dll","battle-state.json"),("KinshipCampIcon.dll","icon-state.json")}){
   Identity();progress(pair.Item1=="KinshipCampIcon.dll"?"Loading the custom Camp icon…":"Loading your in-game Studio…");string path=Path.Combine(folder,pair.Item1);byte[] packet=Packet(path);nint address=Allocate(packet);bool completed=false;byte[] result;
   try{completed=Call((nint)(remote+export-local),address);if(!completed)throw new IOException("Studio loading did not finish. Restart the game before trying again.");result=Read(address,2072);}finally{if(completed)VirtualFreeEx(handle,address,0,0x8000);}
   long module=BitConverter.ToInt64(result,2056);int error=BitConverter.ToInt32(result,2064),done=BitConverter.ToInt32(result,2068);outcomes.Add(new{Name=pair.Item1,Module=module,Error=error,Completed=done});Prefs.Text(Path.Combine(folder,"load-result.json"),JsonSerializer.Serialize(outcomes));if(done!=1||module==0)throw new IOException($"Windows could not load Studio (code {error}). Your game files are unchanged. Restart the game before trying again.");
   if(!Modules().Any(m=>m.BaseAddress.ToInt64()==module&&string.Equals(Session.Canonical(m.FileName),Session.Canonical(path),StringComparison.OrdinalIgnoreCase)))throw new IOException("The loaded Studio file could not be verified.");
   bool ready=false;for(int i=0;i<80;i++){if(Report(folder,pair.Item2)){ready=true;break;}Thread.Sleep(100);}if(!ready)throw new IOException("Studio could not verify its in-game menu. Restart the game before trying again.");
  }
 }
 internal static bool MatchesModule(string? name,string file,string expectedName,string expectedPath)=>string.Equals(name,expectedName,StringComparison.OrdinalIgnoreCase)&&string.Equals(Session.Canonical(file),Session.Canonical(expectedPath),StringComparison.OrdinalIgnoreCase);
 internal static byte[] Packet(string path){if(path.Length>=1024||path.IndexOf('\0')>=0)throw new IOException("The Studio path is too long.");var data=new byte[2072];BitConverter.GetBytes(2072).CopyTo(data,0);BitConverter.GetBytes(0x4b534c31).CopyTo(data,4);Encoding.Unicode.GetBytes(path+'\0').CopyTo(data,8);return data;}
 public void Dispose()=>CloseHandle(handle);
}
