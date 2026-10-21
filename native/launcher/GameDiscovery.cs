using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using KinshipNative;
namespace KinshipLauncher;
internal record LauncherSettings(string GameFolder);
internal static class GameDiscovery
{
 public static string SettingsFile=>Path.Combine(Session.Data,"launcher.json");
 public static string? Saved(){try{return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(SettingsFile))?.GameFolder;}catch{return null;}}
 public static void Save(string game)=>Prefs.Text(SettingsFile,JsonSerializer.Serialize(new LauncherSettings(Session.Canonical(game))));
 public static IEnumerable<string> Candidates(){
  var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);void Add(string? p){if(!string.IsNullOrWhiteSpace(p))try{result.Add(Path.GetFullPath(p));}catch(ArgumentException){}}
  Add(Saved());try{Add(JsonSerializer.Deserialize<HostConfig>(File.ReadAllText(Path.Combine(Session.Root,"host.json")))?.GameDirectory);}catch{}
  foreach(var p in Process.GetProcessesByName("game"))using(p)try{Add(Path.GetDirectoryName(p.MainModule?.FileName));}catch{}
  var steam=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var p in new[]{(string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath",null),(string?)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam","InstallPath",null),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam")})if(p!=null)steam.Add(p);
  foreach(var s in steam){var libraries=new HashSet<string>(StringComparer.OrdinalIgnoreCase){s};string vdf=Path.Combine(s,"steamapps","libraryfolders.vdf");
   if(File.Exists(vdf))try{foreach(var p in SteamPaths(File.ReadAllText(vdf)))libraries.Add(p);}catch(IOException){}
   foreach(var library in libraries){string manifest=Path.Combine(library,"steamapps","appmanifest_1277400.acf");if(File.Exists(manifest))try{string? folder=Key(File.ReadAllText(manifest),"installdir");if(folder!=null&&Path.GetFileName(folder)==folder)Add(Path.Combine(library,"steamapps","common",folder));}catch(IOException){}Add(Path.Combine(library,"steamapps","common","Monster Hunter Stories 2"));}
  }return result;
 }
 public static IEnumerable<string> SteamPaths(string text)=>Pairs(text).Where(x=>x.Key.Equals("path",StringComparison.OrdinalIgnoreCase)).Select(x=>x.Value);
 public static string? Key(string text,string key)=>Pairs(text).FirstOrDefault(x=>x.Key.Equals(key,StringComparison.OrdinalIgnoreCase)).Value;
 static IEnumerable<KeyValuePair<string,string>> Pairs(string text){var tokens=new List<string>();for(int i=0;i<text.Length;i++)if(text[i]=='"'){var s=new System.Text.StringBuilder();for(i++;i<text.Length;i++){if(text[i]=='"')break;if(text[i]=='\\'&&i+1<text.Length&&(text[i+1]=='\\'||text[i+1]=='"'))s.Append(text[++i]);else s.Append(text[i]);}tokens.Add(s.ToString());}for(int i=0;i+1<tokens.Count;i++)yield return new(tokens[i],tokens[i+1]);}
 public static string? Detect(){foreach(var folder in Candidates())try{if(File.Exists(Path.Combine(folder,"game.exe"))){Session.ValidateGame(folder,false);return Session.Canonical(folder);}}catch(IOException){}return null;}
 public static Process? Running(string game){foreach(var p in Process.GetProcessesByName("game")){try{if(string.Equals(Session.Canonical(p.MainModule!.FileName),Path.Combine(Session.Canonical(game),"game.exe"),StringComparison.OrdinalIgnoreCase))return p;}catch{}p.Dispose();}return null;}
 public static void StartSteam()=>Process.Start(new ProcessStartInfo("steam://rungameid/1277400"){UseShellExecute=true});
}
