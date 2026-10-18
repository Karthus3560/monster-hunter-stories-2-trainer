using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace RidersToolkit;

internal sealed record EditorChoice(int Id,string Name,int Stars=2)
{
    public override string ToString()=>$"{Name} · {Id}";
}
internal sealed class NativeCatalog
{
    public EditorChoice[] Species {get;}
    public EditorChoice[] Genes {get;}
    readonly Dictionary<int,byte[]> _starterEggs=[];
    internal byte[] StarterEgg(int species)=>_starterEggs.TryGetValue(species,out var egg)?(byte[])egg.Clone():throw new InvalidOperationException("Starter gene data is unavailable for this species. Choose an existing egg template.");
    static Dictionary<int,string> Labels(string name)
    {
        var uri=new Uri($"pack://application:,,,/RidersToolkit;component/Assets/{name}-labels.json");
        using var stream=Application.GetResourceStream(uri)!.Stream;
        return JsonSerializer.Deserialize<Dictionary<int,string>>(stream)??[];
    }
    public NativeCatalog(string game)
    {
        var arc=new GameArchive(GameDataMods.Original(game));
        XfsTable Table(string name)=>new(arc.Entries.Single(e=>e.Name==name).Extract());
        var monsters=Labels("monster");var genes=Labels("gene");
        var eggRows=Table(@"table\gene_edit").AllRows.Where(r=>r.Fields.ContainsKey("mBuddyID")&&r.Number("mBuddyID")>0).ToArray();
        Species=eggRows.Where(r=>r.Fields.ContainsKey("mBuddyID")).Select(r=>(int)r.Number("mBuddyID")).Where(i=>i>0).Distinct()
            .Select(id=>new EditorChoice(id,monsters.GetValueOrDefault(id,$"Monstie {id}"))).OrderBy(x=>x.Name).ToArray();
        var allowed=Table(@"table\cheatCheck\cheat_check_table_gene").AllRows.Where(r=>r.Fields.ContainsKey("mGeneId")).Select(r=>(int)r.Number("mGeneId")).ToHashSet();
        var rows=Table(@"table\gene_table").Rows;
        Genes=rows.Select((r,id)=>(r,id)).Where(x=>x.id>=1&&x.id<=17||x.id>17&&allowed.Contains(x.id))
            .Select(x=>new EditorChoice(x.id,x.id==1?"Empty slot":x.id==17?"Rainbow Gene":genes.GetValueOrDefault(x.id,x.id<17?$"Locked slot {x.id}":$"Gene {x.id}"),Math.Clamp((int)x.r.Number("mStrengthMaxLevel"),0,2))).OrderBy(x=>x.Name).ToArray();
        foreach(var row in eggRows)
        {
            int species=(int)row.Number("mBuddyID");byte[] egg=new byte[StableEditor.EggBytes];
            BitConverter.GetBytes(species).CopyTo(egg,0);egg[0x2C]=0xFF;
            // Native reset uses FF for no hatch bonus and zero for origin data.
            // Keep species-fixed genes; open remaining slots, without random rolls.
            var fixedGenes=Enumerable.Range(0,9).Select(i=>(int)row.Number($"mGeneFix[{i}]")).Where(id=>id!=0).Distinct().ToArray();
            for(int i=0;i<9;i++)
            {
                int id=i<fixedGenes.Length?fixedGenes[i]:1;
                if(!Genes.Any(g=>g.Id==id)||id>=rows.Count)throw new InvalidDataException("Starter egg gene data differs from the inspected game.");
                BitConverter.GetBytes((ushort)id).CopyTo(egg,8+i*4);
                egg[10+i*4]=(byte)(rows[id].Number("mOpenLevel")>1||id==720?1:0);
            }
            _starterEggs.Add(species,egg);
        }
        if(Species.Length is <50 or >200||Genes.Length is <100 or >800||!Genes.Any(g=>g.Id==17))throw new InvalidDataException("Editor catalog differs from the inspected game.");
    }
    internal NativeCatalog(EditorChoice[] species,EditorChoice[] genes,Dictionary<int,byte[]>? starterEggs=null){Species=species;Genes=genes;if(starterEggs is not null)foreach(var pair in starterEggs)_starterEggs.Add(pair.Key,(byte[])pair.Value.Clone());}
}
internal sealed record EditorSubject(string Label,bool Egg,long Address,long SpeciesAddress,long GenesAddress,byte[] Snapshot,List<PointerGuard> Guards,string Session)
{
    public override string ToString()=>Label;
}
internal sealed record EditorChange(long Address,byte[] Before,byte[] After,string Label);
internal sealed record EditReceipt(EditorSubject Subject,EditorChange[] Changes,string Journal);

