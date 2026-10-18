using System.IO;

namespace RidersToolkit;
internal static class ExtraTests
{
    static void Require(bool v,string message="Extra feature assertion failed"){if(!v)throw new Exception(message);}
    static void Reject(Action a){bool rejected=false;try{a();}catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or IOException){rejected=true;}Require(rejected,"Expected refusal");}
    static AutomaticLayout Layout(AutomaticTests.FixtureMemory m)=>m.Layout() with {ModuleSize=0x5226000,ExtrasSupported=true};
    static Feature Feature(IReadOnlyList<Feature> fs,string id)=>fs.Single(x=>x.Id==id);
    static float Float(AutomaticTests.FixtureMemory m,long a)=>BitConverter.ToSingle(m.Read(a,4));
    internal static void Run(Action<string,Action> check)
    {
        check("Extra routines fail closed when any native proof differs",()=>
        {
            byte[] image=new byte[0xCDCF00];foreach(var (r,h) in ExtraController.Samples)Convert.FromHexString(h).CopyTo(image,r);
            var proofs=new List<CodeProof>();Require(ExtraController.Detect(image,0x140000000,proofs)&&proofs.Count==ExtraController.Samples.Length);
            image[0xC0C380]^=1;Require(!ExtraController.Detect(image,0,[]));
        });
        check("Prayer selection maintains a native effect, keeps pot level, and restores previous prayer",()=>
        {
            using var m=new AutomaticTests.FixtureMemory();m.Put(m.Save+0x9A5C8,1);long r=m.Save+0x50D9EC;m.Put(r,4);m.Put(r+8,2);m.Put(r+0xC,(ushort)123);
            var fs=Catalog.Create().ToArray();var f=Feature(fs,"prayerBuff");f.Active=true;f.Target="5";var c=new ExtraController(m,Layout(m),fs);
            for(int i=0;i<25;i++)c.Tick(false);Require(m.Int(r)==0&&m.Int(r+8)==5&&m.U16(r+0xC)==123);
            f.Target="8";c.Tick(false);Require(m.Int(r+8)==8);c.Restore();Require(m.Int(r)==4&&m.Int(r+8)==2&&m.U16(r+0xC)==123);
        });
        check("Damage taken requests native protection at each pot level and restores all captured values",()=>
        {
            using var m=new AutomaticTests.FixtureMemory();m.Put(m.Save+0x9A5C8,1);
            long manager=m.DataBase+0x180000,table=m.DataBase+0x190000,rows=m.DataBase+0x1A0000,row=m.DataBase+0x1B0000;
            m.Put(m.ModuleBase+0x1DEBC10,manager);m.Put(manager,m.ModuleBase+0x1000);m.Put(manager+0xC98,table);m.Put(table,m.ModuleBase+0x1000);m.Put(table+0xD0,9);m.Put(table+0xE0,rows);m.Put(rows+6*8,row);m.Put(row,m.ModuleBase+0x1000);m.Put(row+0x48,3);
            for(int i=0;i<20;i++)m.Put(row+0x4C+i*4,BitConverter.SingleToInt32Bits(10+i));
            var fs=Catalog.Create().ToArray();var f=Feature(fs,"damageTaken");f.Active=true;f.Target="0.25";var c=new ExtraController(m,Layout(m),fs);c.Tick(false);
            Require(f.Bound&&Float(m,row+0x4C)==75&&Float(m,row+0x98)==75&&m.Int(m.Save+0x50DA24)==6);
            f.Target="0";c.Tick(false);Require(Float(m,row+0x4C)==100);c.Restore();Require(Float(m,row+0x4C)==10&&Float(m,row+0x98)==29&&m.Int(m.Save+0x50DA24)==0);
        });
        check("Faster gathering is limited to manual materials and restores after a chest or action change",()=>
        {
            using var m=new AutomaticTests.FixtureMemory();long player=m.DataBase+0x180000,ctrl=m.DataBase+0x190000,action=m.DataBase+0x1A0000;
            m.Put(m.Save+0x33398,player);m.Put(player,m.ModuleBase+0x1000);m.Put(player+0x4888,ctrl);m.Put(ctrl,m.ModuleBase+0x1000);m.Put(ctrl+0xD940,action);m.Put(action,m.ModuleBase+0x2000);m.Put(action+0x30,player);m.Put(player+0x66C,BitConverter.SingleToInt32Bits(1f));
            m.Put(m.ModuleBase+0x2020,m.ModuleBase+0x3000);m.Write(m.ModuleBase+0x3000,[0x48,0x8D,0x05,0xF9,0x0F,0,0,0xC3]);m.Put(m.ModuleBase+0x4008,m.ModuleBase+0x5000);
            void Type(string type)=>m.Write(m.ModuleBase+0x5000,System.Text.Encoding.ASCII.GetBytes(type+"\0"));
            Type("cPlActGather_PickUp");var fs=Catalog.Create().ToArray();Feature(fs,"fastGather").Active=true;var c=new ExtraController(m,Layout(m),fs);c.Tick(false);Require(Float(m,player+0x66C)==4);
            Type("cPlActGather_ChestAccess");c.Tick(false);Require(Float(m,player+0x66C)==1);Type("cPlActGather_Mining");c.Tick(false);Require(Float(m,player+0x66C)==4);c.Tick(true);Require(Float(m,player+0x66C)==1);
        });
        check("Recycled save owners prevent restoration writes and title screen stays unchanged",()=>
        {
            using var m=new AutomaticTests.FixtureMemory();var fs=Catalog.Create().ToArray();Feature(fs,"prayerBuff").Active=true;var c=new ExtraController(m,Layout(m),fs);int n=m.Writes;c.Tick(false);Require(m.Writes==n);
            m.Put(m.Save+0x9A5C8,1);c.Tick(false);m.Put(Layout(m).SaveSlot,0L);n=m.Writes;c.Restore();Require(m.Writes==n);
        });
        check("Field-menu item preservation changes only its use quantity, never the generic remove routine",()=>
        {
            using var m=new AutomaticTests.FixtureMemory();m.Put(m.Save+0x9A5C8,1);byte[] original=Convert.FromHexString("41B801000000E8530C4700");m.Write(m.ModuleBase+0x541912,original);
            byte[] remove=Convert.FromHexString("4863C2488D14C133C00FB74A06412BC885C9660F4FC16689420633C0C3");m.Write(m.ModuleBase+0x9B2570,remove);
            var fs=Catalog.Create().ToArray();Feature(fs,"fieldItems").Active=true;var c=new ExtraController(m,Layout(m),fs);
            c.Tick(false);int count=m.Writes;for(int i=0;i<30;i++)c.Tick(false);Require(m.Int(m.ModuleBase+0x541914)==0&&m.Writes==count&&m.Read(m.ModuleBase+0x9B2570,remove.Length).SequenceEqual(remove));
            c.Restore("fieldItems");Require(m.Read(m.ModuleBase+0x541912,11).SequenceEqual(original));
            m.Write(m.ModuleBase+0x541912,[0x90]);count=m.Writes;Reject(()=>c.Tick(false));Require(m.Writes==count);
        });
        check("Egg editor previews species, gene arrangement, stars and Rainbow Gene without writing",()=>
        {
            using var f=new EditorFixture();int writes=f.M.Writes;var eggs=f.Editor.Read(f.Catalog).Where(x=>x.Egg).ToArray();Require(eggs.Length==1);
            var genes=Enumerable.Repeat((1,0,false),9).ToArray();genes[0]=(17,0,false);genes[8]=(24,2,false);
            var p=StableEditor.Plan(eggs[0],2,genes,f.Catalog);Require(p.Length==3&&f.M.Writes==writes&&BitConverter.ToInt32(p[0].After)==2);
        });
        check("Editor applies a verified journal and backup, preserves metadata, and supports guarded undo",()=>
        {
            using var f=new EditorFixture();var s=f.Editor.Read(f.Catalog).Single(x=>x.Egg);var genes=Enumerable.Repeat((1,0,false),9).ToArray();genes[4]=(17,0,false);
            var plan=StableEditor.Plan(s,2,genes,f.Catalog);bool backed=false;var receipt=f.Editor.Apply(s,plan,()=>{Require(f.M.Read(s.Address,s.Snapshot.Length).SequenceEqual(s.Snapshot));backed=true;return new("fixture","fixture","now","1");},f.Temp);
            Require(backed&&File.ReadAllText(receipt.Journal).Contains("applied")&&f.M.Int(f.Egg)==2&&f.M.Byte(f.Egg+4)==2&&f.M.U16(f.Egg+24)==17);
            f.Editor.Undo(receipt);Require(f.M.Read(s.Address,s.Snapshot.Length).SequenceEqual(s.Snapshot));
        });
        check("Editor rejects changed eggs, battles, invalid genes and species changes on stable Monsties",()=>
        {
            using var f=new EditorFixture();var subjects=f.Editor.Read(f.Catalog);var egg=subjects.Single(x=>x.Egg);var buddy=subjects.Single(x=>!x.Egg);var genes=Enumerable.Repeat((1,0,false),9).ToArray();
            Reject(()=>StableEditor.Plan(buddy,2,genes,f.Catalog));genes[0]=(9999,0,false);Reject(()=>StableEditor.Plan(egg,1,genes,f.Catalog));genes[0]=(17,2,false);Reject(()=>StableEditor.Plan(egg,1,genes,f.Catalog));genes[0]=(17,0,false);
            var p=StableEditor.Plan(egg,2,genes,f.Catalog);f.M.Write(f.Egg+4,[1]);bool backed=false;Reject(()=>f.Editor.Apply(egg,p,()=>{backed=true;return new("","","","");},f.Temp));Require(!backed);
            f.Battle=true;Reject(()=>f.Editor.Read(f.Catalog));
        });
        check("Editor refuses undo after an object changes and never follows a replaced stable slot",()=>
        {
            using var f=new EditorFixture();var s=f.Editor.Read(f.Catalog).Single(x=>!x.Egg);var genes=Enumerable.Repeat((1,0,false),9).ToArray();genes[0]=(24,2,false);var plan=StableEditor.Plan(s,1,genes,f.Catalog);
            var receipt=f.Editor.Apply(s,plan,()=>new("","","",""),f.Temp);f.M.Put(f.Array,f.M.DataBase+0x1D0000);int writes=f.M.Writes;Reject(()=>f.Editor.Undo(receipt));Require(f.M.Writes==writes);
        });
        check("Editor rollback repairs a partial edit and keeps a readable failure journal",()=>
        {
            using var f=new EditorFixture();var wrapper=new FailingMemory(f.M);var editor=new StableEditor(wrapper,Layout(f.M),()=>false);var s=editor.Read(f.Catalog).Single(x=>x.Egg);var genes=Enumerable.Repeat((1,0,false),9).ToArray();genes[0]=(17,0,false);
            var plan=StableEditor.Plan(s,2,genes,f.Catalog);wrapper.FailAddress=plan[1].Address;
            Reject(()=>editor.Apply(s,plan,()=>new("fixture","fixture","",""),f.Temp));Require(f.M.Read(s.Address,s.Snapshot.Length).SequenceEqual(s.Snapshot));
            using var journal=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(f.Temp).Single()));Require(journal.RootElement.GetProperty("state").GetString()!.StartsWith("failed"));
        });
        check("Reconnecting invalidates old editor previews even if object bytes and addresses match",()=>
        {
            using var f=new EditorFixture();var s=f.Editor.Read(f.Catalog).Single(x=>x.Egg);var genes=Enumerable.Repeat((1,0,false),9).ToArray();genes[0]=(17,0,false);var plan=StableEditor.Plan(s,2,genes,f.Catalog);
            var newConnection=new StableEditor(f.M,Layout(f.M),()=>false);bool backed=false;int writes=f.M.Writes;
            Reject(()=>newConnection.Apply(s,plan,()=>{backed=true;return new("","","","");},f.Temp));Require(!backed&&f.M.Writes==writes);
        });
        check("Workshop archive rebuild keeps rainbow eggs, zeros costs only, and lowers gene requirements",()=>
        {
            var game=SteamInstallation.Find()??throw new Exception("Game installation required for read-only catalog check");string path=GameDataMods.ArchivePath(game.GameDirectory),before=Storage.Hash(path);
            byte[] original=GameDataMods.Original(game.GameDirectory);var output=GameDataMods.Build(original,new(4,4,true,true,true,true));var arc=new GameArchive(output.Archive);var baseline=new GameArchive(original);
            foreach(string name in new[]{@"table\item_mix",@"table\wep_param",@"table\armor_param",@"table\gene_table"})
            {
                var old=new XfsTable(baseline.Entries.Single(e=>e.Name==name).Extract());var edited=new XfsTable(arc.Entries.Single(e=>e.Name==name).Extract());Require(old.AllRows.Count==edited.AllRows.Count);
                var zero=name==@"table\item_mix"?new[]{"mItemNum[0]","mItemNum[1]","mItemNum[2]"}:name==@"table\gene_table"?["mOpenLevel"]:["mForgeDefaultPoint[0]","mForgeDefaultPoint[1]","mDefaultForgePrice","mPrice"];
                for(int i=0;i<old.AllRows.Count;i++)foreach(var (key,field) in old.AllRows[i].Fields)
                {
                    var value=edited.AllRows[i].Fields[key].Value;
                    if(zero.Contains(key))Require(Convert.ToDouble(value)==(key=="mOpenLevel"?Math.Min(1,Convert.ToDouble(field.Value)):0));else Require(Equals(value,field.Value),"Unrelated recipe field changed: "+key);
                }
            }
            Require(Storage.Hash(path)==before);var catalog=new NativeCatalog(game.GameDirectory);Require(catalog.Species.Any(x=>x.Name=="Zamtrios")&&catalog.Genes.Single(x=>x.Id==17).Stars==0);
        });
    }
    sealed class FailingMemory(IProcessMemory inner):IProcessMemory
    {
        public long FailAddress;bool _failed;
        public bool IsAlive=>inner.IsAlive;
        public Region? Query(long a)=>inner.Query(a);
        public byte[] Read(long a,int n)=>inner.Read(a,n);
        public void Write(long a,byte[] b){if(a==FailAddress&&!_failed){_failed=true;throw new InvalidOperationException("Injected fixture failure");}inner.Write(a,b);}
        public IEnumerable<Region> Regions()=>inner.Regions();
        public void Dispose(){}
    }
    sealed class EditorFixture:IDisposable
    {
        public AutomaticTests.FixtureMemory M=new();public StableEditor Editor;public NativeCatalog Catalog=new([new(1,"Velocidrome"),new(2,"Rathian")],[new(1,"Empty",0),new(17,"Rainbow",0),new(24,"Gene",2)]);
        public bool Battle;public long Egg,Array;public string Temp=Path.Combine(Path.GetTempPath(),"riders-editor-test-"+Guid.NewGuid().ToString("N"));
        public EditorFixture()
        {
            long buddy=M.DataBase+0x180000;Array=M.DataBase+0x190000;M.Put(M.Save+0x9A5C8,1);M.Put(M.Save+0x9A5D8,Array);M.Put(Array,buddy);M.Put(buddy,M.ModuleBase+0x162DA48);M.Put(buddy+0x60,42);M.Put(buddy+0x64,1);M.Write(buddy+0x86,[10]);
            Egg=M.Inventory+0x10C64;M.Put(Egg,1);M.Write(Egg+4,[2]);
            for(int i=0;i<9;i++){M.Write(buddy+0x17C+i*4,[1,0,0,0]);M.Write(Egg+8+i*4,[1,0,0,0]);}
            Editor=new(M,Layout(M),()=>Battle);
        }
        public void Dispose(){M.Dispose();string full=Path.GetFullPath(Temp);if(Directory.GetParent(full)?.FullName==Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))&&Path.GetFileName(full).StartsWith("riders-editor-test-")&&Directory.Exists(full))Directory.Delete(full,true);}
    }
}
