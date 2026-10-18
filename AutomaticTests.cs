using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace RidersToolkit;

internal static class AutomaticTests
{
    public static int TestLiveExploration()
    {
        Storage.Discover();string report=Path.Combine(Storage.Root,"live-exploration-report.json");
        try
        {
            var game=Storage.InstalledGame??throw new InvalidOperationException("Game not found");Process? selected=null;
            foreach(var p in Process.GetProcessesByName("game"))
            {if(string.Equals(p.MainModule?.FileName,game.Executable,StringComparison.OrdinalIgnoreCase)){selected=p;break;}p.Dispose();}
            if(selected is null)throw new InvalidOperationException("Game not running");
            var connection=AutomaticConnection.Open(selected,game.GameDirectory);
            using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.Attach(connection);
            var controls=engine.Features.Where(f=>f.Id is "radar" or "gatherRange" or "autoGather").ToArray();
            if(!connection.Layout.FieldSupported)throw new InvalidOperationException("Field proofs did not match");
            var wait=Stopwatch.StartNew();
            while(wait.ElapsedMilliseconds<180000)
            {
                Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out uint foreground);
                if(foreground==selected.Id)break;
                if((Native.GetAsyncKeyState(0x7B)&0x8000)!=0)throw new InvalidOperationException("Test cancelled before activation");
                File.WriteAllText(report,JsonSerializer.Serialize(new{Testing=false,WaitingFor="Stories 2 game focus",MemoryWrites=0}));Thread.Sleep(100);
            }
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out uint focused);
            if(focused!=selected.Id)throw new InvalidOperationException("Game was not brought to foreground. No controls activated.");
            var backup=Storage.Backup();foreach(var f in controls)engine.SetActive(f,true);
            var watch=Stopwatch.StartNew();
            while(watch.ElapsedMilliseconds<30000&&engine.Memory is not null)
            {
                if((Native.GetAsyncKeyState(0x7B)&0x8000)!=0)break;
                engine.Tick();
                File.WriteAllText(report,JsonSerializer.Serialize(new{Testing=true,Seconds=(int)watch.Elapsed.TotalSeconds,Backup=backup.Path,Controls=controls.Select(f=>new{f.Id,f.Active,f.Bound,f.Current,f.Waiting}),engine.Exploration?.GatherRequests,engine.Exploration?.Revealed,engine.Exploration?.Extended,engine.Exploration?.LastGatherStatus,connection.Memory.WriteOperations},new JsonSerializerOptions{WriteIndented=true}));
                Thread.Sleep(50);
            }
            var exploration=engine.Exploration;engine.StopAll();
            File.WriteAllText(report,JsonSerializer.Serialize(new{Completed=true,Backup=backup.Path,exploration?.GatherRequests,exploration?.Revealed,exploration?.Extended,exploration?.LastGatherStatus,connection.Memory.WriteOperations,AllSwitchesOff=engine.Features.All(f=>!f.Active),VisibleEffectNeedsUserConfirmation=true},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{Completed=false,Error=ex.ToString()}));return 1;}
    }
    internal static void Run(Action<string,Action> check)
    {
        void Require(bool value,string message="Automatic engine assertion failed") {if(!value)throw new Exception(message);}
        check("Automatic signatures reject missing and ambiguous routines",()=>
        {
            using var f=new FixtureMemory();var layout=f.Layout();Require(layout.ParameterOffset==0xB0&&layout.HeartsOffset==0x3E58&&layout.InventoryOffset==0x23AE80);
            byte original=f.Code[0x100];f.Code[0x100]^=1;bool rejected=false;try{f.Layout();}catch(InvalidDataException){rejected=true;}Require(rejected);f.Code[0x100]=original;
            f.Code.AsSpan(0x100,41).CopyTo(f.Code.AsSpan(0x1100));rejected=false;try{f.Layout();}catch(InvalidDataException){rejected=true;}Require(rejected);
        });
        check("Retreat changes enemy roll only and restores it on disable",()=>
        {
            using var f=new FixtureMemory();f.LoadBattleAndSave();f.LoadBuddy();long unit=f.DataBase+0xD0000,enemy=f.DataBase+0xE0000;
            f.Put(f.EnemyActor+0x20,unit);f.Put(unit,f.ModuleBase+0x1000);f.Put(unit+0xB0,enemy);f.Put(enemy,f.ModuleBase+0x1000);f.Put(enemy+0x1F48,(ushort)8);f.Put(unit+0x4548,f.DataBase+0xF0000);f.Put(f.DataBase+0xF0052,(ushort)0);
            long decoy=f.DataBase+0xB0000;f.Put(f.EnemyActor+0xB0,decoy);f.Put(decoy+0x1F48,(ushort)77);
            using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout() with {RetreatSupported=true},f.Items);
            var retreat=engine.Features.Single(x=>x.Id=="retreat");engine.SetActive(retreat,true);engine.Tick();
            Require(f.U16(enemy+0x1F48)==100&&f.Byte(f.DataBase+0xF0052)==0&&f.U16(f.Rider+0x1F48)==0&&f.U16(decoy+0x1F48)==77);
            engine.SetActive(retreat,false);Require(f.U16(enemy+0x1F48)==8);
        });
        check("Retreat follows both opposing controllers and all four unit slots",()=>
        {
            using var f=new FixtureMemory();f.LoadBattleAndSave();f.LoadBuddy();long second=f.DataBase+0xD0000,unit=f.DataBase+0xE0000,parameter=f.DataBase+0xF0000;
            f.Put(f.Battle+0xA00,second);f.Put(second,f.ModuleBase+0x1000);f.Put(second+0x38,unit);f.Put(unit,f.ModuleBase+0x1000);f.Put(unit+0xB0,parameter);f.Put(parameter,f.ModuleBase+0x1000);f.Put(parameter+0x1F48,(ushort)15);
            using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout() with {RetreatSupported=true},f.Items);
            var retreat=engine.Features.Single(x=>x.Id=="retreat");engine.SetActive(retreat,true);engine.Tick();Require(f.U16(parameter+0x1F48)==100);
            long replacement=f.DataBase+0x180000;f.Put(replacement,f.ModuleBase+0x1000);f.Put(replacement+0x1F48,(ushort)30);f.Put(unit+0xB0,replacement);f.Put(parameter+0x1F48,(ushort)55);engine.Tick();Require(f.U16(replacement+0x1F48)==100&&f.U16(parameter+0x1F48)==55);
            engine.StopAll();Require(f.U16(replacement+0x1F48)==30&&f.U16(parameter+0x1F48)==55);
        });
        check("Unmatched retreat and field routines stay unavailable",()=>
        {
            using var f=new FixtureMemory();using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout(),f.Items);
            foreach(var feature in engine.Features.Where(x=>x.Category=="Exploration"))Require(!feature.Available);
            Require(!ExplorationController.Ordinary(3,9)&&!ExplorationController.Ordinary(2,12)&&!ExplorationController.Ordinary(2,14));
        });
        check("All switches can arm at the title screen without writing anything",()=>
        {
            using var f=new FixtureMemory();using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout(),f.Items);
            foreach(var feature in engine.Features.Where(x=>x.Available))engine.SetActive(feature,true);for(int i=0;i<25;i++)engine.Tick();
            Require(f.Writes==0&&engine.Features.Where(x=>x.Available).All(x=>x.Active&&!x.Bound));
        });
        check("Thirteen controls resolve battle and inventory data without numeric searches",()=>
        {
            using var f=new FixtureMemory();using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout(),f.Items);
            var original=engine.Features.Where(x=>x.Category is "Battle" or "Inventory").Where(x=>x.Id!="battleExp"&&!ExtraController.Ids.Contains(x.Id)&&!AdventureController.Ids.Contains(x.Id)).ToArray();
            foreach(var feature in original)engine.SetActive(feature,true);f.LoadBattleAndSave();for(int i=0;i<12;i++)engine.Tick();
            Require(original.All(x=>x.Active&&x.Bound));
            Require(f.U16(f.Rider+0x18)==9999&&f.U16(f.Monstie+0x18)==9999);
            Require(f.Byte(f.Actor+0x3E58)==3&&f.Byte(f.Actor+0x3E9E)==100&&f.Byte(f.Monstie+0x1FF0)==100);
            Require(f.Int(f.Battle+0x237C)==1&&f.Int(f.Inventory)==999999);
            Require(f.U16(f.Inventory+6+497*8)==999);
            Require(f.U16(f.Inventory+6+1*8)==999&&f.U16(f.Inventory+6+2*8)==99&&f.U16(f.Inventory+6+3*8)==99);
            Require(f.U16(f.Inventory+6+4*8)==99&&f.U16(f.Inventory+6+5*8)==99);
            Require(f.U16(f.Inventory+6+6*8)==1&&f.U16(f.Inventory+6+7*8)==1,"Quest/key items changed");
            Require(f.U16(f.Inventory+6+8*8)==2&&f.U16(f.Inventory+6+9*8)==0,"Wrong-ID or unowned item was filled");
            Require(f.U16(f.Rider+0x54)==340&&f.U16(f.Monstie+0x54)==480);
        });
        check("Area and party changes automatically replace pointers and prevent stale writes",()=>
        {
            using var f=new FixtureMemory();f.LoadBattleAndSave();using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout(),f.Items);
            var hp=engine.Features.Single(x=>x.Id=="riderHp");engine.SetActive(hp,true);engine.Tick();
            long replacement=f.DataBase+0x60000;f.Put(replacement+0x18,(ushort)42);f.Put(f.RiderArray+0xB0,replacement);f.Put(f.Rider+0x18,(ushort)123);engine.Tick();
            Require(f.U16(replacement+0x18)==9999&&f.U16(f.Rider+0x18)==123);
            f.Put(f.Battle+0x9D0,0L);f.Put(replacement+0x18,(ushort)21);int before=f.Writes;engine.Tick();
            Require(f.Writes==before&&f.U16(replacement+0x18)==21&&hp.Active&&!hp.Bound);
        });
        check("Disable all stops health maintenance and restores attack after target edits",()=>
        {
            using var f=new FixtureMemory();f.LoadBattleAndSave();using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout(),f.Items);
            var damage=engine.Features.Single(x=>x.Id=="damage");var hp=engine.Features[0];engine.SetActive(damage,true);engine.SetActive(hp,true);engine.Tick();
            damage.Target="3.5";engine.Tick();Require(f.U16(f.Rider+0x54)==595);engine.StopAll();
            Require(f.U16(f.Rider+0x54)==170&&f.U16(f.Monstie+0x54)==240);
            f.Put(f.Rider+0x18,(ushort)25);engine.Tick();Require(f.U16(f.Rider+0x18)==25&&engine.Features.All(x=>!x.Active));
        });
        check("Kinship fills normal and mounted gauges and signals the game's change flag",()=>
        {
            using var f=new FixtureMemory();f.LoadBattleAndSave();using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout(),f.Items);
            engine.SetActive(engine.Features.Single(x=>x.Id=="kinship"),true);engine.Tick();
            Require(f.Byte(f.Monstie+0x1FF0)==100&&f.Byte(f.Actor+0x3E9E)==100&&(f.Byte(f.Actor+0x3E5C)&4)!=0);
        });
        check("Kinship also follows the allied battle buddy without editing the opposing side",()=>
        {
            using var f=new FixtureMemory();f.LoadBattleAndSave();f.LoadBuddy();using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout(),f.Items);
            engine.SetActive(engine.Features.Single(x=>x.Id=="kinship"),true);engine.Tick();
            Require(f.Byte(f.BuddyMonstie+0x1FF0)==100&&f.Byte(f.BuddyActor+0x3E9E)==100&&(f.Byte(f.BuddyActor+0x3E5C)&4)!=0);
            Require(f.Byte(f.EnemyActor+0x3E9E)==17,"Opposing party was edited");
        });
        check("Re-enabling an already full gauge signals an immediate refresh",()=>
        {
            using var f=new FixtureMemory();f.LoadBattleAndSave();f.Put(f.Monstie+0x1FF0,(ushort)100);using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout(),f.Items);
            engine.SetActive(engine.Features.Single(x=>x.Id=="kinship"),true);engine.Tick();Require((f.Byte(f.Actor+0x3E5C)&4)!=0);
        });
        check("Unloaded save data stops cached inventory controls immediately",()=>
        {
            using var f=new FixtureMemory();f.LoadBattleAndSave();using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout(),f.Items);
            var money=engine.Features.Single(x=>x.Id=="money");engine.SetActive(money,true);engine.Tick();
            f.Put(f.ModuleBase+0xF008,0L);int before=f.Writes;engine.Tick();Require(f.Writes==before&&!money.Bound&&money.Active&&engine.Memory is not null);
        });
        check("Changed game routines stop controls before further writes",()=>
        {
            using var f=new FixtureMemory();f.LoadBattleAndSave();using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout(),f.Items);
            engine.SetActive(engine.Features[0],true);for(int i=0;i<19;i++)engine.Tick();
            f.Code[0x100]^=1;int before=f.Writes;engine.Tick();Require(engine.Memory is null&&f.Writes==before&&engine.Features.All(x=>!x.Active&&!x.Available));
        });
        check("Item category metadata comes from the user's installed archive",()=>
        {
            var game=SteamInstallation.Find()??throw new Exception("Installed game needed");var items=AutomaticLayout.LoadItems(game.GameDirectory);
            Require(items.Count>1000);Require(items.Single(i=>i.Id==497) is {Category:3,Capacity:999});Require(Enumerable.Range(0,5).All(c=>items.Any(i=>i.Category==c)));
        });
    }
    public static int InspectInstalled()
    {
        Storage.Discover();
        try
        {
            var game=Storage.InstalledGame??throw new Exception("Steam game installation not found");Process? selected=null;
            foreach(var p in Process.GetProcessesByName("game"))
            {try{if(string.Equals(p.MainModule?.FileName,game.Executable,StringComparison.OrdinalIgnoreCase)){selected=p;break;}}catch{}p.Dispose();}
            if(selected is null)throw new Exception("Start Stories 2 and wait for its title screen.");
            var connection=AutomaticConnection.Open(selected,game.GameDirectory);
            using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.Attach(connection);for(int i=0;i<20;i++)engine.Tick();
            var report=new{ReadOnly=true,Pid=connection.Pid,ExecutableHash=Storage.Hash(game.Executable),MatchedCodeChecks=connection.Layout.Proofs.Count,AutomaticControls=engine.Features.Count,ReadyControls=engine.Features.Count(f=>f.Bound),BattleReady=engine.Features.Where(f=>f.Category=="Battle"&&f.Bound).Select(f=>f.Id).ToArray(),engine.BattleManagerLocated,engine.SaveManagerLocated,ActiveControls=engine.Features.Count(f=>f.Active),MemoryWrites=connection.Memory.WriteOperations,SaveLoadedByAgent=false,GameplayEffectsVerified=false};
            File.WriteAllText(Path.Combine(Storage.Root,"automatic-compatibility-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            return connection.Memory.WriteOperations==0?0:1;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(Storage.Root,"automatic-compatibility-report.json"),JsonSerializer.Serialize(new{Error=ex.Message,ReadOnly=true}));return 1;}
    }
    public static int TestLiveBattle()
    {
        Storage.Discover();
        string progress=Path.Combine(Storage.Root,"live-health-progress.json"),report=Path.Combine(Storage.Root,"live-health-report.json");
        void Progress(object state)=>File.WriteAllText(progress,JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
        try
        {
            var game=Storage.InstalledGame??throw new Exception("Steam game installation not found");Process? selected=null;
            foreach(var p in Process.GetProcessesByName("game"))
            {try{if(string.Equals(p.MainModule?.FileName,game.Executable,StringComparison.OrdinalIgnoreCase)){selected=p;break;}}catch{}p.Dispose();}
            if(selected is null)throw new Exception("Stories 2 is not running");
            var connection=AutomaticConnection.Open(selected,game.GameDirectory);
            using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.Attach(connection);
            string[] ids=["riderHp","monstieHp","kinship"];
            var tested=ids.Select(id=>engine.Features.Single(f=>f.Id==id)).ToArray();
            var until=DateTime.UtcNow.AddMinutes(2);Progress(new{Stage="Waiting for the user to enter a solo battle",MemoryWrites=0});
            while(tested.Any(f=>!f.Bound)&&DateTime.UtcNow<until)
            {engine.Tick();if(engine.Memory is null)throw new Exception("Game disconnected while waiting for battle");Thread.Sleep(100);}
            if(tested.Any(f=>!f.Bound))throw new Exception("No ready solo battle was found. No gameplay values were changed.");
            var backup=Storage.Backup();var before=tested.ToDictionary(f=>f.Id,f=>f.Current);
            foreach(var feature in tested)engine.SetActive(feature,true);
            Progress(new{Stage="Testing Rider health, Monstie health and Kinship for 30 seconds",Backup=backup.Path,Before=before});
            int observedHits=0,verifiedSamples=0;var watch=Stopwatch.StartNew();
            while(watch.Elapsed<TimeSpan.FromSeconds(30))
            {
                if((Native.GetAsyncKeyState(0x7B)&0x8000)!=0)break;
                engine.Tick();if(engine.Memory is null)throw new Exception("Automatic engine disconnected during the test");
                foreach(var feature in tested.Where(f=>f.Bound))
                {
                    if(watch.ElapsedMilliseconds>500&&feature.Id is "riderHp" or "monstieHp"&&feature.LastObserved<9999)observedHits++;
                    var bytes=engine.Memory.Read(feature.Address,Numbers.Width(feature.Kind));
                    if(bytes.Length==Numbers.Width(feature.Kind)&&Numbers.Decode(bytes,feature.Kind)==feature.CheckedTarget())verifiedSamples++;
                }
                Thread.Sleep(50);
            }
            engine.StopAll();
            var result=new{Completed=true,TestedControls=ids,Backup=backup.Path,Before=before,VerifiedSamples=verifiedSamples,ObservedHealthDrops=observedHits,MemoryWrites=connection.Memory.WriteOperations,AllSwitchesStopped=engine.Features.All(f=>!f.Active),InventoryOrMoneyEdited=false,SaveLoadedByAgent=false,ScreenEffectNeedsUserConfirmation=true};
            File.WriteAllText(report,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Progress(result);
            return verifiedSamples>0?0:1;
        }
        catch(Exception ex){var error=new{Completed=false,Error=ex.Message};File.WriteAllText(report,JsonSerializer.Serialize(error));Progress(error);return 1;}
    }
    internal sealed class FixtureMemory : IProcessMemory,IFieldItemCostMemory
    {
        public long ModuleBase=0x140000000,DataBase=0x20000000;
        public byte[] Code=new byte[65536],Data=new byte[8*1024*1024];
        readonly Dictionary<long,byte[]> _extraPages=[];
        public int Writes;
        public bool IsAlive=>true;
        public long Battle=>DataBase+0x10000;public long Actor=>DataBase+0x20000;public long RiderArray=>DataBase+0x30000;public long MonstieArray=>DataBase+0x31000;
        public long Rider=>DataBase+0x40000;public long Monstie=>DataBase+0x50000;public long Save=>DataBase+0x100000;public long Inventory=>Save+0x23AE80;
        public long BuddyActor=>DataBase+0x70000;public long BuddyMonstie=>DataBase+0x90000;public long EnemyActor=>DataBase+0xC0000;
        public IReadOnlyList<ItemDefinition> Items=[new(1,0,999,false),new(2,1,99,false),new(3,2,99,false),new(4,3,99,false),new(5,4,99,false),new(6,5,1,true),new(7,0,99,true),new(8,0,99,false),new(9,0,99,false),new(497,3,999,false)];
        public FixtureMemory()
        {
            void Pattern(int at,string text){AutomaticLayout.Pattern(text).Select(x=>x??(byte)0x11).ToArray().CopyTo(Code,at);}
            Pattern(0x100,AutomaticLayout.HealthPattern);BitConverter.GetBytes(0xB0).CopyTo(Code,0x113);BitConverter.GetBytes(0x1FE8).CopyTo(Code,0x11F);
            Pattern(0x200,AutomaticLayout.HeartsPattern);BitConverter.GetBytes(0x3E58).CopyTo(Code,0x203);
            Pattern(0x300,AutomaticLayout.BattlePattern);BitConverter.GetBytes(0xF000-0x307).CopyTo(Code,0x303);BitConverter.GetBytes(0xF000-0x314).CopyTo(Code,0x310);BitConverter.GetBytes(0x237C).CopyTo(Code,0x309);
            Pattern(0x400,AutomaticLayout.MoneyPattern);BitConverter.GetBytes(0xF008-0x407).CopyTo(Code,0x403);BitConverter.GetBytes(0x800-0x411).CopyTo(Code,0x40D);
            Pattern(0x500,AutomaticLayout.ItemsPattern);Pattern(0x600,AutomaticLayout.PartyPattern);
            Pattern(0x900,AutomaticLayout.GaugePattern);BitConverter.GetBytes(0xB0).CopyTo(Code,0x90C);BitConverter.GetBytes(0x1FF0).CopyTo(Code,0x918);
            Pattern(0xB00,AutomaticLayout.GaugeChangePattern);BitConverter.GetBytes(0x1FF0).CopyTo(Code,0xB03);BitConverter.GetBytes(0x3E5C).CopyTo(Code,0xB17);
            new byte[]{0x48,0x8D,0x81,0x80,0xAE,0x23,0x00,0xC3}.CopyTo(Code,0x800);
            Put(ModuleBase+0xF000,Battle);Put(ModuleBase+0xF008,Save);Put(Battle,ModuleBase+0x1000);Put(Save,ModuleBase+0x1000);
        }
        public AutomaticLayout Layout()=>AutomaticLayout.DetectImage(Code,ModuleBase);
        public void LoadBattleAndSave()
        {
            Put(Battle+0x9D0,Actor);Put(Actor,ModuleBase+0x1000);Put(Actor+0x40,RiderArray);Put(Actor+0x48,MonstieArray);
            Put(RiderArray+0xB0,Rider);Put(MonstieArray+0xB0,Monstie);Put(Rider+0x18,(ushort)70);Put(Monstie+0x18,(ushort)180);Put(Rider+0x54,(ushort)170);Put(Monstie+0x54,(ushort)240);
            foreach(var item in Items){Put(Inventory+4+item.Id*8,(ushort)item.Id);Put(Inventory+6+item.Id*8,(ushort)(item.Id==9?0:item.Id is 6 or 7?1:2));}
            Put(Inventory+4+8*8,(ushort)99);Put(Inventory,1200);
        }
        public void LoadBuddy()
        {
            Put(Battle+0x9D8,BuddyActor);Put(BuddyActor,ModuleBase+0x1000);Put(BuddyActor+0x48,DataBase+0xA1000);Put(DataBase+0xA10B0,BuddyMonstie);Put(BuddyMonstie+0x18,(ushort)50);
            Put(Battle+0x9F8,EnemyActor);Put(EnemyActor,ModuleBase+0x1000);Put(EnemyActor+0x3E9E,(ushort)17);
        }
        (byte[] Buffer,int Offset) Locate(long address,int size)
        {
            if(address>=ModuleBase&&address<=ModuleBase+Code.Length-size)return(Code,(int)(address-ModuleBase));
            if(address>=DataBase&&address<=DataBase+Data.Length-size)return(Data,(int)(address-DataBase));
            if(address>=ModuleBase&&address<=ModuleBase+0x5226000-size)
            {long page=address&~0xFFFL;if((address-page)+size>4096)throw new InvalidOperationException("Fixture page boundary");if(!_extraPages.TryGetValue(page,out var bytes))_extraPages[page]=bytes=new byte[4096];return(bytes,(int)(address-page));}
            throw new InvalidOperationException("Unmapped fixture memory");
        }
        public byte[] Read(long address,int size){var (b,o)=Locate(address,size);return b.AsSpan(o,size).ToArray();}
        public void Write(long address,byte[] value){var (b,o)=Locate(address,value.Length);value.CopyTo(b,o);Writes++;}
        public void Put(long address,long value){var (b,o)=Locate(address,8);BitConverter.GetBytes(value).CopyTo(b,o);}
        public void Put(long address,int value){var (b,o)=Locate(address,4);BitConverter.GetBytes(value).CopyTo(b,o);}
        public void Put(long address,ushort value){var (b,o)=Locate(address,2);BitConverter.GetBytes(value).CopyTo(b,o);}
        public ushort U16(long address)=>BitConverter.ToUInt16(Read(address,2));public int Int(long address)=>BitConverter.ToInt32(Read(address,4));public byte Byte(long address)=>Read(address,1)[0];
        public Region? Query(long address)=>address>=DataBase&&address<DataBase+Data.Length?new(DataBase,DataBase,Data.Length,4):null;
        public IEnumerable<Region> Regions(){yield return new(DataBase,DataBase,Data.Length,4);}
        bool _fieldFree;
        void IFieldItemCostMemory.SetFieldItemCost(long module,bool free)
        {
            byte[] code=Read(module+0x541912,11),expected=Convert.FromHexString(_fieldFree?"41B800000000E8530C4700":"41B801000000E8530C4700");
            if(!code.SequenceEqual(expected))throw new InvalidOperationException("Field fixture instruction changed");
            if(free!=_fieldFree){Write(module+0x541914,BitConverter.GetBytes(free?0:1));_fieldFree=free;}
        }
        public void Dispose(){}
    }
}
