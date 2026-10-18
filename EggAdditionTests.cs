using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace RidersToolkit;

internal static class EggAdditionTests
{
    static void Require(bool v,string message="Egg addition assertion failed"){if(!v)throw new Exception(message);}
    static void Reject(Action action){bool failed=false;try{action();}catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or IOException){failed=true;}Require(failed,"Expected the addition to be refused");}
    internal static void Run(Action<string,Action> check)
    {
        check("An empty carton accepts a first egg, backs up before every write, and restores carton state on undo",()=>
        {
            using var f=new Fixture();f.EmptyCarton();int writes=f.M.Writes;
            var p=f.Editor.PreviewAddEgg(null,2,f.Catalog);
            Require(p.Template is null&&p.Slot==0&&BitConverter.ToInt32(p.Egg)==2&&p.Egg[0x2C]==255&&BitConverter.ToUInt16(p.Egg,8)==24&&f.M.Writes==writes);
            bool backup=false;f.Memory.Observer=(a,b)=>{Require(backup);if(a==p.PresenceAddress)Require(f.M.Int(p.Target)==2,"Has-eggs state must be published after the egg");};
            var r=f.Editor.AddEgg(p,()=>{backup=true;return new("fixture","backup","","");},f.Temp);
            Require(f.M.Int(p.PresenceAddress)==0xA4&&f.M.Int(f.Manager+0xAC)==0&&f.Editor.Read(f.Catalog).Count(s=>s.Egg)==1);
            f.Memory.Observer=null;f.Editor.UndoAddedEgg(r);
            Require(f.M.Read(f.Carton,p.CartonBefore.Length).SequenceEqual(p.CartonBefore)&&f.M.Int(p.PresenceAddress)==0xA0);
        });
        check("Failed first-egg backup leaves carton and has-eggs state untouched",()=>
        {
            using var f=new Fixture();f.EmptyCarton();var p=f.Editor.PreviewAddEgg(null,2,f.Catalog);int writes=f.M.Writes;
            Reject(()=>f.Editor.AddEgg(p,()=>throw new IOException("Backup failed"),f.Temp));
            Require(f.M.Writes==writes&&f.M.Int(p.PresenceAddress)==0xA0&&f.M.Int(p.Target)==0);
        });
        check("First-egg state write failure rolls back the published egg without changing other flags",()=>
        {
            using var f=new Fixture();f.EmptyCarton();var p=f.Editor.PreviewAddEgg(null,2,f.Catalog);f.Memory.FailAddress=p.PresenceAddress;
            Reject(()=>f.Editor.AddEgg(p,()=>new("fixture","backup","",""),f.Temp));
            Require(f.M.Read(f.Carton,p.CartonBefore.Length).SequenceEqual(p.CartonBefore)&&f.M.Int(p.PresenceAddress)==0xA0);
        });
        check("First-egg preview cannot use missing starter data or a changed state manager",()=>
        {
            using var f=new Fixture();f.EmptyCarton();var missing=new NativeCatalog(f.Catalog.Species,f.Catalog.Genes);
            Reject(()=>f.Editor.PreviewAddEgg(null,2,missing));var p=f.Editor.PreviewAddEgg(null,2,f.Catalog);
            f.M.Put(f.Layout.ModuleBase+0x1DEC820,0L);int writes=f.M.Writes;bool backup=false;
            Reject(()=>f.Editor.AddEgg(p,()=>{backup=true;return new("fixture","backup","","");},f.Temp));Require(!backup&&f.M.Writes==writes);
        });
        check("First-egg flags are rechecked after disk backup and never clobber a concurrent change",()=>
        {
            using var f=new Fixture();f.EmptyCarton();var p=f.Editor.PreviewAddEgg(null,2,f.Catalog);int ownWrites=0;f.Memory.Observer=(_,_)=>ownWrites++;
            Reject(()=>f.Editor.AddEgg(p,()=>{f.M.Put(p.PresenceAddress,0xA8);return new("fixture","backup","","");},f.Temp));
            Require(ownWrites==0&&f.M.Int(p.PresenceAddress)==0xA8&&f.M.Int(p.Target)==0);
        });
        check("Undo first egg refuses a subsequent state change without clearing unrelated flags",()=>
        {
            using var f=new Fixture();f.EmptyCarton();var p=f.Editor.PreviewAddEgg(null,2,f.Catalog);var r=f.Editor.AddEgg(p,()=>new("fixture","backup","",""),f.Temp);
            f.M.Put(p.PresenceAddress,0xAC);int writes=f.M.Writes;Reject(()=>f.Editor.UndoAddedEgg(r));Require(f.M.Writes==writes&&f.M.Int(p.Target)==2&&f.M.Int(p.PresenceAddress)==0xAC);
        });
        check("Add egg previews a hatchable species in an empty slot without writing",()=>
        {
            using var f=new Fixture();int writes=f.M.Writes;var p=f.Editor.PreviewAddEgg(f.Source,2,f.Catalog);
            Require(p.Slot==1&&p.Capacity==12&&p.SpeciesName=="Rathian"&&BitConverter.ToInt32(p.Egg)==2&&f.M.Writes==writes);
            Require(p.Egg.AsSpan(4).SequenceEqual(f.Source.Snapshot.AsSpan(4))&&f.M.Int(p.Target)==0);
        });
        check("Add egg backs up first, commits species last, preserves other eggs and can undo",()=>
        {
            using var f=new Fixture();var p=f.Editor.PreviewAddEgg(f.Source,2,f.Catalog);byte[] before=f.M.Read(p.Carton,p.CartonBefore.Length);bool backup=false;
            f.Memory.Observer=(a,b)=>{Require(backup,"Memory write happened before backup");if(a==p.Target)Require(f.M.Read(p.Target+4,StableEditor.EggBytes-4).SequenceEqual(p.Egg.AsSpan(4).ToArray()),"Species was published before metadata");};
            var r=f.Editor.AddEgg(p,()=>{Require(f.M.Read(p.Carton,before.Length).SequenceEqual(before));backup=true;return new("fixture","backup","","");},f.Temp);
            Require(backup&&f.M.Read(p.Target,StableEditor.EggBytes).SequenceEqual(p.Egg));Require(f.M.Read(f.Source.Address,StableEditor.EggBytes).SequenceEqual(f.Source.Snapshot));
            var expected=(byte[])before.Clone();p.Egg.CopyTo(expected,p.Slot*StableEditor.EggBytes);Require(f.M.Read(p.Carton,before.Length).SequenceEqual(expected));
            f.Memory.Observer=null;f.Editor.UndoAddedEgg(r);Require(f.M.Read(p.Carton,before.Length).SequenceEqual(before));
            using var journal=JsonDocument.Parse(File.ReadAllText(r.Journal));Require(journal.RootElement.GetProperty("state").GetString()=="undone");
        });
        check("Each egg addition makes a new backup and fills the next actual empty slot",()=>
        {
            using var f=new Fixture();int backups=0;BackupInfo Backup(){backups++;return new("fixture","backup"+backups,"","");}
            var first=f.Editor.PreviewAddEgg(f.Source,2,f.Catalog);var r=f.Editor.AddEgg(first,Backup,f.Temp);var source=f.Editor.Read(f.Catalog).First(s=>s.Egg);
            var next=f.Editor.PreviewAddEgg(source,1,f.Catalog);Require(next.Slot==2);f.Editor.AddEgg(next,Backup,f.Temp);Require(backups==2&&Directory.GetFiles(f.Temp).Length==2);
        });
        check("Full cartons and the native reserved story place reject additions",()=>
        {
            using var f=new Fixture();for(int i=1;i<12;i++)f.M.Write(f.Carton+i*StableEditor.EggBytes,f.Source.Snapshot);
            Reject(()=>f.Editor.PreviewAddEgg(f.Source,2,f.Catalog));Reject(()=>f.Editor.PreviewAddEgg(null,2,f.Catalog));f.M.Put(f.Carton+11*StableEditor.EggBytes,0);
            f.M.Put(f.Manager+0xAC,1<<7);Reject(()=>f.Editor.PreviewAddEgg(f.Source,2,f.Catalog));Reject(()=>f.Editor.PreviewAddEgg(null,2,f.Catalog));
            f.M.Put(f.Manager+0xAC,0);Require(f.Editor.PreviewAddEgg(f.Source,2,f.Catalog).Slot==11);
        });
        check("A failed backup, changed source, changed destination or reconnect prevents all addition writes",()=>
        {
            using var f=new Fixture();var p=f.Editor.PreviewAddEgg(f.Source,2,f.Catalog);int writes=f.M.Writes;
            Reject(()=>f.Editor.AddEgg(p,()=>throw new IOException("Backup failed"),f.Temp));Require(f.M.Writes==writes);
            f.M.Put(p.Target,1);writes=f.M.Writes;bool backup=false;Reject(()=>f.Editor.AddEgg(p,()=>{backup=true;return new("","","","");},f.Temp));Require(!backup&&f.M.Writes==writes);f.M.Put(p.Target,0);
            f.M.Write(f.Source.Address+4,[1]);writes=f.M.Writes;Reject(()=>f.Editor.AddEgg(p,()=>new("","","",""),f.Temp));Require(f.M.Writes==writes);
            var other=new StableEditor(f.Memory,f.Layout,()=>false);Reject(()=>other.AddEgg(p,()=>new("","","",""),f.Temp));
        });
        check("Missing egg source, missing capacity manager, changed routines and battle context fail closed",()=>
        {
            using var f=new Fixture();Reject(()=>f.Editor.PreviewAddEgg(f.Editor.Read(f.Catalog).Single(s=>!s.Egg),2,f.Catalog));Reject(()=>f.Editor.PreviewAddEgg(f.Source,99999,f.Catalog));
            f.M.Put(f.Layout.ModuleBase+0x1DED820,0L);Reject(()=>f.Editor.PreviewAddEgg(f.Source,2,f.Catalog));f.M.Put(f.Layout.ModuleBase+0x1DED820,f.Manager);
            var code=StableEditor.EggAddSamples[0];f.M.Write(f.Layout.ModuleBase+code.Rva,[0x90]);Reject(()=>f.Editor.PreviewAddEgg(f.Source,2,f.Catalog));f.M.Write(f.Layout.ModuleBase+code.Rva,Convert.FromHexString(code.Hex));
            f.Battle=true;Reject(()=>f.Editor.PreviewAddEgg(f.Source,2,f.Catalog));f.Battle=false;f.M.Put(f.Source.Address,0);Reject(()=>f.Editor.PreviewAddEgg(f.Source,2,f.Catalog));
        });
        check("Failed metadata staging rolls back the empty entry without publishing an egg",()=>
        {
            using var f=new Fixture();var p=f.Editor.PreviewAddEgg(f.Source,2,f.Catalog);f.Memory.FailAddress=p.Target+12;
            Reject(()=>f.Editor.AddEgg(p,()=>new("fixture","backup","",""),f.Temp));Require(f.M.Read(p.Carton,p.CartonBefore.Length).SequenceEqual(p.CartonBefore));
            using var journal=JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(f.Temp).Single()));Require(journal.RootElement.GetProperty("state").GetString()!.Contains("restored"));
        });
        check("Undo added egg refuses a changed carton and never overwrites a hatched or replaced egg",()=>
        {
            using var f=new Fixture();var p=f.Editor.PreviewAddEgg(f.Source,2,f.Catalog);var r=f.Editor.AddEgg(p,()=>new("","backup","",""),f.Temp);
            f.M.Put(p.Target,0);int writes=f.M.Writes;Reject(()=>f.Editor.UndoAddedEgg(r));Require(f.M.Writes==writes);
        });
        check("A battle starting while the UI state is stale blocks addition before backup",()=>
        {
            using var f=new Fixture();var p=f.Editor.PreviewAddEgg(f.Source,2,f.Catalog);f.M.LoadBattleAndSave();bool backed=false;int writes=f.M.Writes;
            Reject(()=>f.Editor.AddEgg(p,()=>{backed=true;return new("","backup","","");},f.Temp));Require(!backed&&f.M.Writes==writes);
        });
        check("Carton and template identity are rechecked after a successful disk backup",()=>
        {
            using var f=new Fixture();var p=f.Editor.PreviewAddEgg(f.Source,2,f.Catalog);int ownWrites=0;f.Memory.Observer=(_,_)=>ownWrites++;
            Reject(()=>f.Editor.AddEgg(p,()=>{f.M.Put(p.Target,1);return new("","backup","","");},f.Temp));Require(ownWrites==0&&f.M.Int(p.Target)==1);
        });
    }
    internal static bool EmptyCartonStudioReady(Window owner)
    {
        using var f=new Fixture();f.EmptyCarton();int writes=f.M.Writes;
        var studio=new StableEditorWindow(owner,()=>f.Editor,f.Catalog);studio.Show();
        try
        {
            Button Find(Window window,string label)=>MainWindow.VisualChildren<Button>(window).Single(b=>Equals(b.Content,label));
            void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Click(Find(studio,"Refresh owned"));var add=Find(studio,"Add egg…");if(!add.IsEnabled)return false;
            Click(add);var dialog=studio.OwnedWindows.OfType<AddEggWindow>().Single();
            Click(Find(dialog,"Preview egg"));
            return Find(dialog,"Add egg").IsEnabled&&MainWindow.VisualChildren<TextBlock>(dialog).Any(t=>t.Text.Contains("Carton: 0/12 → 1/12"))&&f.M.Writes==writes;
        }
        finally{foreach(var child in studio.OwnedWindows.OfType<Window>().ToArray())child.Close();studio.Close();}
    }
    sealed class TrackingMemory(IProcessMemory inner):IProcessMemory
    {
        public Action<long,byte[]>? Observer;public long FailAddress;bool _failed;
        public bool IsAlive=>inner.IsAlive;public Region? Query(long a)=>inner.Query(a);public byte[] Read(long a,int n)=>inner.Read(a,n);
        public void Write(long a,byte[] b){if(a==FailAddress&&!_failed){_failed=true;throw new IOException("Injected staging failure");}Observer?.Invoke(a,b);Require(b.Length<=8);inner.Write(a,b);}
        public IEnumerable<Region> Regions()=>inner.Regions();public void Dispose(){}
    }
    sealed class Fixture:IDisposable
    {
        public AutomaticTests.FixtureMemory M=new();public TrackingMemory Memory;public AutomaticLayout Layout;public StableEditor Editor;public NativeCatalog Catalog=new([new(1,"Velocidrome"),new(2,"Rathian")],[new(1,"Empty",0),new(17,"Rainbow",0),new(24,"Gene",2)]);
        public EditorSubject Source;public long Manager,PresenceManager,Carton;public bool Battle;
        public string Temp=Path.Combine(Path.GetTempPath(),"kinship-egg-test-"+Guid.NewGuid().ToString("N"));
        public Fixture()
        {
            Layout=M.Layout() with{ModuleSize=0x5226000,ExtrasSupported=true};foreach(var (r,h) in StableEditor.EggAddSamples)M.Write(Layout.ModuleBase+r,Convert.FromHexString(h));
            long buddy=M.DataBase+0x180000,array=M.DataBase+0x190000;Manager=M.DataBase+0x1C0000;PresenceManager=M.DataBase+0x1D0000;
            M.Put(M.Save+0x9A5C8,1);M.Put(M.Save+0x9A5D8,array);M.Put(array,buddy);M.Put(buddy,M.ModuleBase+0x162DA48);M.Put(buddy+0x60,42);M.Put(buddy+0x64,1);M.Write(buddy+0x86,[10]);
            M.Put(Layout.ModuleBase+0x1DED820,Manager);M.Put(Manager,Layout.ModuleBase+0x1000);M.Put(Manager+0xAC,0);
            M.Put(Layout.ModuleBase+0x1DEC820,PresenceManager);M.Put(PresenceManager,Layout.ModuleBase+0x1100);M.Put(PresenceManager+0xAC,4);
            var starter=new byte[StableEditor.EggBytes];starter[0x2C]=255;for(int i=0;i<9;i++)BitConverter.GetBytes((ushort)(i==0?24:1)).CopyTo(starter,8+i*4);
            Catalog=new(Catalog.Species,Catalog.Genes,new(){[1]=starter,[2]=starter});
            Carton=M.Inventory+0x10C64;M.Put(Carton,1);M.Put(Carton+4,2);M.Write(Carton+0x2C,[0xFF]);M.Put(Carton+0x30,123456);
            for(int i=0;i<9;i++){M.Write(buddy+0x17C+i*4,[1,0,0,0]);M.Write(Carton+8+i*4,[1,0,0,0]);}
            Memory=new(M);Editor=new(Memory,Layout,()=>Battle);Source=Editor.Read(Catalog).Single(s=>s.Egg);
        }
        public void EmptyCarton(){M.Write(Carton,new byte[StableEditor.EggBytes*StableEditor.CartonSlots]);M.Put(PresenceManager+0xAC,0xA0);}
        public void Dispose(){string full=Path.GetFullPath(Temp);if(Directory.GetParent(full)?.FullName==Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))&&Path.GetFileName(full).StartsWith("kinship-egg-test-")&&Directory.Exists(full))Directory.Delete(full,true);M.Dispose();}
    }
}
