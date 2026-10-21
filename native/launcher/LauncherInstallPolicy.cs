using System.Diagnostics;
using System.Text.Json;
using KinshipNative;
namespace KinshipLauncher;
internal record LauncherInstallManifest(string Id,string Version,Dictionary<string,string> Files);
internal static class LauncherInstallPolicy
{
 public const string Id="KinshipStudio.NativeLauncher.E51718EA-3450-48D8-9C59-E51AE70E4D4B";
 static string Target(string root,string relative){if(Path.IsPathRooted(relative)||relative.Split('\\','/').Any(p=>p is ".." or "." or ""))throw new IOException("Invalid package path.");string file=Path.GetFullPath(Path.Combine(root,relative));if(!file.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(file).Equals("game.exe",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(file).Equals("dinput8.dll",StringComparison.OrdinalIgnoreCase))throw new IOException("Package contains an unowned file.");return file;}
 public static LauncherInstallManifest Read(string file){var m=JsonSerializer.Deserialize<LauncherInstallManifest>(File.ReadAllText(file))??throw new IOException("Package ownership record unreadable.");if(m.Id!=Id||m.Files.Count==0||m.Files.Count>25)throw new IOException("Unrecognized launcher installation.");foreach(var (name,hash) in m.Files){Target(@"C:\KinshipPackage",name);if(hash.Length!=64||hash.Any(c=>!Uri.IsHexDigit(c)))throw new IOException("Invalid package checksum.");}return m;}
 public static void Check(string root,string? incoming,bool removal){
  root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
  if(Process.GetProcessesByName("game").Any())throw new IOException("Close Stories 2 before installing, updating or removing Kinship Studio.");
  if(Process.GetProcessesByName("KinshipStudioLauncher").Any(p=>{using(p)return p.Id!=Environment.ProcessId;}))throw new IOException("Close the Kinship Studio launcher before continuing.");
  string marker=Path.Combine(root,"launcher-install.json");LauncherInstallManifest? old=null;
  if(Directory.Exists(root)){Session.Ordinary(root);if(File.Exists(marker)){Session.Ordinary(marker);old=Read(marker);}else if(Directory.EnumerateFileSystemEntries(root).Any())throw new IOException("Choose an empty folder. This folder is not owned by the Kinship Studio launcher.");}
  if(removal&&old==null)throw new IOException("No owned launcher installation was found.");
  if(old!=null)foreach(var (name,hash) in old.Files){string f=Target(root,name);Parents(root,f);if(File.Exists(f)){Session.Ordinary(f);if(Session.Hash(f)!=hash)throw new IOException("An installed file was modified outside the installer: "+name+". It has been preserved.");}}
  if(incoming!=null)foreach(var name in Read(incoming).Files.Keys){string f=Target(root,name);Parents(root,f);if((File.Exists(f)||Directory.Exists(f))&&(old==null||!old.Files.ContainsKey(name)))throw new IOException("An unrelated file conflicts with this package: "+name);}
 }
 static void Parents(string root,string file){for(string? p=Path.GetDirectoryName(file);p!=null&&!string.Equals(p,root,StringComparison.OrdinalIgnoreCase);p=Path.GetDirectoryName(p))if(Directory.Exists(p))Session.Ordinary(p);}
}
