using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RidersToolkit;

internal sealed record EggAdditionPlan(string Session,EditorSubject? Template,long Carton,int Slot,int Capacity,byte[] CartonBefore,byte[] Egg,List<PointerGuard> Guards,string SpeciesName,long PresenceAddress,uint PresenceBefore)
{
    public long Target=>Carton+Slot*StableEditor.EggBytes;
}
internal sealed record EggAdditionReceipt(EggAdditionPlan Plan,string Journal,string BackupPath);

internal sealed partial class StableEditor
{
    internal const int EggBytes=0x78,CartonSlots=12;
    internal static readonly (int Rva,string Hex)[] EggAddSamples=[
        (0x25BBCF,"33F6488BF94889318BDE"),
        (0x25BBFF,"C6472CFF488977344889773C668977444889774889775066897754"),
        (0x25C830,"C70102000100C3"),
        (0x9A97C6,"488B0D53304401E82EA33600"),
        (0xD13B42,"488BC78BD748C1E80583E21F488D0C838B8190000000"),
        (0x25B900,"8339000F95C0C3"),
        (0x9A9733,"488D8E640C0100486BC7784803C8E8BA218BFF"),
        (0x9A974A,"FFC383FB0C7CDF"),
        (0x9A9755,"0F1045004533C941B001486BC778BAE2000000"),
        (0x9A97B8,"F20F104D70F20F118C30D40C0100"),
        (0x9AF007,"BAE7000000488B0D0DD84301E8E8283600F6D81BED33FF83C50C"),
        (0xD11923,"418B8C88900000000FA3C10F92C0")];
    void ReadyForEggAdd()
    {
        Ready();
        // Recheck the live allied-parameter path as well as the UI's cached
        // battle state, which can be stale while a disk backup blocks the UI.
        long battle=Q(layout.BattleSlot);
        if(Object(battle))
        {
            long actor=Q(battle+0x9D0);
            if(Object(actor))
            {
                long array=Q(actor+0x40);
                if(array>=65536&&memory.Query(array)!=null)
                {
                    long rider=Q(array+layout.ParameterOffset);
                    if(rider>=65536&&memory.Query(rider)!=null)throw new InvalidOperationException("Leave battle before adding an egg.");
                }
            }
        }
        if(EggAddSamples.Any(s=>!memory.Read(layout.ModuleBase+s.Rva,Convert.FromHexString(s.Hex).Length).SequenceEqual(Convert.FromHexString(s.Hex))))
            throw new InvalidOperationException("The egg addition routines do not match this build. Existing-egg editing remains separate.");
    }
    bool AddOwned(EggAdditionPlan p)=>p.Session==_session&&memory.IsAlive&&p.Guards.All(g=>Q(g.Address)==g.Value)&&memory.Query(p.Carton)!=null&&memory.Query(p.Carton+CartonSlots*EggBytes-1)!=null;
    void CheckAddition(EggAdditionPlan p,byte[] expected,uint? presence=null)
    {
        ReadyForEggAdd();
        if(!AddOwned(p)||!memory.Read(p.Carton,expected.Length).SequenceEqual(expected)||BitConverter.ToUInt32(memory.Read(p.PresenceAddress,4))!=(presence??p.PresenceBefore))
            throw new InvalidOperationException("Your carton or game connection changed. Preview the addition again.");
    }
    public EggAdditionPlan PreviewAddEgg(EditorSubject? source,int species,NativeCatalog catalog)
    {
        ReadyForEggAdd();
        var choice=catalog.Species.FirstOrDefault(s=>s.Id==species)??throw new ArgumentException("Choose a hatchable species.");
        // The normal read verifies a loaded save and an owned stable even when
        // the carton is empty. A new egg never uses a fabricated source address.
        var owned=Read(catalog);long save=Q(layout.SaveSlot),carton=save+layout.InventoryOffset+0x10C64;
        if(source is not null)
        {
            if(source.Session!=_session||!source.Egg||source.Snapshot.Length!=EggBytes||!Owned(source))throw new InvalidOperationException("Refresh and select a carton egg template, or choose New egg.");
            var current=owned.SingleOrDefault(s=>s.Egg&&s.Address==source.Address);
            if(current is null||!current.Snapshot.SequenceEqual(source.Snapshot))throw new InvalidOperationException("The template egg changed. Refresh and preview again.");
            if((source.Address-carton)%EggBytes!=0||source.Address<carton||source.Address>=carton+CartonSlots*EggBytes)throw new InvalidOperationException("The template is outside the carton.");
        }
        long manager=Q(layout.ModuleBase+0x1DED820),presenceManager=Q(layout.ModuleBase+0x1DEC820);
        if(!Object(manager)||!Object(presenceManager))throw new InvalidOperationException("The game's carton state is not ready. Load a solo save in a village.");
        // Native capacity uses story E7. Native addition sets system E2 in a
        // separate manager; never write E2 into the story manager.
        long flagsAddress=manager+0xAC,presenceAddress=presenceManager+0xAC;
        uint flags=BitConverter.ToUInt32(memory.Read(flagsAddress,4)),presence=BitConverter.ToUInt32(memory.Read(presenceAddress,4));
        int capacity=CartonSlots-((flags&(1u<<7))!=0?1:0);
        byte[] before=memory.Read(carton,CartonSlots*EggBytes);int used=0,slot=-1;
        for(int i=0;i<CartonSlots;i++){if(BitConverter.ToInt32(before,i*EggBytes)!=0)used++;else if(slot<0)slot=i;}
        if(slot<0||used>=capacity)throw new InvalidOperationException($"The carton is full ({used}/{capacity}). Hatch or release an egg before adding another.");
        if(source is not null&&!before.AsSpan(checked((int)(source.Address-carton)),EggBytes).SequenceEqual(source.Snapshot))throw new InvalidOperationException("The template changed while previewing. Refresh and retry.");
        byte[] egg=source is null?catalog.StarterEgg(species):(byte[])source.Snapshot.Clone();
        if(BitConverter.ToInt32(egg,4) is <0 or >2)throw new InvalidOperationException("The template egg's quality data is unsupported. Choose another egg.");
        for(int i=0;i<9;i++)
        {
            int at=8+i*4,id=BitConverter.ToUInt16(egg,at);var gene=catalog.Genes.FirstOrDefault(g=>g.Id==id);
            if(id!=0&&gene is null||egg[at+2]>1||egg[at+3]>(gene?.Stars??0))throw new InvalidOperationException("The template has unsupported gene data. Choose an ordinary unedited egg.");
        }
        BitConverter.GetBytes(species).CopyTo(egg,0);
        var guards=new List<PointerGuard>{new(layout.SaveSlot,save),new(save,Q(save)),new(layout.ModuleBase+0x1DED820,manager),new(manager,Q(manager)),new(flagsAddress,Q(flagsAddress)),new(layout.ModuleBase+0x1DEC820,presenceManager),new(presenceManager,Q(presenceManager))};
        if(source is not null)guards.AddRange(source.Guards);
        return new(_session,source,carton,slot,capacity,before,egg,guards,choice.Name,presenceAddress,presence);
    }
    public EggAdditionReceipt AddEgg(EggAdditionPlan plan,Func<BackupInfo> backup,string journalFolder)
    {
        if(plan.Session!=_session||plan.Egg.Length!=EggBytes||plan.CartonBefore.Length!=CartonSlots*EggBytes||plan.Slot is <0 or >=CartonSlots||plan.Capacity is <1 or >CartonSlots||BitConverter.ToInt32(plan.CartonBefore,plan.Slot*EggBytes)!=0||BitConverter.ToInt32(plan.Egg)<=0)
            throw new InvalidOperationException("Preview an addition to an empty carton slot first.");
        long save=Q(layout.SaveSlot);if(!Object(save)||plan.Carton!=save+layout.InventoryOffset+0x10C64||!Object(Q(layout.ModuleBase+0x1DEC820))||plan.PresenceAddress!=Q(layout.ModuleBase+0x1DEC820)+0xAC)throw new InvalidOperationException("The carton no longer belongs to this save. Refresh first.");
        CheckAddition(plan,plan.CartonBefore);
        var saved=backup(); // A failed/cancelled backup prevents every memory write.
        Directory.CreateDirectory(journalFolder);string file=Path.Combine(journalFolder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-egg-add-"+Guid.NewGuid().ToString("N")+".json");
        var receipt=new EggAdditionReceipt(plan,file,saved.Path);
        void Journal(string state)=>File.WriteAllText(file,JsonSerializer.Serialize(new{state,SaveBackup=saved.Path,Receipt=receipt},new JsonSerializerOptions{WriteIndented=true}));
        Journal("prepared");byte[] expected=(byte[])plan.CartonBefore.Clone();uint expectedPresence=plan.PresenceBefore;
        try
        {
            // Commit the validity/species field last. The game sees an empty
            // entry while its metadata is being staged. Writes remain <=8 bytes.
            for(int offset=4;offset<EggBytes;offset+=4)Stage(offset);
            Stage(0);
            CheckAddition(plan,expected,expectedPresence);
            uint afterPresence=expectedPresence|4u;
            if(afterPresence!=expectedPresence)
            {
                memory.Write(plan.PresenceAddress,BitConverter.GetBytes(afterPresence));expectedPresence=afterPresence;
            }
            CheckAddition(plan,expected,expectedPresence);Journal("applied");return receipt;
        }
        catch(Exception original)
        {
            try
            {
                // A whole-carton comparison is required even for rollback. If
                // the game changed it, retain the journal and stop writing.
                CheckAddition(plan,expected,expectedPresence);
                RestoreEntry(plan,expected,plan.CartonBefore,expectedPresence);
                Journal("failed; staged entry restored");
            }
            catch(Exception rollback){Journal("failed; rollback blocked: "+rollback.Message);throw new InvalidOperationException("Egg addition stopped. The carton changed or could not be restored; inspect the game before saving. A verified backup and change journal are available.",original);}
            throw;
        }
        void Stage(int offset)
        {
            CheckAddition(plan,expected,expectedPresence);int at=plan.Slot*EggBytes+offset;byte[] value=plan.Egg.AsSpan(offset,4).ToArray();
            if(expected.AsSpan(at,4).SequenceEqual(value))return;
            memory.Write(plan.Target+offset,value);value.CopyTo(expected,at);
            if(!memory.Read(plan.Target+offset,4).SequenceEqual(value))throw new IOException("The game did not retain the staged egg data.");
        }
    }
    void RestoreEntry(EggAdditionPlan plan,byte[] expected,byte[] before,uint expectedPresence)
    {
        // Withdraw the validity field first on undo; metadata then returns to
        // the exact previous empty-entry bytes. Other eggs remain untouched.
        foreach(int offset in Enumerable.Range(0,EggBytes/4).Select(i=>i*4))
        {
            CheckAddition(plan,expected,expectedPresence);int at=plan.Slot*EggBytes+offset;byte[] value=before.AsSpan(at,4).ToArray();
            if(expected.AsSpan(at,4).SequenceEqual(value))continue;
            memory.Write(plan.Target+offset,value);value.CopyTo(expected,at);
            if(!memory.Read(plan.Target+offset,4).SequenceEqual(value))throw new IOException("The game did not retain the restored carton data.");
        }
        CheckAddition(plan,expected,expectedPresence);
        if(expectedPresence!=plan.PresenceBefore)memory.Write(plan.PresenceAddress,BitConverter.GetBytes(plan.PresenceBefore));
        CheckAddition(plan,expected,plan.PresenceBefore);
    }
    public void UndoAddedEgg(EggAdditionReceipt receipt)
    {
        var p=receipt.Plan;byte[] expected=(byte[])p.CartonBefore.Clone();p.Egg.CopyTo(expected,p.Slot*EggBytes);
        CheckAddition(p,expected,p.PresenceBefore|4u);RestoreEntry(p,expected,p.CartonBefore,p.PresenceBefore|4u);
        var journal=JsonNode.Parse(File.ReadAllText(receipt.Journal))!;journal["state"]="undone";journal["UndoneAt"]=DateTimeOffset.Now.ToString("O");File.WriteAllText(receipt.Journal,journal.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
    }
}
