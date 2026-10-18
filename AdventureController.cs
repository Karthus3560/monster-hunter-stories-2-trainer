using System.ComponentModel;

namespace RidersToolkit;

// Temporary native data changes. Every capture is tied to its owning object.
internal sealed class AdventureController(IProcessMemory memory,AutomaticLayout layout,IReadOnlyList<Feature> features)
{
    sealed record Saved(string Id,AutomaticTarget Target,double Original,double Written);
    readonly Dictionary<long,Saved> _saved=[];
    internal static readonly string[] Ids=["eggSpeed","kinshipLevel","battleSpeed","loot","noConsume"];
    internal static readonly (int Rva,string Hex)[] Samples=[
        (0x40C455,"488B4728"),
        (0x40C461,"F30F10848850020000"),
        (0xB8CC70,"8891593E0000C3"),
        (0xB8A4C0,"83B9B43E0000020F94C0C3"),
        (0xB87AE0,"FE81593E0000C3"),
        (0xF0A282,"488D8794000000"),
        (0x3BB6DD,"0FB6B1C8270000"),
        (0x3BB79B,"8B5318488BC8448BC6"),
        (0xD3B121,"0FB7036689070FB64312884712"),
        (0xB7EC30,"488D816C040000C3")
    ];
    internal static bool Detect(byte[] image,long module,List<CodeProof> proofs)
    {
        if(!Samples.All(s=>s.Rva<=image.Length-Convert.FromHexString(s.Hex).Length&&image.AsSpan(s.Rva,Convert.FromHexString(s.Hex).Length).SequenceEqual(Convert.FromHexString(s.Hex))))return false;
        foreach(var s in Samples)proofs.Add(new(module+s.Rva,Convert.FromHexString(s.Hex)));return true;
    }
    long Q(long a)=>BitConverter.ToInt64(memory.Read(a,8));
    int I(long a)=>BitConverter.ToInt32(memory.Read(a,4));
    float F(long a)=>BitConverter.ToSingle(memory.Read(a,4));
    bool Object(long a)=>a>=65536&&memory.Query(a)!=null&&Q(a)>=layout.ModuleBase&&Q(a)<layout.ModuleBase+layout.ModuleSize;
    bool Valid(AutomaticTarget t)=>memory.IsAlive&&memory.Query(t.Address)!=null&&t.Guards.All(g=>Q(g.Address)==g.Value);
    long Follow(long a,List<PointerGuard> guards)
    {long p=Q(a);if(!Object(p))return 0;guards.Add(new(a,p));guards.Add(new(p,Q(p)));return p;}
    void Write(string id,AutomaticTarget target,double setting,bool multiply=false)
    {
        if(!Valid(target))return;
        double current=Numbers.Decode(memory.Read(target.Address,Numbers.Width(target.Kind)),target.Kind);
        if(!double.IsFinite(current)||current<0||current>target.Maximum)return;
        double original=_saved.TryGetValue(target.Address,out var prior)&&Valid(prior.Target)&&(!multiply||current==prior.Written)?prior.Original:current;
        double value=Math.Min(target.Maximum,multiply?original*setting:setting);
        if(target.Kind!=NumberKind.Float32)value=Math.Round(value);
        // Store the exact Float32 value that will be compared on the next tick.
        if(target.Kind==NumberKind.Float32)value=(float)value;
        _saved[target.Address]=new(id,target,original,value);
        if(current!=value)memory.Write(target.Address,Numbers.Encode(value,target.Kind));
    }
    internal void Restore(string? id=null)
    {
        foreach(var pair in _saved.ToArray())
        {
            var s=pair.Value;if(id!=null&&s.Id!=id)continue;
            try {if(Valid(s.Target)&&Numbers.Decode(memory.Read(pair.Key,Numbers.Width(s.Target.Kind)),s.Target.Kind)==s.Written)memory.Write(pair.Key,Numbers.Encode(s.Original,s.Target.Kind));}
            catch(Exception ex) when(ex is Win32Exception or InvalidOperationException or ArgumentException){}
            _saved.Remove(pair.Key);
        }
        if(id is null or "noConsume")_inventory.Clear();
    }
    Feature Feature(string id,bool bound,string waiting,string current)
    {
        var f=features.Single(f=>f.Id==id);f.Available=layout.AdventureSupported;f.Bound=bound&&f.Available;f.Waiting=f.Available?waiting:"game routine unavailable";f.Current=f.Bound?current:"—";return f;
    }
    internal void Tick(bool inBattle)
    {
        if(!layout.AdventureSupported)return;
        var path=new List<PointerGuard>();long save=Follow(layout.SaveSlot,path),player=save==0?0:Follow(save+0x33398,path);
        var statePath=new List<PointerGuard>(path);long state=player==0?0:Follow(player+0x4890,statePath);
        var egg=Feature("eggSpeed",state!=0&&!inBattle,"waiting for an outdoor area","egg carrying");
        if(egg.Active&&egg.Bound)
            for(int i=0;i<7;i++)if(F(state+0x250+i*4) is >0 and <=10)Write(egg.Id,new(state+0x250+i*4,NumberKind.Float32,10,[..statePath]),egg.CheckedTarget(),true);
        Battle(inBattle);
        Inventory(inBattle,save,path);
        Loot(inBattle);
    }
    void Battle(bool inBattle)
    {
        var path=new List<PointerGuard>();long battle=Follow(layout.BattleSlot,path);
        var f=Feature("kinshipLevel",false,"mount your Monstie in battle","Level 3");
        if(battle!=0&&inBattle)
            for(int party=0;party<2;party++)
            {
                var chain=new List<PointerGuard>(path);long actor=Follow(battle+0x9D0+party*8,chain);
                if(actor==0||I(actor+0x3EB4)!=2)continue;
                // Native riding state, independently guarded from the full gauge.
                chain.Add(new(actor+0x3EB4,Q(actor+0x3EB4)));
                f.Bound=true;f.Current="Level 3";
                if(f.Active)Write(f.Id,new(actor+0x3E59,NumberKind.Byte,3,chain),2);
            }
        var clockPath=new List<PointerGuard>();long clock=Follow(layout.ModuleBase+0x1EA2FF0,clockPath);
        var speed=Feature("battleSpeed",clock!=0&&inBattle,"waiting for battle","2× fast-forward");
        if(speed.Active&&speed.Bound&&F(clock+0x94) is >0 and <=4)Write(speed.Id,new(clock+0x94,NumberKind.Float32,4,clockPath),2,true);
        else Restore("battleSpeed");
    }
    readonly Dictionary<int,(int Id,int Count)> _inventory=[];
    internal IReadOnlyList<ItemDefinition> Items {get;set;}=[];
    long _inventoryOwner;
    void Inventory(bool inBattle,long save,List<PointerGuard> savePath)
    {
        var f=Feature("noConsume",save!=0&&inBattle,"waiting for battle","ordinary battle items");
        if(!f.Active||!f.Bound||save!=_inventoryOwner){_inventory.Clear();_inventoryOwner=save;}
        if(!f.Active||!f.Bound)return;
        var definitions=Items.Where(i=>i.Id>0&&i.Category is 0 or 1&&!i.KeyItem&&i.Capacity>0).ToDictionary(i=>i.Id);
        long inventory=save+layout.InventoryOffset;byte[] snapshot=memory.Read(inventory,4+2000*8);
        for(int slot=0;slot<2000;slot++)
        {
            long a=inventory+4+slot*8;byte[] data=snapshot.AsSpan(4+slot*8,4).ToArray();
            int id=BitConverter.ToUInt16(data),count=BitConverter.ToUInt16(data,2);_inventory.TryGetValue(slot,out var prior);
            bool lastItem=id==0&&count==0&&prior.Count==1&&definitions.ContainsKey(prior.Id);
            if(!definitions.TryGetValue(lastItem?prior.Id:id,out var item)||count>item.Capacity){_inventory.Remove(slot);continue;}
            bool filled=features.Any(x=>x.Active&&x.Id==(item.Category==0?"items":"support"));
            if(!filled&&(prior.Id==id||lastItem)&&prior.Count-count==1)
            {
                var t=new AutomaticTarget(a+2,NumberKind.UInt16,item.Capacity,[..savePath]);
                if(Valid(t)&&memory.Read(a,4).SequenceEqual(data))
                {
                    if(lastItem)memory.Write(a,[..BitConverter.GetBytes((ushort)prior.Id),..BitConverter.GetBytes((ushort)prior.Count)]);
                    else memory.Write(a+2,BitConverter.GetBytes((ushort)prior.Count));
                    id=prior.Id;count=prior.Count;
                }
            }
            _inventory[slot]=(id,count);
        }
    }
    void Loot(bool inBattle)
    {
        var f=Feature("loot",false,inBattle?"waiting for enemy rewards":"waiting for material points",features.Single(x=>x.Id=="loot").Target+"× rewards");
        if(!f.Active)return;
        double multiplier=f.CheckedTarget();if(multiplier is not (2 or 5 or 10))throw new ArgumentException("Choose 2×, 5× or 10× loot.");
        if(inBattle)
        {
            var rootPath=new List<PointerGuard>();long battle=Follow(layout.BattleSlot,rootPath);if(battle==0)return;
            for(int side=0;side<2;side++)
            {
                var sidePath=new List<PointerGuard>(rootPath);long actor=Follow(battle+0x9F8+side*8,sidePath);if(actor==0)continue;
                for(int slot=0;slot<4;slot++)
                {
                    var path=new List<PointerGuard>(sidePath);long unit=Follow(actor+0x20+slot*8,path);if(unit==0)continue;
                    long param=Follow(unit+0xB0,path);if(param==0||Q(Q(param)+0x330)!=layout.ModuleBase+0xB7EC30)continue;
                    path.Add(new(Q(param)+0x330,layout.ModuleBase+0xB7EC30));
                    long data=param+0x46C;
                    for(int j=0;j<9;j++)
                    {
                        int id=BitConverter.ToUInt16(memory.Read(data+j*2,2));if(!OrdinaryItem(id))continue;
                        var guard=new List<PointerGuard>(path){new(data+Math.Min(j/4*8,10),Q(data+Math.Min(j/4*8,10)))};
                        int qty=memory.Read(data+0x12+j,1)[0];if(qty==0)continue;
                        f.Bound=true;f.Current=multiplier+"× enemy loot";
                        Write(f.Id,new(data+0x12+j,NumberKind.Byte,99,guard),multiplier,true);
                    }
                }
            }
        }
        else
        {
            if(++_fieldTicks%10==1)FindMaterials();
            foreach(var actor in _materials)
            {
                if(!Valid(actor))continue;
                long node=actor.Address,lot=Q(node+0x27C0);if(lot==0||memory.Query(lot)==null)continue;
                int id=I(lot+0x18);if(memory.Read(lot+0x14,1)[0]!=1||!OrdinaryItem(id))continue;
                int qty=memory.Read(node+0x27C8,1)[0];if(qty==0||qty>99)continue;
                var path=new List<PointerGuard>(actor.Guards){new(node+0x27C0,lot),new(lot+0x18,Q(lot+0x18))};
                f.Bound=true;f.Current=multiplier+"× material loot";
                Write(f.Id,new(node+0x27C8,NumberKind.Byte,99,path),multiplier,true);
            }
        }
    }
    bool OrdinaryItem(int id)=>id!=497&&Items.Any(i=>i.Id==id&&!i.KeyItem&&i.Category is >=0 and <=4&&i.Capacity>0);
    readonly List<AutomaticTarget> _materials=[];
    int _fieldTicks;
    void FindMaterials()
    {
        _materials.Clear();var path=new List<PointerGuard>();long manager=Follow(layout.ModuleBase+0x1D386C0,path);if(manager==0)return;
        long list=Follow(manager+0x100,path);if(list==0)return;
        long first=Q(list+0x10),end=Q(list+0x18);if(first<65536||end<first||(end-first)%40!=0||end-first>40*512)return;
        path.Add(new(list+0x10,first));path.Add(new(list+0x18,end));int visited=0;
        for(long e=first;e<end;e+=40)
        {
            var gp=new List<PointerGuard>(path);long group=Follow(e+0x20,gp);if(group==0)continue;
            int count=I(group+0x2D0);long slots=Q(group+0x2E0);if(count is <0 or >2048||slots<65536)continue;gp.Add(new(group+0x2E0,slots));
            for(int i=0;i<count;i++)
            {
                var pp=new List<PointerGuard>(gp);long slot=Follow(slots+i*8,pp);if(slot==0)continue;
                long package=Follow(slot+0x18,pp);if(package==0){pp.Add(new(slot+0x18,0));package=Follow(slot+0x10,pp);}
                if(package==0||Q(Q(package)+0x80)!=layout.ModuleBase+0x49B1D0)continue;
                int n=I(package+0xE8);long objects=Q(package+0xF8);if(n is <0 or >4096||objects<65536)continue;pp.Add(new(package+0xF8,objects));
                for(int j=0;j<n;j++)
                {
                    if(++visited>16384)throw new InvalidOperationException("Unexpected field object count.");
                    var ap=new List<PointerGuard>(pp);long wrapper=Follow(objects+j*8,ap);if(wrapper==0||Q(Q(wrapper)+0x28)!=layout.ModuleBase+0x3A48A0)continue;
                    long actor=Follow(wrapper+0x298,ap);if(actor==0||Q(actor)-layout.ModuleBase is not (0x15F2528 or 0x15F1F68 or 0x15D0288 or 0x15D0728))continue;
                    // Only ordinary material lots; eggs, chests and quest keys
                    // never enter this list of writable reward quantities.
                    _materials.Add(new(actor,NumberKind.Byte,99,ap));
                }
            }
        }
    }
}
