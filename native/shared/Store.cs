using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KinshipNative;
internal static class Prefs
{
    public static readonly uint[] Maxima=[1,1,1,1,36,1,76,1,8,20,8,8,8,8,8,1,1,1,1,8,1,3,3,3,3,1,4,5,1];
    public static byte[] Encode(uint[] values)
    {
        if(values.Length!=29||values.Where((v,i)=>v>Maxima[i]).Any())throw new InvalidDataException("Invalid native settings.");
        var bytes=new byte[160];BinaryPrimitives.WriteUInt32LittleEndian(bytes,0x314e534b);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4),1);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8),29);
        for(int i=0;i<29;i++)BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12+i*4),values[i]);
        SHA256.HashData(bytes.AsSpan(0,128)).CopyTo(bytes,128);return bytes;
    }
    public static uint[] Decode(byte[] bytes)
    {
        if(bytes.Length!=160||BinaryPrimitives.ReadUInt32LittleEndian(bytes)!=0x314e534b||BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4))!=1||BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8))!=29||!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes.AsSpan(0,128)),bytes.AsSpan(128)))throw new InvalidDataException("Settings integrity check failed.");
        var values=new uint[29];for(int i=0;i<29;i++){values[i]=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12+i*4));if(values[i]>Maxima[i])throw new InvalidDataException("Unknown setting value.");}return values;
    }
    public static void Atomic(string path,byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}File.Move(temp,path,true);
    }
    public static void Text(string path,string value)=>Atomic(path,Encoding.UTF8.GetBytes(value));
}
internal record SaveManifest(int Version,string SaveDirectory,DateTimeOffset CreatedUtc,Dictionary<string,string> Files);
internal record BackupEntry(string Id,string Date,string Summary);
internal sealed class BackupStore(string root,string saveDirectory,Func<bool> gameRunning)
{
    public static readonly string[] Allowed=["mhr_slot_1.sav","mhr_slot_2.sav","mhr_slot_3.sav","mhr_slot_auto.sav","mhr_sys.sav"];
    public string Root {get;}=Path.GetFullPath(root);
    public string Saves {get;}=Path.GetFullPath(saveDirectory);
    static readonly Regex IdPattern=new("^[0-9]{8}T[0-9]{6}-[a-f0-9]{8}$",RegexOptions.CultureInvariant);
    static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    static void Ordinary(string path){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked backup/save paths are not supported.");}
    string Location(string id){if(!IdPattern.IsMatch(id))throw new InvalidDataException("Choose a backup from the list.");var path=Path.GetFullPath(Path.Combine(Root,id));if(Path.GetDirectoryName(path)!=Root)throw new InvalidDataException("Invalid backup location.");return path;}
    public string Create()
    {
        Ordinary(Saves);Directory.CreateDirectory(Root);Ordinary(Root);
        var files=Allowed.Where(n=>File.Exists(Path.Combine(Saves,n))).ToArray();if(files.Length==0)throw new IOException("No save files were found.");
        foreach(var n in files)Ordinary(Path.Combine(Saves,n));
        var hashes=files.ToDictionary(n=>n,n=>Hash(Path.Combine(Saves,n)));
        var id=DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8];var staging=Path.Combine(Root,"incomplete-"+id);Directory.CreateDirectory(staging);
        foreach(var n in files){var target=Path.Combine(staging,n);File.Copy(Path.Combine(Saves,n),target,false);if(Hash(target)!=hashes[n])throw new IOException("The game saved during backup. Wait until saving ends and retry.");}
        if(files.Any(n=>Hash(Path.Combine(Saves,n))!=hashes[n])||!files.SequenceEqual(Allowed.Where(n=>File.Exists(Path.Combine(Saves,n)))))throw new IOException("Save files changed during backup. Retry after saving ends.");
        Prefs.Text(Path.Combine(staging,"manifest.json"),JsonSerializer.Serialize(new SaveManifest(1,Saves,DateTimeOffset.UtcNow,hashes)));
        Directory.Move(staging,Location(id));Verify(id);return id;
    }
    public SaveManifest Verify(string id)
    {
        var folder=Location(id);Ordinary(folder);var manifestPath=Path.Combine(folder,"manifest.json");Ordinary(manifestPath);
        var manifest=JsonSerializer.Deserialize<SaveManifest>(File.ReadAllText(manifestPath))??throw new InvalidDataException("Backup manifest is unreadable.");
        if(manifest.Version!=1||!string.Equals(Path.GetFullPath(manifest.SaveDirectory),Saves,StringComparison.OrdinalIgnoreCase)||manifest.Files.Count==0||manifest.Files.Count>5)throw new InvalidDataException("Backup belongs to a different save folder or format.");
        foreach(var (name,hash) in manifest.Files){if(!Allowed.Contains(name))throw new InvalidDataException("Unknown file in backup.");var file=Path.Combine(folder,name);Ordinary(file);if(Hash(file)!=hash)throw new InvalidDataException("Backup verification failed. Nothing was restored.");}
        return manifest;
    }
    public List<BackupEntry> List()
    {
        if(!Directory.Exists(Root))return [];
        return Directory.EnumerateDirectories(Root).Where(p=>IdPattern.IsMatch(Path.GetFileName(p))).OrderDescending().Take(100).Select(p=>{
            try{Ordinary(p);var m=JsonSerializer.Deserialize<SaveManifest>(File.ReadAllText(Path.Combine(p,"manifest.json")))!;var size=m.Files.Keys.Where(Allowed.Contains).Sum(n=>new FileInfo(Path.Combine(p,n)).Length);return new BackupEntry(Path.GetFileName(p),m.CreatedUtc.ToLocalTime().ToString("dd MMM HH:mm"),$"{m.Files.Count} saves, {size/1048576d:F1} MB");}
            catch{return new BackupEntry(Path.GetFileName(p),"Unreadable backup","Verify before restoring");}
        }).ToList();
    }
    public string Restore(string id)
    {
        if(gameRunning())throw new IOException("Close the game before restoring.");var manifest=Verify(id);Ordinary(Saves);
        var safety=Create();var prepared=new Dictionary<string,string>();
        foreach(var (name,hash) in manifest.Files){var target=Path.Combine(Saves,name);if(File.Exists(target))Ordinary(target);var temp=target+".kinship-"+Guid.NewGuid().ToString("N");File.Copy(Path.Combine(Location(id),name),temp);if(Hash(temp)!=hash)throw new IOException("Staged save verification failed.");prepared[name]=temp;}
        if(gameRunning())throw new IOException("The game started during restore preparation. Nothing was replaced.");
        var original=Verify(safety);var changed=new List<string>();
        try{
            foreach(var (name,temp) in prepared){if(gameRunning())throw new IOException("The game started during restore.");File.Move(temp,Path.Combine(Saves,name),true);changed.Add(name);}
            foreach(var (name,hash) in manifest.Files)if(Hash(Path.Combine(Saves,name))!=hash)throw new IOException("Restored save verification failed.");
        }catch(Exception error){
            foreach(var name in changed){var target=Path.Combine(Saves,name);if(original.Files.ContainsKey(name))File.Copy(Path.Combine(Location(safety),name),target,true);else File.Delete(target);}
            throw new IOException("Restore failed; previous saves were recovered. Safety backup: "+safety,error);
        }
        return safety;
    }
}
