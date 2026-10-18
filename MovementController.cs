namespace RidersToolkit;

// Runtime copies of the native field-player parameter tables. No archive edits.
internal sealed class MovementController(IProcessMemory memory,AutomaticLayout layout,IReadOnlyList<Feature> features)
{
    readonly Dictionary<string,List<AutomaticTarget>> _targets=[];
    readonly Dictionary<long,(string Id,AutomaticTarget Target,double Original,double Written)> _saved=[];
    readonly Dictionary<string,string> _lastSetting=[];
    int _ticks;
    internal static readonly (int Rva,string Hex)[] Samples=[
        (0xCDC600,"488B8100030000C3"),
        (0xCDC59F,"488B8908030000"),
        (0xCDC61F,"488B8918030000"),
        (0xAC4970,"488BC13B91D0000000720333C0C3488B80E00000008BCA488B04C8C3"),
        (0xC77E6E,"488D4718C745A80C000000"),
        (0xC7751B,"488D4740C745A80C000000"),
        (0xC784A4,"488D4738C745A80C000000"),
        (0x4350A1,"488B8768D900000F57F6480548010000"),
        (0x41B244,"F30F10BE48010000F3410F59F8"),
        (0x41C1B7,"F3440F109348010000F3440F59D6"),
        (0x491590,"488B8188480000C3")
    ];
    internal static bool Detect(byte[] image,long module,List<CodeProof> proofs)
    {
        if(!Samples.All(s=>s.Rva<=image.Length-Convert.FromHexString(s.Hex).Length&&image.AsSpan(s.Rva,Convert.FromHexString(s.Hex).Length).SequenceEqual(Convert.FromHexString(s.Hex))))return false;
        foreach(var s in Samples)proofs.Add(new(module+s.Rva,Convert.FromHexString(s.Hex)));return true;
    }
    long Q(long a)=>BitConverter.ToInt64(memory.Read(a,8));
    bool Valid(AutomaticTarget t)=>memory.Query(t.Address)!=null&&t.Guards.All(g=>Q(g.Address)==g.Value);
    long Follow(long at,List<PointerGuard> path)
    {
        long p=Q(at);if(p<65536||memory.Query(p)==null)return 0;path.Add(new(at,p));return p;
    }
    void Resolve()
    {
        _targets.Clear();var path=new List<PointerGuard>();long manager=Follow(layout.ModuleBase+0x1DEBC10,path);if(manager==0)return;
        foreach(var (id,offset,vt,fields) in new (string,int,int,int[])[]{
            ("walkSpeed",0x300,0x175B900,[0x18,0x1C,0x20]),
            ("mountSpeed",0x308,0x175B728,[0x40,0x44,0x5C]),
            ("swimSpeed",0x318,0x175B9C0,[0x38,0x3C])})
        {
            var chain=new List<PointerGuard>(path);long table=Follow(manager+offset,chain);if(table==0)continue;
            int count=BitConverter.ToInt32(memory.Read(table+0xD0,4));if(count is <1 or >256)continue;
            long rows=Follow(table+0xE0,chain);if(rows==0)continue;
            chain.Add(new(table+0xD0,Q(table+0xD0)));var list=new List<AutomaticTarget>();
            for(int i=0;i<count;i++)
            {
                var guards=new List<PointerGuard>(chain);long row=Follow(rows+i*8,guards);if(row==0||Q(row)!=layout.ModuleBase+vt)continue;
                guards.Add(new(row,layout.ModuleBase+vt));
                foreach(int field in fields)
                {
                    long address=row+field;if(memory.Query(address)==null)continue;
                    float v=BitConverter.ToSingle(memory.Read(address,4));if(!float.IsFinite(v)||v<0||v>30)continue;
                    list.Add(new(address,NumberKind.Float32,30,[..guards]));
                }
            }
            if(list.Count>0)_targets[id]=list;
        }
    }
    bool InsideModule(long p,int size=8)=>p>=layout.ModuleBase&&p<=layout.ModuleBase+layout.ModuleSize-size;
    void ResolveFlight()
    {
        _targets.Remove("flySpeed");
        var path=new List<PointerGuard>();long save=Follow(layout.SaveSlot,path);if(save==0)return;
        long player=Follow(save+0x33398,path);if(player==0)return;
        path.Add(new(player,Q(player)));long ctrl=Q(player+0x4888);if(memory.Query(ctrl)==null)return;
        long vt=Q(ctrl);if(!InsideModule(vt,0x28))return;long fn=Q(vt+0x20);if(!InsideModule(fn))return;
        byte[] code=memory.Read(fn,8);if(code.Length!=8||!code.AsSpan(0,3).SequenceEqual(new byte[]{0x48,0x8D,0x05})||code[7]!=0xC3)return;
        long meta=fn+7+BitConverter.ToInt32(code,3);if(!InsideModule(meta,16))return;
        long name=Q(meta+8);if(!InsideModule(name,64))return;
        string type=System.Text.Encoding.ASCII.GetString(memory.Read(name,64)).Split('\0')[0];if(!type.StartsWith("cPlCtrlAirRide",StringComparison.Ordinal))return;
        // The player owns this common data independently of the active controller.
        // That ownership guard allows restoration after landing or dismounting.
        long state=Follow(player+0x4890,path);if(state==0||Q(ctrl+0xD968)!=state)return;
        path.Add(new(state,Q(state)));float speed=BitConverter.ToSingle(memory.Read(state+0x148,4));
        if(float.IsFinite(speed)&&speed is >0 and <=200)_targets["flySpeed"]=[new(state+0x148,NumberKind.Float32,200,path)];
    }
    internal void Tick()
    {
        if(!layout.MovementSupported)return;
        if(_ticks++%20==0)Resolve();
        ResolveFlight();
        foreach(var f in features.Where(f=>f.Category=="Movement"&&f.Id!="eggSpeed"))
        {
            f.Available=true;f.Bound=_targets.TryGetValue(f.Id,out var targets)&&targets.Any(Valid);f.Waiting=f.Id=="flySpeed"?"take off on a flying Monstie":"waiting for field parameters";
            f.Current=f.Bound?f.MultiplierLabel:"—";
            if(!f.Active||!f.Bound)continue;
            // Static tables need no per-frame rewriting. Slider edits apply on
            // the next tick; one-second maintenance covers reloaded resources.
            if(f.Id!="flySpeed"&&_ticks%20!=1&&_lastSetting.GetValueOrDefault(f.Id)==f.Target)continue;
            _lastSetting[f.Id]=f.Target;
            double multiplier=f.CheckedTarget();
            foreach(var t in targets!)
            {
                if(!Valid(t))continue;float current=BitConverter.ToSingle(memory.Read(t.Address,4));
                if(!float.IsFinite(current)||current<0||current>t.Maximum)continue;
                double original=_saved.TryGetValue(t.Address,out var saved)&&current==saved.Written&&Valid(saved.Target)?saved.Original:current;
                float value=(float)Math.Min(t.Maximum,original*multiplier);
                _saved[t.Address]=(f.Id,t,original,value);if(current!=value)memory.Write(t.Address,BitConverter.GetBytes(value));
            }
        }
    }
    internal void Restore(string? id=null)
    {
        foreach(var pair in _saved.ToArray())
        {
            if(id!=null&&pair.Value.Id!=id)continue;
            var s=pair.Value;
            try {if(Valid(s.Target)&&BitConverter.ToSingle(memory.Read(pair.Key,4))==s.Written)memory.Write(pair.Key,BitConverter.GetBytes((float)s.Original));}
            catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException){}
            _saved.Remove(pair.Key);
        }
        if(id==null)_lastSetting.Clear();else _lastSetting.Remove(id);
    }
}
