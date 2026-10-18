namespace RidersToolkit;

internal static class AdventureTests
{
    internal static void Run(Action<string,Action> check)
    {
        void Require(bool v,string message="Adventure assertion failed"){if(!v)throw new Exception(message);}
        AutomaticLayout Layout(AutomaticTests.FixtureMemory m)=>m.Layout() with{ModuleSize=0x5226000,AdventureSupported=true};
        Feature Feature(IReadOnlyList<Feature> f,string id)=>f.Single(x=>x.Id==id);
        float Float(AutomaticTests.FixtureMemory m,long a)=>BitConverter.ToSingle(m.Read(a,4));
        check("Egg carrying scales all seven weights once, updates sliders and restores exact originals",()=>
        {
            using var m=new AutomaticTests.FixtureMemory();long player=m.DataBase+0x180000,state=m.DataBase+0x190000;
            m.Put(m.Save+0x33398,player);m.Put(player,m.ModuleBase+0x1000);m.Put(player+0x4890,state);m.Put(state,m.ModuleBase+0x1000);
            for(int i=0;i<7;i++)m.Put(state+0x250+i*4,BitConverter.SingleToInt32Bits(1.6f-i*.1f));
            var features=Catalog.Create().ToArray();var f=Feature(features,"eggSpeed");f.Active=true;f.Target="2";var c=new AdventureController(m,Layout(m),features);
            for(int i=0;i<30;i++)c.Tick(false);Require(Float(m,state+0x250)==3.2f&&Float(m,state+0x268)==2);
            f.Target="3";c.Tick(false);Require(Math.Abs(Float(m,state+0x250)-4.8f)<.000001);
            c.Restore(f.Id);Require(Float(m,state+0x250)==1.6f&&Float(m,state+0x268)==1);
        });
        check("Removed stealth is absent and field ticks never change its riding state or timer",()=>
        {
            using var m=new AutomaticTests.FixtureMemory();long player=m.DataBase+0x180000,state=m.DataBase+0x190000;
            m.Put(m.Save+0x33398,player);m.Put(player,m.ModuleBase+0x1000);m.Put(player+0x4890,state);m.Put(state,m.ModuleBase+0x1000);
            m.Write(state+0x26E,[4]);m.Put(state+0x2A4,BitConverter.SingleToInt32Bits(12.5f));
            var features=Catalog.Create().ToArray();Require(features.All(f=>f.Id!="stealth")&&!AdventureController.Ids.Contains("stealth"));
            foreach(var f in features.Where(f=>AdventureController.Ids.Contains(f.Id)))f.Active=true;
            var c=new AdventureController(m,Layout(m),features);for(int i=0;i<30;i++)c.Tick(false);c.Restore();
            Require(m.Byte(state+0x26E)==4&&Float(m,state+0x2A4)==12.5f);
        });
        check("Maximum attack level affects mounted allies only and leaves loss counters and gauges alone",()=>
        {
            using var m=new AutomaticTests.FixtureMemory();m.LoadBattleAndSave();m.LoadBuddy();m.Put(m.Actor+0x3E5A,(ushort)1);
            var features=Catalog.Create().ToArray();Feature(features,"kinshipLevel").Active=true;var c=new AdventureController(m,Layout(m),features);c.Tick(true);Require(m.Byte(m.Actor+0x3E59)==0);
            m.Put(m.Actor+0x3EB4,2);m.Put(m.BuddyActor+0x3EB4,2);c.Tick(true);
            Require(m.Byte(m.Actor+0x3E59)==2&&m.Byte(m.BuddyActor+0x3E59)==2&&m.Byte(m.Actor+0x3E5A)==1&&m.Byte(m.Monstie+0x1FF0)==0&&m.Byte(m.EnemyActor+0x3E59)==0);
            c.Restore();Require(m.Byte(m.Actor+0x3E59)==0);
        });
        check("Battle fast-forward scales current pace once and restores immediately outside battle",()=>
        {
            using var m=new AutomaticTests.FixtureMemory();long clock=m.DataBase+0x180000;m.Put(m.ModuleBase+0x1EA2FF0,clock);m.Put(clock,m.ModuleBase+0x1000);m.Put(clock+0x94,BitConverter.SingleToInt32Bits(.75f));
            var features=Catalog.Create().ToArray();Feature(features,"battleSpeed").Active=true;var c=new AdventureController(m,Layout(m),features);c.Tick(false);Require(Float(m,clock+0x94)==.75f);
            for(int i=0;i<30;i++)c.Tick(true);Require(Float(m,clock+0x94)==1.5f);c.Tick(false);Require(Float(m,clock+0x94)==.75f);
        });
        check("No consumption preserves ordinary battle items including the last item, with excluded categories untouched",()=>
        {
            using var m=new AutomaticTests.FixtureMemory();m.LoadBattleAndSave();var features=Catalog.Create().ToArray();Feature(features,"noConsume").Active=true;
            var c=new AdventureController(m,Layout(m),features){Items=m.Items};c.Tick(true);
            foreach(int id in new[]{1,2,3,6,7})m.Put(m.Inventory+6+id*8,(ushort)1);c.Tick(true);
            Require(m.U16(m.Inventory+14)==2&&m.U16(m.Inventory+22)==2&&m.U16(m.Inventory+30)==1&&m.U16(m.Inventory+54)==1&&m.U16(m.Inventory+62)==1);
            // Rebaseline a one-item stack, then let the native inventory clear it.
            c.Restore("noConsume");m.Put(m.Inventory+14,(ushort)1);c.Tick(true);m.Put(m.Inventory+12,0);c.Tick(true);Require(m.U16(m.Inventory+12)==1&&m.U16(m.Inventory+14)==1);
            c.Tick(false);m.Put(m.Inventory+14,(ushort)0);c.Tick(false);Require(m.U16(m.Inventory+14)==0);
        });
        check("Enemy loot scales source quantities once, caps and excludes quest items and bottle caps",()=>
        {
            using var m=new AutomaticTests.FixtureMemory();m.LoadBattleAndSave();m.LoadBuddy();long unit=m.DataBase+0xD0000,param=m.DataBase+0xE0000,vt=m.ModuleBase+0x4000;
            m.Put(m.EnemyActor+0x20,unit);m.Put(unit,m.ModuleBase+0x1000);m.Put(unit+0xB0,param);m.Put(param,vt);m.Put(vt+0x330,m.ModuleBase+0xB7EC30);
            long data=param+0x46C;for(int j=0;j<4;j++){m.Put(data+j*2,(ushort)new[]{3,6,497,7}[j]);m.Write(data+0x12+j,[1]);}
            var features=Catalog.Create().ToArray();var loot=Feature(features,"loot");loot.Active=true;loot.Target="5";var c=new AdventureController(m,Layout(m),features){Items=m.Items};
            for(int i=0;i<30;i++)c.Tick(true);Require(m.Byte(data+0x12)==5&&m.Byte(data+0x13)==1&&m.Byte(data+0x14)==1&&m.Byte(data+0x15)==1);
            loot.Target="10";c.Tick(true);Require(m.Byte(data+0x12)==10);m.Write(data+0x12,[20]);c.Tick(true);Require(m.Byte(data+0x12)==99);c.Restore();Require(m.Byte(data+0x12)==20);
        });
        check("Material loot changes pending ordinary rewards rather than purchased inventory",()=>
        {
            using var f=new ExplorationTests.FieldFixture();var m=f.Memory;long lot=m.DataBase+0x1C0000;m.Put(f.Node+0x27C0,lot);m.Write(lot+0x14,[1]);m.Put(lot+0x18,3);m.Write(f.Node+0x27C8,[1]);
            var features=Catalog.Create().ToArray();Feature(features,"loot").Active=true;var c=new AdventureController(m,f.Layout with{AdventureSupported=true},features){Items=m.Items};
            for(int i=0;i<30;i++)c.Tick(false);Require(m.Byte(f.Node+0x27C8)==2);c.Restore();Require(m.Byte(f.Node+0x27C8)==1);
        });
        check("Restoration rejects recycled owners and invalid loot choices are rejected",()=>
        {
            using var m=new AutomaticTests.FixtureMemory();long clock=m.DataBase+0x180000;m.Put(m.ModuleBase+0x1EA2FF0,clock);m.Put(clock,m.ModuleBase+0x1000);m.Put(clock+0x94,BitConverter.SingleToInt32Bits(1));
            var features=Catalog.Create().ToArray();Feature(features,"battleSpeed").Active=true;var c=new AdventureController(m,Layout(m),features);c.Tick(true);m.Put(m.ModuleBase+0x1EA2FF0,0L);int writes=m.Writes;c.Restore();Require(m.Writes==writes);
            var loot=Feature(features,"loot");loot.Target="3";bool rejected=false;try{loot.CheckedTarget();}catch(ArgumentException){rejected=true;}Require(rejected);
        });
    }
}
