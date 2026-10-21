using System.IO;
using System.Text;
using System.Text.Json;
using KinshipNative;
using KinshipLauncher;
int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);checks++;}
void Reject(Action action,string text){try{action();}catch(IOException){Check(true,text);return;}throw new Exception("Not refused: "+text);}
var folder=Path.Combine(Path.GetTempPath(),"KinshipLauncherChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
string libraries="\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\Program Files (x86)\\\\Steam\" } \"1\" { \"path\" \"E:\\\\My Games\\\\SteamLibrary\" } }";
Check(GameDiscovery.SteamPaths(libraries).SequenceEqual(new[]{@"C:\Program Files (x86)\Steam",@"E:\My Games\SteamLibrary"}),"Steam detection reads multiple libraries and escaped Windows paths");
Check(GameDiscovery.Key("\"AppState\" { \"installdir\" \"Stories 2 — test\" }","installdir")=="Stories 2 — test","Steam app folder names preserve Unicode");
Check(GameDiscovery.Key("\"AppState\" {}","installdir")==null,"a missing install folder is not invented");
string path=@"C:\Users\Test User\Kinship Studio Runtime\sessions\123-456\KinshipNativeBattleV2.dll";var packet=NativeSession.Packet(path);
Check(packet.Length==2072&&BitConverter.ToInt32(packet,0)==2072&&BitConverter.ToInt32(packet,4)==0x4b534c31,"loader packet matches the proven native bridge envelope");
Check(Encoding.Unicode.GetString(packet,8,path.Length*2)==path&&BitConverter.ToInt64(packet,2056)==0&&BitConverter.ToInt32(packet,2068)==0,"loader packet preserves paths and starts with no reported result");
Reject(()=>NativeSession.Packet(new string('x',1024)),"overlong native loader paths are refused");Reject(()=>NativeSession.Packet("a\0b"),"embedded nulls cannot redirect a native loader path");
Check(!NativeSession.MatchesModule("unrelated.dll",@"C:\missing-module.dll","KinshipLoadBridge.dll",@"C:\missing-bridge.dll"),"unrelated Windows modules never enter Studio path resolution");
string incoming=Path.Combine(folder,"payload.json"),target=Path.Combine(folder,"app");var bytes=Encoding.UTF8.GetBytes("owned launcher fixture");string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
var manifest=new LauncherInstallManifest(LauncherInstallPolicy.Id,"1.1.0",new(){{"KinshipStudioLauncher.exe",hash},{"modules\\KinshipLoadBridge.dll",hash}});File.WriteAllText(incoming,JsonSerializer.Serialize(manifest));
LauncherInstallPolicy.Check(target,incoming,false);Check(true,"a new launcher can install without modifying a game folder");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"unrelated.txt"),"keep");Reject(()=>LauncherInstallPolicy.Check(target,incoming,false),"an unrelated existing installation folder is protected");
File.WriteAllText(Path.Combine(target,"launcher-install.json"),JsonSerializer.Serialize(manifest));File.WriteAllBytes(Path.Combine(target,"KinshipStudioLauncher.exe"),bytes);Directory.CreateDirectory(Path.Combine(target,"modules"));File.WriteAllBytes(Path.Combine(target,"modules/KinshipLoadBridge.dll"),bytes);
LauncherInstallPolicy.Check(target,incoming,false);LauncherInstallPolicy.Check(target,null,true);Check(File.Exists(Path.Combine(target,"unrelated.txt")),"owned updates and removal checks preserve unrelated files");
File.WriteAllText(Path.Combine(target,"KinshipStudioLauncher.exe"),"someone else's file");Reject(()=>LauncherInstallPolicy.Check(target,null,true),"uninstall preserves an externally replaced executable");File.WriteAllBytes(Path.Combine(target,"KinshipStudioLauncher.exe"),bytes);
foreach(string bad in new[]{"..\\outside.dll","game.exe","dinput8.dll",@"C:\outside.dll"}){File.WriteAllText(incoming,JsonSerializer.Serialize(manifest with{Files=new(){{bad,hash}}}));Reject(()=>LauncherInstallPolicy.Check(target,incoming,false),"package refuses unowned path: "+bad);}
File.WriteAllText(Path.Combine(folder,"battle-state.json"),"{\"installed\":true,\"compatible\":true}");Check(NativeSession.Report(folder,"battle-state.json"),"successful native reports are recognized");
File.WriteAllText(Path.Combine(folder,"battle-state.json"),"{\"installed\":true,\"compatible\":false}");Check(!NativeSession.Report(folder,"battle-state.json"),"an incompatible native report cannot appear ready");
File.WriteAllText(Path.Combine(folder,"battle-state.json"),"{\"installed\":");Check(!NativeSession.Report(folder,"battle-state.json"),"a partially written report is never mistaken for success");
Check(!LaunchCoordinator.RuntimeRoot.Contains("AppData",StringComparison.OrdinalIgnoreCase),"native modules stage outside the rejected AppData path");
Console.WriteLine($"{checks} launcher checks passed. Fixture retained: {folder}");
