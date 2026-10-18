using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace RidersToolkit;

public static class Storage
{
    internal static string? TestRoot;
    internal static string? TestSaves;
    public static string Root => TestRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RidersToolkit");
    public static string BackupRoot => Path.Combine(Root,"Backups");
    public static string ProfileRoot => Path.Combine(Root,"Profiles");
    public static string? GameDirectory { get; private set; }
    public static SteamGame? InstalledGame { get; private set; }
    public const string KnownHash="AC73BBA1019F09DE83CA8466C4856B3937E9D63972F5FBCA2BCBC670437B7DA9";
    static string? _saveDirectory;
    public static string? SaveDirectory => TestSaves ?? _saveDirectory;
    static readonly string[] AllowedFiles=["mhr_slot_1.sav","mhr_slot_2.sav","mhr_slot_3.sav","mhr_slot_auto.sav","mhr_sys.sav"];
    public static void Discover()
    {
        Directory.CreateDirectory(Root); Directory.CreateDirectory(BackupRoot); Directory.CreateDirectory(ProfileRoot);
        InstalledGame=SteamInstallation.Find();
        GameDirectory=InstalledGame?.GameDirectory;
        _saveDirectory=null;
        var steam=Path.Combine(InstalledGame?.SteamRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"),"userdata");
        if(!Directory.Exists(steam)) return;
        var saves=Directory.GetDirectories(steam).Select(p=>Path.Combine(p,"1277400","remote")).Where(p=>Directory.Exists(p)&&AllowedFiles.Any(f=>File.Exists(Path.Combine(p,f)))).ToList();
        if(saves.Count==1) _saveDirectory=saves[0];
        // Do not silently choose among different Steam users.
    }
    public static (string Version,string Hash) InspectGame()
    {
        var exe=Path.Combine(GameDirectory ?? throw new InvalidOperationException("The game install was not found."),"game.exe");
        return(FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "Unknown",Hash(exe));
    }
    public static string Hash(string path) { using var stream=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static BackupInfo Backup()
    {
        var source=SaveDirectory ?? throw new InvalidOperationException("A single Steam save folder could not be identified.");
        var files=AllowedFiles.Where(f=>File.Exists(Path.Combine(source,f))).ToList();
        if(files.Count==0) throw new InvalidOperationException("There are no saves to back up.");
        var directory=Path.Combine(BackupRoot,DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")+"_"+Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(directory);
        var hashes=new Dictionary<string,string>();
        var before=files.ToDictionary(f=>f,f=>Hash(Path.Combine(source,f)));
        long total=0;
        foreach(var file in files)
        {
            var original=Path.Combine(source,file); var target=Path.Combine(directory,file);
            File.Copy(original,target,false);
            var hash=Hash(target);
            if(hash!=before[file]) throw new IOException("The game changed a save during backup. Wait until it finishes saving and try again.");
            hashes[file]=hash; total+=new FileInfo(target).Length;
        }
        if(files.Any(f=>Hash(Path.Combine(source,f))!=before[f])) throw new IOException("The game saved while the backup was being made. Wait until saving finishes and try again.");
        File.WriteAllText(Path.Combine(directory,"manifest.json"),JsonSerializer.Serialize(new BackupManifest(source,DateTimeOffset.Now,hashes),new JsonSerializerOptions{WriteIndented=true}));
        return new(Path.GetFileName(directory),directory,DateTime.Now.ToString("dd MMM yyyy, HH:mm"),$"{total/1048576d:N1} MB");
    }
    public static List<BackupInfo> Backups() => Directory.GetDirectories(BackupRoot).Where(d=>File.Exists(Path.Combine(d,"manifest.json"))).OrderDescending().Select(d=>new BackupInfo(Path.GetFileName(d),d,Directory.GetCreationTime(d).ToString("dd MMM yyyy, HH:mm"),$"{Directory.GetFiles(d,"*.sav").Sum(f=>new FileInfo(f).Length)/1048576d:N1} MB")).ToList();
    public static void Restore(BackupInfo backup)
    {
        if(GameRunning()) throw new InvalidOperationException("Close Monster Hunter Stories 2 before restoring a backup.");
        var root=Path.GetFullPath(BackupRoot)+Path.DirectorySeparatorChar;
        var path=Path.GetFullPath(backup.Path);
        if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a backup made by this trainer.");
        var manifest=JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(Path.Combine(path,"manifest.json"))) ?? throw new InvalidDataException("The backup manifest is unreadable.");
        if(!string.Equals(manifest.OriginalDirectory,SaveDirectory,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("This backup belongs to a different Steam save folder.");
        foreach(var pair in manifest.Hashes)
        {
            if(!AllowedFiles.Contains(pair.Key)||Hash(Path.Combine(path,pair.Key))!=pair.Value) throw new InvalidDataException("The backup failed integrity checking. It was not restored.");
        }
        if(manifest.Hashes.Count==0) throw new InvalidDataException("The backup contains no saves.");
        // Preserve current state before replacing anything, including the slot activation file.
        var previous=Backup();
        foreach(var file in manifest.Hashes.Keys)
        {
            var target=Path.Combine(SaveDirectory!,file); var staged=target+".riders-restore";
            File.Copy(Path.Combine(path,file),staged,true);
            if(Hash(staged)!=manifest.Hashes[file]) throw new IOException("The staged save did not verify.");
        }
        if(GameRunning()) throw new InvalidOperationException("The game started during restore preparation. Close it and retry.");
        var replaced=new List<string>();
        try
        {
            foreach(var file in manifest.Hashes.Keys)
            {
                var target=Path.Combine(SaveDirectory!,file); File.Move(target+".riders-restore",target,true); replaced.Add(file);
            }
        }
        catch(Exception original)
        {
            try { foreach(var file in replaced) File.Copy(Path.Combine(previous.Path,file),Path.Combine(SaveDirectory!,file),true); }
            catch(Exception rollback) { throw new IOException($"Restore failed and automatic recovery also failed. Your original saves are preserved at {previous.Path}. {rollback.Message}",original); }
            throw new IOException("Restore failed. Already replaced saves were recovered from the automatic backup.",original);
        }
    }
    public static bool GameRunning()
    {
        foreach(var process in Process.GetProcessesByName("game"))
        {
            using(process) { try { if(string.Equals(process.MainModule?.FileName,Path.Combine(GameDirectory ?? "","game.exe"),StringComparison.OrdinalIgnoreCase)) return true; } catch { return true; } }
        }
        return false;
    }
    public static void SaveProfile(Profile profile)
    {
        var name=profile.Name.Trim();
        if(name.Length is <1 or >48 || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || name.Contains("..")) throw new ArgumentException("Use a profile name of 1–48 characters without file-path symbols.");
        var path=Path.Combine(ProfileRoot,name+".json");
        var temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(profile with {Name=name},new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temp,path,true);
    }
    public static List<string> Profiles() => Directory.GetFiles(ProfileRoot,"*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToList();
    public static Profile LoadProfile(string name)
    {
        if(!Profiles().Contains(name)) throw new ArgumentException("Choose an existing profile.");
        return JsonSerializer.Deserialize<Profile>(File.ReadAllText(Path.Combine(ProfileRoot,name+".json"))) ?? throw new InvalidDataException("The profile could not be read.");
    }
    sealed record BackupManifest(string OriginalDirectory,DateTimeOffset Created,Dictionary<string,string> Hashes);
}
