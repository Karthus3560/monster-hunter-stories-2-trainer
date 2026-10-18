namespace RidersToolkit;

internal static class ModifierTests
{
    internal static void Run(Action<string,Action> check)
    {
        void Require(bool v){if(!v)throw new Exception("Modifier assertion failed");}
        check("Damage slider accepts fractions, never compounds and respects native stat updates",()=>
        {
            using var f=new AutomaticTests.FixtureMemory();f.LoadBattleAndSave();using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout(),f.Items);
            var feature=engine.Features.Single(x=>x.Id=="damage");feature.Target="2.5";engine.SetActive(feature,true);for(int i=0;i<25;i++)engine.Tick();
            Require(f.U16(f.Rider+0x54)==425&&f.U16(f.Monstie+0x54)==600);
            f.Put(f.Rider+0x54,(ushort)200);engine.Tick();Require(f.U16(f.Rider+0x54)==500);engine.SetActive(feature,false);Require(f.U16(f.Rider+0x54)==200);
        });
        check("EXP scales enemy rewards before level calculation, caps and restores without compounding",()=>
        {
            using var f=new AutomaticTests.FixtureMemory();f.LoadBattleAndSave();f.LoadBuddy();long unit=f.DataBase+0xD0000,parameter=f.DataBase+0xE0000;
            f.Put(f.EnemyActor+0x20,unit);f.Put(unit,f.ModuleBase+0x1000);f.Put(unit+0xB0,parameter);f.Put(parameter,f.ModuleBase+0x1000);f.Put(parameter+0x1FE8,150);
            using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout() with {ExperienceSupported=true},f.Items);
            var feature=engine.Features.Single(x=>x.Id=="battleExp");feature.Target="5";engine.SetActive(feature,true);for(int i=0;i<25;i++)engine.Tick();
            Require(feature.Bound&&f.Int(parameter+0x1FE8)==750);feature.Target="20";engine.Tick();Require(f.Int(parameter+0x1FE8)==3000);
            f.Put(parameter+0x1FE8,80000);engine.Tick();Require(f.Int(parameter+0x1FE8)==999999);engine.StopAll();Require(f.Int(parameter+0x1FE8)==80000);
        });
        check("EXP restoration skips replaced enemies and unmatched routines stay unavailable",()=>
        {
            using var f=new AutomaticTests.FixtureMemory();f.LoadBattleAndSave();f.LoadBuddy();long unit=f.DataBase+0xD0000,parameter=f.DataBase+0xE0000;
            f.Put(f.EnemyActor+0x20,unit);f.Put(unit,f.ModuleBase+0x1000);f.Put(unit+0xB0,parameter);f.Put(parameter,f.ModuleBase+0x1000);f.Put(parameter+0x1FE8,150);
            using var engine=new AutomaticTrainerEngine(Catalog.Create().ToArray());engine.AttachTest(f,f.Layout() with {ExperienceSupported=true},f.Items);var feature=engine.Features.Single(x=>x.Id=="battleExp");engine.SetActive(feature,true);engine.Tick();
            f.Put(unit+0xB0,0L);int writes=f.Writes;engine.StopAll();Require(f.Writes==writes);
        });
        check("Movement tables scale independently, preserve fractional speeds and restore originals",()=>
        {
            using var fixture=new MovementFixture();var f=fixture.Memory;var features=Catalog.Create().ToArray();var controller=new MovementController(f,fixture.Layout,features);
            var walk=features.Single(x=>x.Id=="walkSpeed");walk.Active=true;walk.Target="2.25";
            for(int i=0;i<30;i++)controller.Tick();Require(Math.Abs(fixture.Float(fixture.Walk+0x18)-2.475)<.00001&&fixture.Float(fixture.Mount+0x44)==1);
            var mount=features.Single(x=>x.Id=="mountSpeed");mount.Active=true;mount.Target="1.5";controller.Tick();Require(fixture.Float(fixture.Mount+0x44)==1.5&&fixture.Float(fixture.Water+0x3C)==.75);
            walk.Active=false;controller.Restore(walk.Id);Require(Math.Abs(fixture.Float(fixture.Walk+0x18)-1.1)<.00001&&fixture.Float(fixture.Mount+0x44)==1.5);
            controller.Restore();Require(fixture.Float(fixture.Mount+0x44)==1);
        });
        check("Movement restoration validates table, row and vtable guards",()=>
        {
            using var fixture=new MovementFixture();var features=Catalog.Create().ToArray();features.Single(x=>x.Id=="walkSpeed").Active=true;
            var controller=new MovementController(fixture.Memory,fixture.Layout,features);controller.Tick();fixture.Memory.Put(fixture.Walk,0L);int writes=fixture.Memory.Writes;controller.Restore();Require(fixture.Memory.Writes==writes);
        });
        check("Flight modifies only a flying controller and restores after landing",()=>
        {
            using var fixture=new MovementFixture();var m=fixture.Memory;var features=Catalog.Create().ToArray();var fly=features.Single(f=>f.Id=="flySpeed");fly.Active=true;fly.Target="2";
            long player=m.DataBase+0x180000,ctrl=m.DataBase+0x190000,state=m.DataBase+0x1A0000;long vt=m.ModuleBase+0x2000,fn=m.ModuleBase+0x3000,meta=m.ModuleBase+0x4000,name=m.ModuleBase+0x5000;
            m.Put(m.Save+0x33398,player);m.Put(player,m.ModuleBase+0x1000);m.Put(player+0x4888,ctrl);m.Put(player+0x4890,state);m.Put(ctrl,vt);m.Put(ctrl+0xD968,state);m.Put(state,m.ModuleBase+0x1000);m.Put(state+0x148,BitConverter.SingleToInt32Bits(25));
            m.Put(vt+0x20,fn);m.Write(fn,[0x48,0x8D,0x05,0xF9,0x0F,0x00,0x00,0xC3]);m.Put(meta+8,name);m.Write(name,System.Text.Encoding.ASCII.GetBytes("cPlCtrlGroundRide_def\0"));
            var controller=new MovementController(m,fixture.Layout,features);controller.Tick();Require(!fly.Bound&&fixture.Float(state+0x148)==25);
            m.Write(name,System.Text.Encoding.ASCII.GetBytes("cPlCtrlAirRide_mo008\0"));for(int i=0;i<25;i++)controller.Tick();Require(fly.Bound&&fixture.Float(state+0x148)==50);
            m.Put(player+0x4888,0L);controller.Tick();Require(!fly.Bound);fly.Active=false;controller.Restore("flySpeed");Require(fixture.Float(state+0x148)==25);
        });
    }
    sealed class MovementFixture : IDisposable
    {
        public AutomaticTests.FixtureMemory Memory=new();public AutomaticLayout Layout;
        public long Walk=>Memory.DataBase+0x150000;public long Mount=>Memory.DataBase+0x160000;public long Water=>Memory.DataBase+0x170000;
        public float Float(long a)=>BitConverter.ToSingle(Memory.Read(a,4));
        public MovementFixture()
        {
            var m=Memory;Layout=m.Layout() with{ModuleSize=0x5226000,MovementSupported=true};long manager=m.DataBase+0x80000;m.Put(m.ModuleBase+0x1DEBC10,manager);
            int i=0;foreach(var (offset,vt,row,fields) in new (int,int,long,(int,float)[])[]{
                (0x300,0x175B900,Walk,[(0x18,1.1f),(0x1C,1),(0x20,.75f)]),
                (0x308,0x175B728,Mount,[(0x40,.5f),(0x44,1),(0x5C,1)]),
                (0x318,0x175B9C0,Water,[(0x38,.6f),(0x3C,.75f)])})
            {
                long table=m.DataBase+0x90000+i*0x1000,rows=table+0x800;i++;m.Put(manager+offset,table);m.Put(table+0xD0,1);m.Put(table+0xE0,rows);m.Put(rows,row);m.Put(row,m.ModuleBase+vt);
                foreach(var (field,value) in fields)m.Put(row+field,BitConverter.SingleToInt32Bits(value));
            }
        }
        public void Dispose()=>Memory.Dispose();
    }
}
