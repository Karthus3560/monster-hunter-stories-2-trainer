using System.IO;
using System.Text;
using System.Text.Json;

namespace RidersToolkit;
internal static class OfflineEditorTests
{
    static void Require(bool value,string message="Offline editor assertion failed"){if(!value)throw new Exception(message);}
    static void Reject(Action action){try{action();}catch(Exception ex)when(ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException){return;}throw new Exception("Expected offline operation to be refused");}
    internal static void Run(Action<string,Action> check)
    {
        check("Steam save codec roundtrips both key lengths and rejects corrupt saves or checksums",()=>
        {
            foreach(int length in new[]{55,56})
            {
                using var f=new Fixture(length);var decoded=SteamSaveCodec.Decode(f.Original);Require(decoded.Key.Length==length&&SteamSaveCodec.Encode(decoded.Data,decoded.Key).SequenceEqual(f.Original));
                var corrupt=(byte[])f.Original.Clone();corrupt[100]^=1;Reject(()=>SteamSaveCodec.Decode(corrupt));Reject(()=>SteamSaveCodec.Decode(f.Original[..100]));
                decoded.Data[100]^=1;Reject(()=>SteamSaveCodec.Validate(decoded.Data,decoded.Key));
            }
        });
        check("Offline Add creates the first egg and increments the saved count, with exact backed-up Undo",()=>
        {
            using var f=new Fixture();var editor=f.Open();var p=editor.PreviewAddEgg(null,2,f.Catalog);Require(p.Slot==0&&f.Backups==0&&File.ReadAllBytes(f.File).SequenceEqual(f.Original));
            var receipt=editor.AddEgg(p,f.Backup,f.Journals);Require(f.Backups==1&&File.ReadAllBytes(f.BackupFiles[0]).SequenceEqual(f.Original));
            byte[] edited=SteamSaveCodec.Decode(File.ReadAllBytes(f.File)).Data;
            Require(edited[OfflineEggEditor.EggCount]==1&&BitConverter.ToInt32(edited,OfflineEggEditor.Carton)==2&&editor.Read(f.Catalog).Count(s=>s.Egg)==1);
            var allowed=Enumerable.Range(12,20).Concat([OfflineEggEditor.EggCount]).Concat(Enumerable.Range(OfflineEggEditor.Carton,120)).ToHashSet();
            for(int i=0;i<edited.Length;i++)if(edited[i]!=f.Data[i])Require(allowed.Contains(i),"Unrelated save bytes changed");
            editor.UndoAddedEgg(receipt);Require(f.Backups==2&&File.ReadAllBytes(f.File).SequenceEqual(f.Original));
            using var journal=JsonDocument.Parse(File.ReadAllText(receipt.Journal));Require(journal.RootElement.TryGetProperty("UndoneAt",out _));
        });
        check("Offline gene edits preserve species, unrelated data and restore the exact encrypted original",()=>
        {
            using var f=new Fixture();var editor=f.Open();var subject=editor.Read(f.Catalog).Single();var genes=Enumerable.Range(0,9).Select(i=>(i==0?24:1,i==0?2:0,false)).ToArray();
            var changes=StableEditor.Plan(subject,1,genes,f.Catalog);var receipt=editor.Apply(subject,changes,f.Backup,f.Journals);
            var decoded=SteamSaveCodec.Decode(File.ReadAllBytes(f.File)).Data;Require(BitConverter.ToInt32(decoded,(int)subject.SpeciesAddress)==1&&BitConverter.ToUInt16(decoded,(int)subject.GenesAddress)==24&&decoded[(int)subject.GenesAddress+3]==2);
            var allowed=Enumerable.Range(12,20).Concat(changes.SelectMany(c=>Enumerable.Range((int)c.Address,4))).ToHashSet();
            for(int i=0;i<decoded.Length;i++)if(decoded[i]!=f.Data[i])Require(allowed.Contains(i));
            editor.Undo(receipt);Require(f.Backups==2&&File.ReadAllBytes(f.File).SequenceEqual(f.Original));
        });
        check("A running game or failed backup prevents every offline save replacement",()=>
        {
            using var f=new Fixture();f.Running=true;Reject(()=>f.Open());f.Running=false;var editor=f.Open();var p=editor.PreviewAddEgg(null,2,f.Catalog);
            Reject(()=>editor.AddEgg(p,()=>throw new IOException("Backup failed"),f.Journals));Require(File.ReadAllBytes(f.File).SequenceEqual(f.Original));
            f.Running=true;Reject(()=>editor.AddEgg(p,f.Backup,f.Journals));Require(f.Backups==0&&File.ReadAllBytes(f.File).SequenceEqual(f.Original));
        });
        check("A disk change during backup is preserved and blocks offline replacement",()=>
        {
            using var f=new Fixture();var editor=f.Open();var p=editor.PreviewAddEgg(null,2,f.Catalog);var changed=(byte[])f.Original.Clone();changed[80]^=1;
            Reject(()=>editor.AddEgg(p,()=>{var b=f.Backup();File.WriteAllBytes(f.File,changed);return b;},f.Journals));Require(File.ReadAllBytes(f.File).SequenceEqual(changed));
        });
        check("Offline editing rejects full or malformed cartons, unsafe names and forged change addresses",()=>
        {
            using var f=new Fixture();var editor=f.Open();var subject=editor.Read(f.Catalog).Single();
            Reject(()=>editor.Apply(subject,[new(100,[0,0,0,0],[1,0,0,0],"invalid")],f.Backup,f.Journals));Require(f.Backups==0);
            Reject(()=>new OfflineEggEditor(Path.Combine(f.Root,"mhr_sys.sav"),f.Catalog,()=>false));
            var full=(byte[])f.Data.Clone();full[OfflineEggEditor.EggCount]=12;for(int i=0;i<12;i++)BitConverter.GetBytes(1).CopyTo(full,OfflineEggEditor.Carton+120*i);
            File.WriteAllBytes(f.File,SteamSaveCodec.Encode(full,f.Key));var fullEditor=f.Open();Reject(()=>fullEditor.PreviewAddEgg(null,2,f.Catalog));
            full[OfflineEggEditor.EggCount]=13;File.WriteAllBytes(f.File,SteamSaveCodec.Encode(full,f.Key));Reject(()=>f.Open());
        });
        check("Undo refuses changed encrypted saves without writing or clearing the user's changes",()=>
        {
            using var f=new Fixture();var editor=f.Open();var r=editor.AddEgg(editor.PreviewAddEgg(null,2,f.Catalog),f.Backup,f.Journals);var changed=File.ReadAllBytes(f.File);changed[200]^=1;File.WriteAllBytes(f.File,changed);
            Reject(()=>editor.UndoAddedEgg(r));Require(f.Backups==1&&File.ReadAllBytes(f.File).SequenceEqual(changed));
        });
    }
    internal sealed class Fixture:IDisposable
    {
        public string Root=Path.Combine(Path.GetTempPath(),"kinship-offline-"+Guid.NewGuid().ToString("N"));
        public string File=>Path.Combine(Root,"mhr_slot_1.sav");public string Journals=>Path.Combine(Root,"journals");
        public byte[] Data=new byte[SteamSaveCodec.Size],Original,Key;public bool Running;public int Backups;public List<string> BackupFiles=[];
        public NativeCatalog Catalog;
        public Fixture(int length=55)
        {
            Directory.CreateDirectory(Root);Key=SteamSaveCodec.Key(0x1693C111)[..length];
            BitConverter.GetBytes(1L).CopyTo(Data,0);BitConverter.GetBytes(24).CopyTo(Data,8);BitConverter.GetBytes(0x808070).CopyTo(Data,32);
            uint crc=0xffffffff;foreach(byte b in Key){crc^=b;for(int i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0xEDB88320u:0);};BitConverter.GetBytes(crc).CopyTo(Data,36);
            int at=OfflineEggEditor.Stable;Encoding.UTF8.GetBytes("Test Monstie").CopyTo(Data,at);BitConverter.GetBytes(1).CopyTo(Data,at+52);Data[at+86]=10;
            // An unrelated story-only record must survive every edit and undo.
            int special=at+OfflineEggEditor.StableStride;Encoding.UTF8.GetBytes("Unlisted story buddy").CopyTo(Data,special);BitConverter.GetBytes(9999).CopyTo(Data,special+52);Data[special+86]=30;
            byte[] starter=new byte[120];starter[0x2C]=255;for(int i=0;i<9;i++){BitConverter.GetBytes((ushort)1).CopyTo(starter,8+i*4);BitConverter.GetBytes((ushort)1).CopyTo(Data,at+332+i*4);}
            Catalog=new([new(1,"Velocidrome"),new(2,"Rathian")],[new(1,"Empty",0),new(17,"Rainbow",0),new(24,"Gene",2)],new(){[1]=starter,[2]=starter});
            Original=SteamSaveCodec.Encode(Data,Key);System.IO.File.WriteAllBytes(File,Original);
        }
        public OfflineEggEditor Open()=>new(File,Catalog,()=>Running);
        public BackupInfo Backup(){string path=Path.Combine(Root,"backup-"+(++Backups)+".sav");System.IO.File.Copy(File,path);BackupFiles.Add(path);return new("fixture",path,"","");}
        public void Dispose(){string path=Path.GetFullPath(Root);if(Directory.GetParent(path)?.FullName==Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))&&Path.GetFileName(path).StartsWith("kinship-offline-")&&Directory.Exists(path))Directory.Delete(path,true);}
    }
    internal static int InspectInstalledReadOnly()
    {
        try
        {
            Storage.Discover();var catalog=new NativeCatalog(Storage.GameDirectory??throw new InvalidOperationException("Game not detected"));
            var reports=new List<object>();
            foreach(string file in new[]{"mhr_slot_1.sav","mhr_slot_2.sav","mhr_slot_3.sav"})
            {
                string path=Path.Combine(Storage.SaveDirectory??throw new InvalidOperationException("Save folder not detected"),file);if(!System.IO.File.Exists(path))continue;
                // Read and roundtrip only. This mode exposes no edit or commit action.
                var before=System.IO.File.ReadAllBytes(path);var d=SteamSaveCodec.Decode(before);var editor=new OfflineEggEditor(path,catalog,()=>false);var subjects=editor.Read(catalog);
                if(!SteamSaveCodec.Encode(d.Data,d.Key).SequenceEqual(before)||!System.IO.File.ReadAllBytes(path).SequenceEqual(before))throw new IOException("Save changed during read-only inspection");
                reports.Add(new{Slot=file,Monsties=subjects.Count(s=>!s.Egg),Eggs=subjects.Count(s=>s.Egg),Checksum=true,ExactRoundtrip=true,OriginalUnchanged=true});
            }
            System.IO.File.WriteAllText(Path.Combine(Storage.Root,"offline-readonly-report.json"),JsonSerializer.Serialize(reports,new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception ex){System.IO.File.WriteAllText(Path.Combine(Storage.Root,"offline-readonly-report.json"),JsonSerializer.Serialize(new{Error=ex.Message}));return 1;}
    }
}
