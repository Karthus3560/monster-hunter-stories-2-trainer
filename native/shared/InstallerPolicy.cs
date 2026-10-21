using System.Text.Json;
namespace KinshipNative;
internal record InstallManifest(string Id,string Version,Dictionary<string,string> Files);
internal static class InstallerPolicy
{
 public const string Id="KinshipStudio.Native.7A745B07-5CB0-4C47-AF13-B8404EF11036";
 static string Target(string game,string relative){
  if(Path.IsPathRooted(relative)||relative.Split('\\','/').Any(p=>p is ".." or "." or ""))throw new IOException("Invalid package file path.");
  if(relative!="dinput8.dll"&&!relative.StartsWith("KinshipStudio\\",StringComparison.Ordinal))throw new IOException("Package includes an unowned game file.");
  var full=Path.GetFullPath(Path.Combine(game,relative));if(!full.StartsWith(game+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Package file escapes game folder.");return full;
 }
 public static InstallManifest Read(string file){var m=JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(file))??throw new IOException("Unreadable installation ownership record.");if(m.Id!=Id||m.Files.Count==0||m.Files.Count>30)throw new IOException("Unrecognized installation ownership record.");foreach(var pair in m.Files){Target(@"C:\KinshipPolicyRoot",pair.Key);if(pair.Value.Length!=64||pair.Value.Any(c=>!Uri.IsHexDigit(c)))throw new IOException("Invalid package file checksum.");}return m;}
 public static void Check(string game,string? incoming,bool removing=false){
  Session.Ordinary(game);game=Path.TrimEndingDirectorySeparator(Session.Canonical(game));if(Session.Running(game))throw new IOException("Close Monster Hunter Stories 2 before installing, updating or removing Kinship Studio.");
  if(!removing)Session.ValidateGame(game,false);
  string folder=Path.Combine(game,"KinshipStudio"),manifest=Path.Combine(folder,"install.json");InstallManifest? previous=null;
  if(Directory.Exists(folder)){Session.Ordinary(folder);if(!File.Exists(manifest))throw new IOException("An existing KinshipStudio folder is not owned by this installer. Nothing was changed.");Session.Ordinary(manifest);previous=Read(manifest);}
  if(removing&&previous==null)throw new IOException("No owned Kinship Studio installation was found.");
  if(previous!=null)foreach(var (name,hash) in previous.Files){string file=Target(game,name);if(File.Exists(file)){CheckParents(game,file);Session.Ordinary(file);if(Session.Hash(file)!=hash)throw new IOException("An installed file was changed outside this installer: "+name+". Nothing was removed or overwritten.");}}
  if(File.Exists(Path.Combine(game,"dinput8.dll"))&&(previous==null||!previous.Files.ContainsKey("dinput8.dll")))throw new IOException("Another mod already uses dinput8.dll. Kinship Studio will not replace it.");
  if(incoming!=null){var next=Read(incoming);foreach(var name in next.Files.Keys){string target=Target(game,name);CheckParents(game,target);if((File.Exists(target)||Directory.Exists(target))&&(previous==null||!previous.Files.ContainsKey(name)))throw new IOException("An unrelated file conflicts with this package: "+name);}}
 }
 static void CheckParents(string root,string file){for(string? p=Path.GetDirectoryName(file);p!=null&&!string.Equals(p,root,StringComparison.OrdinalIgnoreCase);p=Path.GetDirectoryName(p))if(Directory.Exists(p))Session.Ordinary(p);}
}
