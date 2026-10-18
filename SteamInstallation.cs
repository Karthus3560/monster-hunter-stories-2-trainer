using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace RidersToolkit;

public sealed record SteamGame(string SteamRoot,string LibraryRoot,string GameDirectory)
{
    public string Executable => Path.Combine(GameDirectory,"game.exe");
    public string SteamExecutable => Path.Combine(SteamRoot,"steam.exe");
}

public static class SteamInstallation
{
    public const string AppId="1277400";
    public static IEnumerable<string> SteamRoots()
    {
        var paths=new List<string>();
        foreach(var key in new[]{@"HKEY_CURRENT_USER\Software\Valve\Steam",@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"})
            foreach(var name in new[]{"SteamPath","InstallPath"})
                if(Registry.GetValue(key,name,null) is string path && !string.IsNullOrWhiteSpace(path)) paths.Add(path.Replace('/',Path.DirectorySeparatorChar));
        paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }
    public static SteamGame? Find(IEnumerable<string>? roots=null)
    {
        foreach(var steamRoot in roots ?? SteamRoots())
        {
            if(!Directory.Exists(steamRoot)||!File.Exists(Path.Combine(steamRoot,"steam.exe"))) continue;
            var libraries=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Path.GetFullPath(steamRoot)};
            var libraryList=Path.Combine(steamRoot,"steamapps","libraryfolders.vdf");
            try
            {
                if(File.Exists(libraryList))
                {
                    var vdf=ValveData.Parse(File.ReadAllText(libraryList));
                    var folder=vdf.Object("libraryfolders");
                    if(folder is not null)
                        foreach(var pair in folder.Values)
                        {
                            var path=pair.Value is ValveData node ? node.Text("path") : pair.Value as string;
                            if(!string.IsNullOrWhiteSpace(path)&&Path.IsPathRooted(path)) libraries.Add(Path.GetFullPath(path));
                        }
                }
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { }
            foreach(var library in libraries)
            {
                var manifest=Path.Combine(library,"steamapps","appmanifest_"+AppId+".acf");
                try
                {
                    if(!File.Exists(manifest)) continue;
                    var state=ValveData.Parse(File.ReadAllText(manifest)).Object("AppState");
                    if(state?.Text("appid")!=AppId) continue;
                    var install=state.Text("installdir");
                    if(string.IsNullOrWhiteSpace(install)||Path.IsPathRooted(install)) continue;
                    var common=Path.GetFullPath(Path.Combine(library,"steamapps","common"))+Path.DirectorySeparatorChar;
                    var directory=Path.GetFullPath(Path.Combine(common,install));
                    if(!directory.StartsWith(common,StringComparison.OrdinalIgnoreCase)||!File.Exists(Path.Combine(directory,"game.exe"))) continue;
                    return new(Path.GetFullPath(steamRoot),library,directory);
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { }
            }
        }
        return null;
    }
    public static ProcessStartInfo LaunchInfo(SteamGame game)
    {
        if(!File.Exists(game.SteamExecutable)||!File.Exists(game.Executable)) throw new FileNotFoundException("Steam or the game moved. Refresh the installation before launching.");
        var result=new ProcessStartInfo(game.SteamExecutable){UseShellExecute=false,WorkingDirectory=game.SteamRoot};
        result.ArgumentList.Add("-applaunch"); result.ArgumentList.Add(AppId);
        return result;
    }
}

public sealed class ValveData
{
    public Dictionary<string,object> Values { get; }=new(StringComparer.OrdinalIgnoreCase);
    public string? Text(string key)=>Values.GetValueOrDefault(key) as string;
    public ValveData? Object(string key)=>Values.GetValueOrDefault(key) as ValveData;
    public static ValveData Parse(string input)
    {
        int position=0;
        string? Token()
        {
            while(position<input.Length)
            {
                if(char.IsWhiteSpace(input[position])) { position++;continue; }
                if(position+1<input.Length&&input[position]=='/'&&input[position+1]=='/') { while(position<input.Length&&input[position]!='\n') position++;continue; }
                break;
            }
            if(position>=input.Length) return null;
            char ch=input[position++];
            if(ch is '{' or '}') return ch.ToString();
            if(ch!='"') throw new InvalidDataException("Unexpected Steam manifest token.");
            var text=new System.Text.StringBuilder();
            while(position<input.Length)
            {
                ch=input[position++];
                if(ch=='"') return text.ToString();
                if(ch=='\\'&&position<input.Length && input[position] is '\\' or '"') ch=input[position++];
                text.Append(ch);
            }
            throw new InvalidDataException("Unterminated Steam manifest string.");
        }
        ValveData Read(bool nested,int depth)
        {
            if(depth>24) throw new InvalidDataException("Steam manifest nesting is too deep.");
            var node=new ValveData(); string? key;
            while((key=Token()) is not null)
            {
                if(key=="}") { if(nested) return node; throw new InvalidDataException("Unmatched Steam manifest brace."); }
                if(key=="{") throw new InvalidDataException("Missing Steam manifest key.");
                var value=Token() ?? throw new InvalidDataException("Missing Steam manifest value.");
                node.Values[key]=value=="{"?Read(true,depth+1):value=="}"?throw new InvalidDataException("Missing Steam manifest value."):value;
            }
            if(nested) throw new InvalidDataException("Incomplete Steam manifest object.");
            return node;
        }
        return Read(false,0);
    }
}
