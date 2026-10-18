using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RidersToolkit;

internal interface IEggStudioEditor
{
    EditorSubject[] Read(NativeCatalog catalog);
    EditReceipt Apply(EditorSubject subject,EditorChange[] changes,Func<BackupInfo> backup,string journalFolder);
    void Undo(EditReceipt receipt);
    EggAdditionPlan PreviewAddEgg(EditorSubject? source,int species,NativeCatalog catalog);
    EggAdditionReceipt AddEgg(EggAdditionPlan plan,Func<BackupInfo> backup,string journalFolder);
    void UndoAddedEgg(EggAdditionReceipt receipt);
}
internal sealed class OfflineEggEditor:IEggStudioEditor
{
    internal const int Carton=0x10CDC,EggCount=0x10C7A,Stable=0x2D72F8,StableStride=412;
    readonly string _path,_session=Guid.NewGuid().ToString("N");readonly Func<bool> _running;readonly byte[] _key;
    readonly NativeCatalog _catalog;
    byte[] _data,_encrypted;byte[]? _undoBefore,_undoAfter;string? _undoJournal;Func<BackupInfo>? _backupUndo;
    internal string FileName=>Path.GetFileName(_path);
    internal OfflineEggEditor(string path,NativeCatalog catalog,Func<bool>? running=null)
    {
        _running=running??Storage.GameRunning;_path=Path.GetFullPath(path);_catalog=catalog;
        if(!new[]{"mhr_slot_1.sav","mhr_slot_2.sav","mhr_slot_3.sav"}.Contains(FileName,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("Choose a manual save slot (1, 2 or 3). Autosave and system saves are not edited.");
        Closed();_encrypted=File.ReadAllBytes(_path);var opened=SteamSaveCodec.Decode(_encrypted);_data=opened.Data;_key=opened.Key;Unchanged();Read(catalog);
    }
    void Closed(){if(_running())throw new InvalidOperationException("Save and close Stories 2 before opening or editing a save.");}
    void Unchanged()
    {
        Closed();if(!File.ReadAllBytes(_path).SequenceEqual(_encrypted))throw new InvalidOperationException("The save changed on disk. Reopen it before editing; nothing was overwritten.");
    }
    int Count()
    {
        int count=_data[EggCount];if(count>12)throw new InvalidDataException("Unsupported egg carton count.");
        for(int i=0;i<12;i++){int species=BitConverter.ToInt32(_data,Carton+i*120);if((i<count)!=(species!=0)||species!=0&&!_catalog.Species.Any(s=>s.Id==species))throw new InvalidDataException("The saved carton is not in the supported packed layout.");}
        return count;
    }
    public EditorSubject[] Read(NativeCatalog catalog)
    {
        Unchanged();int count=Count();var found=new List<EditorSubject>();
        for(int i=0;i<500;i++)
        {
            int at=Stable+i*StableStride,species=BitConverter.ToInt32(_data,at+52),level=_data[at+86];
            // Story-only Monsties may not occur in the hatchable-species table.
            // Preserve those records exactly rather than rejecting the whole save.
            if(species==0||!catalog.Species.Any(s=>s.Id==species))continue;
            if(level is <1 or >99)throw new InvalidDataException("The saved stable layout is unsupported.");
            string name=Encoding.UTF8.GetString(_data,at,30).Split('\0')[0];
            found.Add(new($"{name} · Lv. {level} · stable {i+1}",false,at,at+52,at+332,_data.AsSpan(at,StableStride).ToArray(),[],_session));
        }
        for(int i=0;i<count;i++)
        {
            int at=Carton+i*120,species=BitConverter.ToInt32(_data,at);string name=catalog.Species.First(s=>s.Id==species).Name;
            found.Add(new($"Egg {i+1} · {name}",true,at,at,at+8,_data.AsSpan(at,120).ToArray(),[],_session));
        }
        if(!found.Any(s=>!s.Egg))throw new InvalidDataException("This save has no supported owned Monsties.");
        return found.ToArray();
    }
    string Commit(byte[] after,Func<BackupInfo> backup,string journalFolder,string action,object detail)
    {
        Unchanged();byte[] encoded=SteamSaveCodec.Encode(after,_key);var decoded=SteamSaveCodec.DecodeWithKey(encoded,_key);
        // Header checksum is the only allowed extra difference introduced by encoding.
        if(!decoded.AsSpan(64).SequenceEqual(after.AsSpan(64)))throw new IOException("Edited save verification failed before replacement.");
        var saved=backup();Unchanged();
        Directory.CreateDirectory(journalFolder);string journal=Path.Combine(journalFolder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-offline-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(journal,JsonSerializer.Serialize(new{Action=action,Save=FileName,Backup=saved.Path,Before=Convert.ToHexString(SHA256.HashData(_encrypted)),After=Convert.ToHexString(SHA256.HashData(encoded)),Detail=detail},new JsonSerializerOptions{WriteIndented=true}));
        Replace(encoded);_undoBefore=_encrypted;_undoAfter=encoded;_undoJournal=journal;_backupUndo=backup;_encrypted=encoded;_data=decoded;return journal;
    }
    void Replace(byte[] encoded)
    {
        string temp=_path+".kinship-"+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(encoded);stream.Flush(true);}
            if(!File.ReadAllBytes(temp).SequenceEqual(encoded))throw new IOException("Temporary save verification failed.");
            Unchanged();File.Replace(temp,_path,null);
            if(!File.ReadAllBytes(_path).SequenceEqual(encoded))throw new IOException("The replaced save could not be verified. Use the verified backup before loading it.");
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public EditReceipt Apply(EditorSubject subject,EditorChange[] changes,Func<BackupInfo> backup,string journalFolder)
    {
        var current=Read(_catalog).SingleOrDefault(s=>s.Address==subject.Address&&s.Egg==subject.Egg);
        if(current is null||subject.Session!=_session||!current.Snapshot.SequenceEqual(subject.Snapshot)||changes.Length==0)throw new InvalidOperationException("Reopen or refresh the save and preview the changes again.");
        var after=(byte[])_data.Clone();
        foreach(var c in changes)
        {
            if(c.Before.Length!=4||c.After.Length!=4||!(subject.Egg&&c.Address==subject.SpeciesAddress||c.Address>=subject.GenesAddress&&c.Address<subject.GenesAddress+36&&(c.Address-subject.GenesAddress)%4==0)||!_data.AsSpan((int)c.Address,4).SequenceEqual(c.Before))throw new InvalidOperationException("Invalid saved egg/gene change.");
            c.After.CopyTo(after,(int)c.Address);
        }
        // Revalidate requested genes/species through the same catalog planner.
        var genes=Enumerable.Range(0,9).Select(i=>{int at=(int)subject.GenesAddress+i*4;return((int)BitConverter.ToUInt16(after,at),(int)after[at+3],after[at+2]!=0);}).ToArray();
        var allowed=StableEditor.Plan(subject,BitConverter.ToInt32(after,(int)subject.SpeciesAddress),genes,_catalog);
        if(allowed.Length!=changes.Length||changes.Any(c=>!allowed.Any(a=>a.Address==c.Address&&a.After.SequenceEqual(c.After))))throw new InvalidOperationException("The save edit did not match its preview.");
        string journal=Commit(after,backup,journalFolder,"Edit genes or egg species",changes);return new(subject,changes,journal);
    }
    public EggAdditionPlan PreviewAddEgg(EditorSubject? source,int species,NativeCatalog catalog)
    {
        var owned=Read(catalog);int count=Count();if(count>=12)throw new InvalidOperationException("The carton is full (12/12). Hatch an egg in-game first.");
        if(!catalog.Species.Any(s=>s.Id==species))throw new ArgumentException("Choose a hatchable species.");
        if(source is not null&&!owned.Any(s=>s.Egg&&s.Session==source.Session&&s.Address==source.Address&&s.Snapshot.SequenceEqual(source.Snapshot)))throw new InvalidOperationException("The template changed. Refresh the save first.");
        byte[] egg=source is null?catalog.StarterEgg(species):(byte[])source.Snapshot.Clone();BitConverter.GetBytes(species).CopyTo(egg,0);
        return new(_session,source,Carton,count,12,_data.AsSpan(Carton,12*120).ToArray(),egg,[],catalog.Species.First(s=>s.Id==species).Name,EggCount,(uint)count);
    }
    public EggAdditionReceipt AddEgg(EggAdditionPlan plan,Func<BackupInfo> backup,string journalFolder)
    {
        var checkedPlan=PreviewAddEgg(plan.Template,BitConverter.ToInt32(plan.Egg),_catalog);
        if(plan.Session!=_session||plan.Slot!=checkedPlan.Slot||plan.Carton!=Carton||plan.PresenceBefore!=checkedPlan.PresenceBefore||!plan.CartonBefore.SequenceEqual(checkedPlan.CartonBefore)||!plan.Egg.SequenceEqual(checkedPlan.Egg))throw new InvalidOperationException("The carton or preview changed. Preview the egg again.");
        var after=(byte[])_data.Clone();plan.Egg.CopyTo(after,Carton+plan.Slot*120);after[EggCount]=(byte)(plan.Slot+1);
        string backupPath="";BackupInfo Backup(){var info=backup();backupPath=info.Path;return info;}
        string journal=Commit(after,Backup,journalFolder,"Add saved egg",new{Slot=plan.Slot+1,plan.SpeciesName});return new(plan,journal,backupPath);
    }
    void UndoJournal(string journal)
    {
        if(_undoBefore is null||_undoAfter is null||_undoJournal!=journal)throw new InvalidOperationException("Undo is only available for the last edit in this opened save.");
        Unchanged();if(!_encrypted.SequenceEqual(_undoAfter))throw new InvalidOperationException("The save changed since the edit.");
        // Back up the edited file too before reverting it.
        (_backupUndo??throw new InvalidOperationException("The undo backup source is unavailable."))();Replace(_undoBefore);_encrypted=_undoBefore;_data=SteamSaveCodec.DecodeWithKey(_encrypted,_key);_undoBefore=null;_undoAfter=null;_undoJournal=null;
        var entry=JsonNode.Parse(File.ReadAllText(journal))!;entry["UndoneAt"]=DateTimeOffset.Now.ToString("O");File.WriteAllText(journal,entry.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
    }
    public void Undo(EditReceipt receipt)=>UndoJournal(receipt.Journal);
    public void UndoAddedEgg(EggAdditionReceipt receipt)=>UndoJournal(receipt.Journal);
}
