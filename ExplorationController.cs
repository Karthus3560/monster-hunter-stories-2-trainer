using System.ComponentModel;

namespace RidersToolkit;

// All exploration changes are private game data, never executable patches.
internal sealed class ExplorationController(IProcessMemory memory,AutomaticLayout layout,IReadOnlyList<Feature> features)
{
    internal sealed record Actor(long Address,long Vtable,List<PointerGuard> Guards);
    sealed record Saved(string Feature,long Address,byte[] Original,byte[] Written,List<PointerGuard> Guards);
    readonly Dictionary<long,Saved> _saved=[];
    readonly List<Actor> _actors=[];
    int _ticks;
    long _player,_ctrl,_state;
    List<PointerGuard> _fieldGuards=[];
    long _pendingInput;
    List<PointerGuard> _inputGuards=[];
    public int GatherRequests {get;private set;}
    public int Revealed {get;private set;}
    public int Extended {get;private set;}
    public string LastGatherStatus {get;private set;}="waiting for field";
    static readonly (int Rva,string Hex)[] Samples=[
        (0xA94EA0,"4863C24869D0A0010200488D81403201004803C2C3"),
        (0x491590,"488B8188480000C3"),
        (0x3E1390,"488B0529739501C3"),
        (0x3E10E0,"488B8100010000C3"),
        (0x336980,"488B41184885C07504488B4110C3"),
        (0x49B1D0,"8B81E8000000C3"),
        (0x3A48A0,"488B8198020000C3"),
        (0xAC4970,"488BC13B91D0000000720333C0C3488B80E00000008BCA488B04C8C3"),
        (0x3BC810,"0FB681D9270000C3"),
        (0x3BEF3D,"4088BBD9270000"),
        (0x3F7770,"F30F10412CC3"),
        (0x3F79B0,"F30F11492CC3"),
        (0xD1BE41,"418B8C89B81200000FA3C10F8219010000"),
        (0x4376E7,"488B0D42B9A601BA1F000000E8A8468E00"),
        (0x3B477E,"889162090000"),
        (0xB68E1C,"488D7908488B174885D27419F6829C000000507510"),
        (0xD1F045,"8B8A9C0000008BC183E0503C507408F6C101"),
        (0xD1AC7F,"498981B8120000418981C0120000")
    ];
    public static bool Detect(byte[] image,long moduleBase,List<CodeProof> proofs)
    {
        if(!Samples.All(s=>s.Rva<=image.Length-Convert.FromHexString(s.Hex).Length&&image.AsSpan(s.Rva,Convert.FromHexString(s.Hex).Length).SequenceEqual(Convert.FromHexString(s.Hex))))return false;
        foreach(var s in Samples)proofs.Add(new(moduleBase+s.Rva,Convert.FromHexString(s.Hex)));
        return true;
    }
    byte[] Read(long a,int n)=>memory.Read(a,n);
    long Q(long a) {var b=Read(a,8);return b.Length==8?BitConverter.ToInt64(b):0;}
    int I(long a) {var b=Read(a,4);return b.Length==4?BitConverter.ToInt32(b):-1;}
    byte B(long a) {var b=Read(a,1);return b.Length==1?b[0]:(byte)255;}
    float F(long a) {var b=Read(a,4);return b.Length==4?BitConverter.ToSingle(b):float.NaN;}
    long Follow(long a,List<PointerGuard> path) {long p=Q(a);if(p>=65536&&p<0x7FFFFFFF0000)path.Add(new(a,p));else p=0;return p;}
    bool Object(long p)=>p>=65536&&memory.Query(p) is not null&&Q(p)>=layout.ModuleBase&&Q(p)<layout.ModuleBase+layout.ModuleSize;
    bool Valid(List<PointerGuard> path)=>memory.IsAlive&&path.All(g=>Q(g.Address)==g.Value);
    void Maintain(string id,long address,byte[] bytes,List<PointerGuard> path)
    {
        if(!Valid(path)||memory.Query(address) is null)return;
        var current=Read(address,bytes.Length);if(current.Length!=bytes.Length)return;
        if(_saved.TryGetValue(address,out var saved))
        {
            // Reused objects never inherit a restoration record from an old area.
            if(!Valid(saved.Guards))_saved.Remove(address);
            else _saved[address]=saved with {Original=current.SequenceEqual(saved.Written)?saved.Original:current,Written=bytes};
        }
        if(!_saved.ContainsKey(address))_saved[address]=new(id,address,current,bytes,[..path]);
        if(!current.SequenceEqual(bytes))memory.Write(address,bytes);
    }
    public void Restore(string? id=null)
    {
        if(id is null or "autoGather")
        {
            ReleaseGatherRequest();
        }
        foreach(var saved in _saved.Values.Where(s=>id is null||s.Feature==id).ToArray())
        {
            try {if(Valid(saved.Guards)&&Read(saved.Address,saved.Written.Length).SequenceEqual(saved.Written))memory.Write(saved.Address,saved.Original);}
            catch(Exception ex) when(ex is Win32Exception or InvalidOperationException or ArgumentException) { }
            _saved.Remove(saved.Address);
        }
    }
    bool Field()
    {
        var path=new List<PointerGuard>();long save=Follow(layout.SaveSlot,path);
        if(!Object(save))return false;
        _player=Follow(save+0x33398,path);if(!Object(_player))return false;
        _ctrl=Follow(_player+0x4888,path);if(!Object(_ctrl))return false;
        _state=Follow(_ctrl+0xD968,path);if(!Object(_state))return false;
        long body=Follow(_ctrl+0xD918,path);if(!Object(body))return false;
        _fieldGuards=path;return true;
    }
    List<PointerGuard> PathFor(Actor actor)=>[.._fieldGuards,..actor.Guards,new(actor.Address,actor.Vtable)];
    void Actors()
    {
        _actors.Clear();var path=new List<PointerGuard>();long manager=Follow(layout.ModuleBase+0x1D386C0,path);
        if(!Object(manager))return;
        long list=Follow(manager+0x100,path);if(!Object(list))return;
        long first=Follow(list+0x10,path),end=Q(list+0x18);
        if(first==0||end<first||(end-first)%40!=0||end-first>40*512)return;
        var seen=new HashSet<long>();int packages=0,visited=0;
        for(long entry=first;entry<end;entry+=40)
        {
            var groupPath=new List<PointerGuard>(path);long group=Follow(entry+0x20,groupPath);
            if(!Object(group))continue;
            int count=I(group+0x2D0);long slots=Follow(group+0x2E0,groupPath);
            if(count is <0 or >2048||slots==0)continue;
            for(int i=0;i<count;i++)
            {
                if(++packages>8192)throw new InvalidOperationException("Unexpected field package count.");
                var packagePath=new List<PointerGuard>(groupPath);long slot=Follow(slots+i*8,packagePath);
                if(!Object(slot))continue;
                long package=Follow(slot+0x18,packagePath);if(package==0){packagePath.Add(new(slot+0x18,0));package=Follow(slot+0x10,packagePath);}
                if(!Object(package)||Q(Q(package)+0x80)!=layout.ModuleBase+0x49B1D0)continue;
                int n=I(package+0xE8);long objects=Follow(package+0xF8,packagePath);
                if(n is <0 or >4096||objects==0)continue;
                for(int j=0;j<n;j++)
                {
                    if(++visited>16384)throw new InvalidOperationException("Unexpected field wrapper count.");
                    var actorPath=new List<PointerGuard>(packagePath);long wrapper=Follow(objects+j*8,actorPath);
                    if(!Object(wrapper)||Q(Q(wrapper)+0x28)!=layout.ModuleBase+0x3A48A0)continue;
                    long actor=Follow(wrapper+0x298,actorPath);
                    if(Object(actor)&&seen.Add(actor))_actors.Add(new(actor,Q(actor),actorPath));
                    if(_actors.Count>4096)throw new InvalidOperationException("Unexpected field object count. Exploration controls stopped.");
                }
            }
        }
    }
    bool Ingredient(Actor a)=>a.Vtable-layout.ModuleBase is 0x15F2528 or 0x15F1F68 or 0x15D0288 or 0x15D0728;
    (int Tool,int Type) IngredientKind(long node)
    {
        long manager=Q(layout.ModuleBase+0x1DEBC10);if(!Object(manager))return(-1,-1);
        long resource=Q(manager+0x1E0);if(!Object(resource))return(-1,-1);
        int id=I(node+0x2280),count=I(resource+0xD0);if(id<0||id>=count||count>10000)return(-1,-1);
        long row=Q(Q(resource+0xE0)+id*8);if(!Object(row))return(-1,-1);
        return(I(row+0x2C),I(row+0x30));
    }
    internal static bool Ordinary(int tool,int type)=>tool is 1 or 2 or 4&&type is >=1 and <=16&&type is not (12 or 14 or 15);
    void Radar()
    {
        Revealed=0;
        foreach(var a in _actors)
        {
            long address=0;
            if(Ingredient(a))address=a.Address+0x27D9;
            else if(a.Vtable==layout.ModuleBase+0x15EF848)
            {
                uint flags=(uint)I(a.Address+0x18);long body=Q(a.Address+0x120);
                if((flags&0x4000)!=0&&(flags&7)==2&&(flags&0x4800)==0x4800&&Object(body)&&F(body+0x22C0)!=0)address=a.Address+0x962;
            }
            if(address!=0&&B(address)<=1){Maintain("radar",address,[1],PathFor(a));Revealed++;}
        }
    }
    void Range()
    {
        Extended=0;
        foreach(var a in _actors.Where(a=>a.Vtable==layout.ModuleBase+0x15F2528))
        {
            var kind=IngredientKind(a.Address);if(!Ordinary(kind.Tool,kind.Type))continue;
            var path=PathFor(a);long sphere=Follow(a.Address+0x2770,path);
            if(!Object(sphere)||Q(sphere)!=layout.ModuleBase+0x1600040)continue;
            path.Add(new(sphere,layout.ModuleBase+0x1600040));
            float radius=F(sphere+0x2C);if(radius is <1 or >200||!float.IsFinite(radius))continue;
            float original=_saved.TryGetValue(sphere+0x2C,out var saved)?BitConverter.ToSingle(saved.Original):radius;
            Maintain("gatherRange",sphere+0x2C,BitConverter.GetBytes(Math.Min(200,original*4)),path);Extended++;
        }
    }
    void ReleaseGatherRequest()
    {
        try
        {
            if(_pendingInput!=0&&Valid(_inputGuards)&&memory.Query(_pendingInput) is not null)
            {uint bits=(uint)I(_pendingInput);if((bits&0x80000000u)!=0)memory.Write(_pendingInput,BitConverter.GetBytes(bits&0x7FFFFFFFu));}
        }
        catch(Exception ex)when(ex is Win32Exception or InvalidOperationException or ArgumentException){}
        finally{_pendingInput=0;_inputGuards=[];}
    }
    bool DialogueActive(List<PointerGuard> path)
    {
        // DA90 is an array of 32 {id, padding, message pointer} entries. Its
        // first ID can stay nonzero after any dialog. Native B68E10/D1F040
        // check message flags instead of requiring that whole array to be empty.
        for(int i=0;i<32;i++)
        {
            long entry=_ctrl+0xDA90+i*0x10,pointer=Q(entry+8);path.Add(new(entry+8,pointer));
            if(pointer==0)continue;
            if(!Object(pointer))return true;
            uint flags=(uint)I(pointer+0x9C);
            if((flags&0x51)==0)return true;
            path.Add(new(pointer,Q(pointer)));path.Add(new(pointer+0x9C,Q(pointer+0x9C)));
        }
        return false;
    }
    void Gather(bool foreground)
    {
        LastGatherStatus="walk beside a material";
        if(!foreground){LastGatherStatus="waiting for game focus";return;}
        var path=new List<PointerGuard>(_fieldGuards);
        if(DialogueActive(path)){LastGatherStatus="paused for dialogue";return;}
        if(B(_state+0x270)==0||Q(_ctrl+0xD958)!=0){LastGatherStatus="waiting for field controls";return;}
        // Native 3FE7A0 selects the first sorted interaction whenever the list
        // is nonempty. Several nearby interactions are normal after a skill,
        // a chest or a larger gathering radius.
        if(I(_ctrl+0x48) is <=0 or >128)return;
        long selected=Follow(_ctrl+0x58,path);if(selected==0)return;
        long affordance=Follow(selected,path);if(!Object(affordance)||Q(affordance)!=layout.ModuleBase+0x1601B60||I(affordance+0x24)!=4)return;
        path.Add(new(affordance,layout.ModuleBase+0x1601B60));
        long node=Follow(affordance+0x28,path);if(!Object(node)||Q(node)!=layout.ModuleBase+0x15F2528)return;
        path.Add(new(node,layout.ModuleBase+0x15F2528));var kind=IngredientKind(node);if(!Ordinary(kind.Tool,kind.Type))return;
        long input=Follow(layout.ModuleBase+0x1EA3030,path);if(!Object(input)||B(input+0x1261)==0||B(input+0xC68)==0){LastGatherStatus="waiting for game input";return;}
        path.Add(new(input,Q(input)));
        uint bits=(uint)I(input+0x12B8);
        if(!Valid(path))return;
        // The game's own transient logical-button buffer, cleared each frame.
        // No operating-system key presses and no inventory fabrication.
        memory.Write(input+0x12B8,BitConverter.GetBytes(bits|0x80000000u));GatherRequests++;
        if((bits&0x80000000u)==0){_pendingInput=input+0x12B8;_inputGuards=[new(layout.ModuleBase+0x1EA3030,input),new(input,Q(input))];}
        LastGatherStatus="collecting nearby material";
    }
    public void Tick(bool battle,bool foreground)
    {
        ReleaseGatherRequest(); // Also release on dialogue, focus loss, loading and battle.
        var controls=features.Where(f=>f.Id is "radar" or "gatherRange" or "autoGather").ToArray();
        foreach(var f in controls){f.Available=layout.FieldSupported;f.Bound=false;f.Waiting=layout.FieldSupported?"waiting for field":"game routine unavailable";}
        if(!layout.FieldSupported||!memory.IsAlive)return;
        try
        {
            if(battle||!Field())return;
            foreach(var f in controls){f.Bound=true;f.Waiting="ready in field";}
            if(++_ticks%10==1&&controls.Any(f=>f.Active))
            {
                Actors();
                if(controls.Any(f=>f.Id=="radar"&&f.Active))Radar();
                if(controls.Any(f=>f.Id=="gatherRange"&&f.Active))Range();
                // Stale area restoration records should not grow throughout a long session.
                foreach(long address in _saved.Keys.ToArray())if(!Valid(_saved[address].Guards))_saved.Remove(address);
            }
            if(controls.Any(f=>f.Id=="autoGather"&&f.Active))Gather(foreground);
            foreach(var f in controls)f.Current=f.Id switch {"radar"=>$"{Revealed} loaded markers","gatherRange"=>$"{Extended} material points",_=>LastGatherStatus};
        }
        catch(Exception ex) when(ex is Win32Exception or InvalidOperationException or ArgumentException)
        {
            // Area transitions can invalidate a complete object list between frames.
            foreach(var f in controls){f.Bound=false;f.Waiting="waiting for field to finish loading";}
            _actors.Clear();
        }
    }
}