internal sealed partial class StableEditor(IProcessMemory memory,AutomaticLayout layout,Func<bool> inBattle):IEggStudioEditor
{
    readonly string _session=Guid.NewGuid().ToString("N");
    public bool Supported=>layout.ExtrasSupported;
    long Q(long a)=>BitConverter.ToInt64(memory.Read(a,8));
    int I(long a)=>BitConverter.ToInt32(memory.Read(a,4));
    bool Object(long a)=>a>=65536&&memory.Query(a)!=null&&Q(a)>=layout.ModuleBase&&Q(a)<layout.ModuleBase+layout.ModuleSize;
    void Ready(){if(!Supported||!memory.IsAlive||!layout.Verify(memory))throw new InvalidOperationException("Editor routines are unavailable. Reconnect to the inspected game.");if(inBattle())throw new InvalidOperationException("Leave battle before using the editor.");}
    bool Owned(EditorSubject s)=>memory.IsAlive&&s.Guards.All(g=>Q(g.Address)==g.Value)&&memory.Query(s.Address)!=null;
    public EditorSubject[] Read(NativeCatalog catalog)
    {
        Ready();long save=Q(layout.SaveSlot);if(!Object(save))throw new InvalidOperationException("Load a solo save in a village first.");
        var root=new List<PointerGuard>{new(layout.SaveSlot,save),new(save,Q(save))};var found=new List<EditorSubject>();
        long collection=save+0x9A5A0;int count=I(collection+0x28);long array=Q(collection+0x38);
        if(count is <1 or >1000||array<65536||memory.Query(array)==null)throw new InvalidOperationException("Owned Monsties are not ready. Load a solo save first.");
        for(int i=0;i<count;i++)
        {
            long p=Q(array+i*8);if(!Object(p)||Q(p)!=layout.ModuleBase+0x162DA48)continue;
            int species=I(p+0x64),uid=I(p+0x60);byte level=memory.Read(p+0x86,1)[0];
            if(!catalog.Species.Any(x=>x.Id==species)||level is <1 or >99||uid<0)continue;
            var name=Encoding.UTF8.GetString(memory.Read(p+0x30,30)).Split('\0')[0];
            var guards=new List<PointerGuard>(root){new(collection+0x28,Q(collection+0x28)),new(collection+0x38,array),new(array+i*8,p),new(p,Q(p)),new(p+0x60,Q(p+0x60))};
            found.Add(new($"{name} · Lv. {level} · stable {i+1}",false,p,p+0x64,p+0x17C,memory.Read(p,0x1E0),guards,_session));
        }
        for(int i=0;i<12;i++)
        {
            long p=save+layout.InventoryOffset+0x10C64+i*0x78;int species=I(p);
            if(!catalog.Species.Any(x=>x.Id==species))continue;
            string name=catalog.Species.First(x=>x.Id==species).Name;
            found.Add(new($"Egg {i+1} · {name}",true,p,p,p+8,memory.Read(p,0x78),[..root],_session));
        }
        return found.ToArray();
    }
    public static EditorChange[] Plan(EditorSubject subject,int species,IReadOnlyList<(int Id,int Stars,bool Locked)> genes,NativeCatalog catalog)
    {
        if(genes.Count!=9)throw new ArgumentException("Choose nine gene slots.");
        if(!catalog.Species.Any(s=>s.Id==species))throw new ArgumentException("Choose a hatchable species from the list.");
        var output=new List<EditorChange>();
        void Add(long a,byte[] after,string name)
        {
            int off=checked((int)(a-subject.Address));var before=subject.Snapshot.AsSpan(off,after.Length).ToArray();
            if(!before.SequenceEqual(after))output.Add(new(a,before,after,name));
        }
        int current=BitConverter.ToInt32(subject.Snapshot,checked((int)(subject.SpeciesAddress-subject.Address)));
        if(!subject.Egg&&current!=species)throw new ArgumentException("Species changes are limited to existing eggs.");
        if(subject.Egg)Add(subject.SpeciesAddress,BitConverter.GetBytes(species),"Species: "+catalog.Species.First(x=>x.Id==current).Name+" → "+catalog.Species.First(x=>x.Id==species).Name);
        for(int i=0;i<9;i++)
        {
            var g=genes[i];if(g.Id is <0 or >65535||g.Stars is <0 or >2)throw new ArgumentException("Invalid gene or star level.");var choice=catalog.Genes.FirstOrDefault(x=>x.Id==g.Id);
            byte[] after=[..BitConverter.GetBytes((ushort)g.Id),(byte)(g.Locked?1:0),(byte)g.Stars];
            bool unchanged=subject.Snapshot.AsSpan(checked((int)(subject.GenesAddress-subject.Address))+i*4,4).SequenceEqual(after);
            if(!unchanged&&(choice is null||g.Stars<0||g.Stars>choice.Stars))throw new ArgumentException($"Slot {i+1}: choose a valid gene and star level.");
            int oldAt=checked((int)(subject.GenesAddress-subject.Address))+i*4,oldId=BitConverter.ToUInt16(subject.Snapshot,oldAt);
            string oldName=catalog.Genes.FirstOrDefault(x=>x.Id==oldId)?.Name??$"Existing gene {oldId}";
            Add(subject.GenesAddress+i*4,after,$"Slot {i+1}: {oldName} ({subject.Snapshot[oldAt+3]}★) → {choice?.Name??oldName} ({g.Stars}★), "+(g.Locked?"locked":"open"));
        }
        return output.ToArray();
    }
    public EditReceipt Apply(EditorSubject subject,EditorChange[] changes,Func<BackupInfo> backup,string journalFolder)
    {
        Ready();if(subject.Session!=_session)throw new InvalidOperationException("The game connection changed. Refresh and preview again.");if(changes.Length==0)throw new InvalidOperationException("Preview a change first.");
        if(!Owned(subject)||!memory.Read(subject.Address,subject.Snapshot.Length).SequenceEqual(subject.Snapshot))throw new InvalidOperationException("The selected egg or Monstie changed. Refresh and preview again.");
        // Recreate a narrow allowlist; callers cannot turn the editor into an arbitrary memory writer.
        if(changes.Any(c=>c.Before.Length!=4||c.After.Length!=4||
            !(subject.Egg&&c.Address==subject.SpeciesAddress ||c.Address>=subject.GenesAddress&&c.Address<subject.GenesAddress+36&&(c.Address-subject.GenesAddress)%4==0))||changes.Select(c=>c.Address).Distinct().Count()!=changes.Length)
            throw new InvalidOperationException("Invalid editor change set.");
        var saved=backup();Directory.CreateDirectory(journalFolder);string path=Path.Combine(journalFolder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")+".json");
        var receipt=new EditReceipt(subject,changes,path);
        void Journal(string state)=>File.WriteAllText(path,JsonSerializer.Serialize(new{state,SaveBackup=saved.Path,Receipt=receipt},new JsonSerializerOptions{WriteIndented=true}));
        Journal("prepared");var done=new List<EditorChange>();byte[] expected=(byte[])subject.Snapshot.Clone();
        try
        {
            foreach(var c in changes)
            {
                Ready();if(!Owned(subject)||!memory.Read(subject.Address,expected.Length).SequenceEqual(expected))throw new InvalidOperationException("Game data changed during the edit. Refresh before trying again.");
                memory.Write(c.Address,c.After);done.Add(c);c.After.CopyTo(expected,checked((int)(c.Address-subject.Address)));
                if(!memory.Read(c.Address,4).SequenceEqual(c.After))throw new IOException("The game did not retain the change.");
            }
            Journal("applied");return receipt;
        }
        catch
        {
            foreach(var c in done.AsEnumerable().Reverse())if(Owned(subject)&&memory.Read(c.Address,4).SequenceEqual(c.After))memory.Write(c.Address,c.Before);
            Journal("failed; owned fields rolled back where unchanged");throw;
        }
    }
    public void Undo(EditReceipt receipt)
    {
        Ready();var s=receipt.Subject;if(s.Session!=_session)throw new InvalidOperationException("The connection changed after editing. Undo is blocked; refresh the studio.");
        // Monstie identity includes its unchanged species/UID. Egg identity is the complete expected record.
        var expected=(byte[])s.Snapshot.Clone();foreach(var c in receipt.Changes)c.After.CopyTo(expected,checked((int)(c.Address-s.Address)));
        if(!Owned(s)||!memory.Read(s.Address,expected.Length).SequenceEqual(expected))throw new InvalidOperationException("This object changed after editing. Undo is blocked; use the verified save backup if needed.");
        foreach(var c in receipt.Changes.Reverse())
        {
            Ready();if(!Owned(s)||!memory.Read(s.Address,expected.Length).SequenceEqual(expected))throw new InvalidOperationException("Object changed during undo. No further fields were restored.");
            memory.Write(c.Address,c.Before);c.Before.CopyTo(expected,checked((int)(c.Address-s.Address)));
        }
        var journal=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(receipt.Journal))!;
        journal["state"]="undone";journal["UndoneAt"]=DateTimeOffset.Now.ToString("O");
        File.WriteAllText(receipt.Journal,journal.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
    }
}
