namespace RidersToolkit;

internal static class ExplorationTests
{
    internal static void Run(Action<string,Action> check)
    {
        void Require(bool v,string message="Exploration assertion failed"){if(!v)throw new Exception(message);}
        check("Field radar, fourfold range and selective auto-gather use native structures",()=>
        {
            using var f=new FieldFixture();var controls=Catalog.Create().ToArray();var radar=controls.Single(x=>x.Id=="radar");var range=controls.Single(x=>x.Id=="gatherRange");var collect=controls.Single(x=>x.Id=="autoGather");
            radar.Active=range.Active=collect.Active=true;var engine=new ExplorationController(f.Memory,f.Layout,controls);engine.Tick(false,true);
            Require(f.Memory.Byte(f.Node+0x27D9)==1&&BitConverter.ToSingle(f.Memory.Read(f.Sphere+0x2C,4))==80);
            Require((uint)f.Memory.Int(f.Input+0x12B8)==0x80000000&&engine.GatherRequests==1);
            engine.Restore();Require(f.Memory.Byte(f.Node+0x27D9)==0&&BitConverter.ToSingle(f.Memory.Read(f.Sphere+0x2C,4))==20&&f.Memory.Int(f.Input+0x12B8)==0);
        });
        check("Auto-gather excludes chests, eggs, missing focus and battle",()=>
        {
            using var f=new FieldFixture();var controls=Catalog.Create().ToArray();controls.Single(x=>x.Id=="autoGather").Active=true;
            var engine=new ExplorationController(f.Memory,f.Layout,controls);
            engine.Tick(false,false);Require(f.Memory.Writes==0);
            f.Memory.Put(f.Row+0x2C,3);engine.Tick(false,true);Require(f.Memory.Writes==0);
            f.Memory.Put(f.Row+0x2C,2);f.Memory.Put(f.Row+0x30,12);engine.Tick(false,true);Require(f.Memory.Writes==0);
            f.Memory.Put(f.Row+0x30,9);engine.Tick(true,true);Require(f.Memory.Writes==0);
            f.Memory.Put(f.Ctrl+0x48,0);engine.Tick(false,true);Require(f.Memory.Writes==0);
        });
        check("Field restoration never edits a recycled actor or sphere",()=>
        {
            using var f=new FieldFixture();var controls=Catalog.Create().ToArray();controls.Single(x=>x.Id=="gatherRange").Active=true;
            var engine=new ExplorationController(f.Memory,f.Layout,controls);engine.Tick(false,true);
            f.Memory.Put(f.Node+0x2770,0L);int before=f.Memory.Writes;engine.Restore();Require(f.Memory.Writes==before);
        });
        check("Auto-gather resumes with several nearby interactions and still skips a selected chest",()=>
        {
            using var f=new FieldFixture();var controls=Catalog.Create().ToArray();controls.Single(x=>x.Id=="autoGather").Active=true;
            var engine=new ExplorationController(f.Memory,f.Layout,controls);
            f.Memory.Put(f.Ctrl+0x48,3);engine.Tick(false,true);Require(engine.GatherRequests==1);
            long selected=BitConverter.ToInt64(f.Memory.Read(f.Ctrl+0x58,8)),affordance=BitConverter.ToInt64(f.Memory.Read(selected,8));
            long chest=f.Memory.DataBase+0x1A0000;f.Memory.Put(chest,f.Memory.ModuleBase+0x15D0288);f.Memory.Put(affordance+0x28,chest);engine.Tick(false,true);Require(engine.GatherRequests==1);
            f.Memory.Put(affordance+0x28,f.Node);engine.Tick(false,true);Require(engine.GatherRequests==2);
        });
        check("Auto-gather releases its request during every dialogue and resumes with retained message IDs",()=>
        {
            using var f=new FieldFixture();var controls=Catalog.Create().ToArray();controls.Single(x=>x.Id=="autoGather").Active=true;
            var c=new ExplorationController(f.Memory,f.Layout,controls);c.Tick(false,true);int requests=1;
            for(int i=0;i<3;i++)
            {
                long message=f.Memory.DataBase+0x1B0000+i*0x100,entry=f.Ctrl+0xDA90+i*16;
                f.Memory.Put(entry,27+i);f.Memory.Put(entry+8,message);f.Memory.Put(message,f.Memory.ModuleBase+0x1000);f.Memory.Put(message+0x9C,0);
                f.Memory.Put(f.Input+0x12B8,unchecked((int)0x80000010));c.Tick(false,true);
                Require(c.GatherRequests==requests&&c.LastGatherStatus=="paused for dialogue"&&f.Memory.Int(f.Input+0x12B8)==0x10);
                f.Memory.Put(message+0x9C,new[]{0x10,0x40,1}[i]);c.Tick(false,true);
                Require(c.GatherRequests==++requests&&f.Memory.Int(entry)==27+i,"Completed dialog IDs must not permanently stop gathering");
            }
        });
        check("Auto-gather clears only its own request after losing focus and leaves modal controls untouched",()=>
        {
            using var f=new FieldFixture();var controls=Catalog.Create().ToArray();controls.Single(x=>x.Id=="autoGather").Active=true;
            var c=new ExplorationController(f.Memory,f.Layout,controls);c.Tick(false,true);f.Memory.Put(f.Input+0x12B8,unchecked((int)0x80000004));
            c.Tick(false,false);Require(f.Memory.Int(f.Input+0x12B8)==4&&c.GatherRequests==1);
            f.Memory.Put(f.Input+0x1260,(ushort)0);c.Tick(false,true);Require(c.GatherRequests==1&&f.Memory.U16(f.Input+0x1260)==0);
            f.Memory.Put(f.Input+0x1260,(ushort)0x100);c.Tick(false,true);Require(c.GatherRequests==2);
        });
        check("Pending gather release never writes through a recycled input owner",()=>
        {
            using var f=new FieldFixture();var controls=Catalog.Create().ToArray();controls.Single(x=>x.Id=="autoGather").Active=true;
            var c=new ExplorationController(f.Memory,f.Layout,controls);c.Tick(false,true);f.Memory.Put(f.Layout.ModuleBase+0x1EA3030,0L);int writes=f.Memory.Writes;
            c.Tick(false,false);Require(f.Memory.Writes==writes);c.Restore();Require(f.Memory.Writes==writes);
        });
        check("Fourfold gathering never compounds and restores the original radius",()=>
        {
            using var f=new FieldFixture();var controls=Catalog.Create().ToArray();controls.Single(x=>x.Id=="gatherRange").Active=true;
            var engine=new ExplorationController(f.Memory,f.Layout,controls);for(int i=0;i<31;i++)engine.Tick(false,true);
            Require(BitConverter.ToSingle(f.Memory.Read(f.Sphere+0x2C,4))==80);engine.Restore();Require(BitConverter.ToSingle(f.Memory.Read(f.Sphere+0x2C,4))==20);
        });
    }
    internal sealed class FieldFixture : IDisposable
    {
        public AutomaticTests.FixtureMemory Memory=new();
        public AutomaticLayout Layout;
        public long Node=>Memory.DataBase+0x30000;public long Sphere=>Memory.DataBase+0x38000;
        public long Input=>Memory.DataBase+0x50000;public long Ctrl=>Memory.DataBase+0x60000;public long Row=>Memory.DataBase+0x70000;
        public FieldFixture()
        {
            var m=Memory;long mb=m.ModuleBase,db=m.DataBase;Layout=m.Layout() with {ModuleSize=0x5226000,FieldSupported=true};
            void Obj(long a,long vt=0x1000)=>m.Put(a,mb+vt);
            long player=db+0x80000,state=db+0x90000,body=db+0xA0000,manager=db+0xB0000,list=db+0xC0000,entry=db+0xD0000,group=db+0xE0000,slots=db+0xF0000,slot=db+0x110000,package=db+0x120000,objects=db+0x130000,wrapper=db+0x140000,gm=db+0x150000,resource=db+0x160000,rows=db+0x170000,selected=db+0x180000,affordance=db+0x190000;
            m.Put(m.Save+0x33398,player);Obj(player);m.Put(player+0x4888,Ctrl);Obj(Ctrl);m.Put(Ctrl+0xD968,state);Obj(state);m.Put(Ctrl+0xD918,body);Obj(body);
            m.Put(mb+0x1D386C0,manager);Obj(manager);m.Put(manager+0x100,list);Obj(list);m.Put(list+0x10,entry);m.Put(list+0x18,entry+40);m.Put(entry+0x20,group);Obj(group);
            m.Put(group+0x2D0,1);m.Put(group+0x2E0,slots);m.Put(slots,slot);Obj(slot);m.Put(slot+0x18,package);Obj(package,0x2000);m.Put(mb+0x2080,mb+0x49B1D0);
            m.Put(package+0xE8,1);m.Put(package+0xF8,objects);m.Put(objects,wrapper);Obj(wrapper,0x3000);m.Put(mb+0x3028,mb+0x3A48A0);m.Put(wrapper+0x298,Node);Obj(Node,0x15F2528);
            m.Put(Node+0x2770,Sphere);Obj(Sphere,0x1600040);m.Put(Sphere+0x2C,BitConverter.SingleToInt32Bits(20));
            m.Put(mb+0x1DEBC10,gm);Obj(gm);m.Put(gm+0x1E0,resource);Obj(resource);m.Put(resource+0xD0,1);m.Put(resource+0xE0,rows);m.Put(rows,Row);Obj(Row);m.Put(Row+0x2C,2);m.Put(Row+0x30,9);
            m.Put(Ctrl+0x48,1);m.Put(Ctrl+0x58,selected);m.Put(selected,affordance);Obj(affordance,0x1601B60);m.Put(affordance+0x24,4);m.Put(affordance+0x28,Node);m.Put(state+0x270,(ushort)1);
            m.Put(mb+0x1EA3030,Input);Obj(Input);m.Put(Input+0x1260,(ushort)0x100);m.Put(Input+0xC68,(ushort)1);
        }
        public void Dispose()=>Memory.Dispose();
    }
}
