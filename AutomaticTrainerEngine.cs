using System.ComponentModel;

namespace RidersToolkit;

internal sealed record PointerGuard(long Address,long Value);
internal sealed record AutomaticTarget(long Address,NumberKind Kind,int Maximum,List<PointerGuard> Guards,int? ItemId=null,long DirtyFlag=0);

internal sealed class AutomaticTrainerEngine : IDisposable
{
    public IProcessMemory? Memory {get;private set;}
    public int Pid {get;private set;}
    public IReadOnlyList<Feature> Features {get;}
    public bool BattleManagerLocated {get;private set;}
    public bool SaveManagerLocated {get;private set;}
    public event Action<string,string>? Fault;
    AutomaticLayout? _layout;
    ExplorationController? _exploration;
    MovementController? _movement;
    AdventureController? _adventure;
    ExtraController? _extra;
    StableEditor? _editor;
    public StableEditor? Editor=>_editor;
    public ExplorationController? Exploration=>_exploration;
    public string? RestorationIssue=>_extra?.RestorationIssue;
    IReadOnlyList<ItemDefinition> _items=[];
    readonly Dictionary<string,List<AutomaticTarget>> _targets=[];
    readonly Dictionary<long,(AutomaticTarget Target,double Original,double Written)> _restoreAttack=[];
    readonly Dictionary<long,(AutomaticTarget Target,double Original,double Written)> _restoreRetreat=[];
    readonly Dictionary<long,(AutomaticTarget Target,double Original,double Written)> _restoreExperience=[];
    readonly HashSet<string> _pendingSignals=[];
    int _ticks;
    public AutomaticTrainerEngine(IReadOnlyList<Feature> features)=>Features=features;
    public void Attach(AutomaticConnection connection) { Detach(); Memory=connection.Memory; _layout=connection.Layout; _items=connection.Items; Pid=connection.Pid;_ticks=0;_exploration=new(Memory,_layout,Features);_movement=new(Memory,_layout,Features);_adventure=new(Memory,_layout,Features){Items=_items};_extra=new(Memory,_layout,Features);_editor=new(Memory,_layout,()=>Features.Any(f=>f.Id=="riderHp"&&f.Bound));Refresh(true); }
    internal void AttachTest(IProcessMemory memory,AutomaticLayout layout,IReadOnlyList<ItemDefinition> items) { Detach(); Memory=memory;_layout=layout;_items=items;_ticks=0;_exploration=new(Memory,_layout,Features);_movement=new(Memory,_layout,Features);_adventure=new(Memory,_layout,Features){Items=_items};_extra=new(Memory,_layout,Features);_editor=new(Memory,_layout,()=>Features.Any(f=>f.Id=="riderHp"&&f.Bound));Refresh(true); }
    long Pointer(long address)
    {
        var bytes=Memory!.Read(address,8); if(bytes.Length!=8) return 0; long pointer=BitConverter.ToInt64(bytes);
        return pointer is >=65536 and <0x7FFFFFFF0000?pointer:0;
    }
    long Follow(long address,List<PointerGuard> guards)
    { long pointer=Pointer(address); if(pointer!=0) guards.Add(new(address,pointer));return pointer; }
    bool IsObject(long pointer)
    {
        if(pointer==0 || Memory!.Query(pointer) is null) return false;
        long vtable=Pointer(pointer);return vtable>=_layout!.ModuleBase&&vtable<_layout.ModuleBase+_layout.ModuleSize;
    }
    void Add(string id,long address,NumberKind kind,int max,List<PointerGuard> guards,int? itemId=null,long dirtyFlag=0)
    {
        var region=Memory!.Query(address); int width=Numbers.Width(kind);
        if(region is null || address>region.Base+region.Length-width) return;
        var bytes=Memory.Read(address,width); if(bytes.Length!=width) return;
        double value=Numbers.Decode(bytes,kind); if(!double.IsFinite(value)||value<0||value>max) return;
        if(!_targets.TryGetValue(id,out var list)) _targets[id]=list=[];
        list.Add(new(address,kind,max,[..guards],itemId,dirtyFlag));
    }
    void Refresh(bool forceInventory=false)
    {
        if(Memory is null || _layout is null) return;
        foreach(var feature in Features.Where(f=>f.Category=="Battle"))_targets.Remove(feature.Id);
        _targets.Remove("retreat");
        BattleManagerLocated=false;
        try
        {
            var guards=new List<PointerGuard>(); long battle=Follow(_layout.BattleSlot,guards);
            if(IsObject(battle))
            {
                BattleManagerLocated=true;
              // The first side contains the Rider and the allied battle buddy.
              // Battle boosts only apply to the allied side.
              for(int party=0;party<2;party++)
              {
                var actorGuards=new List<PointerGuard>(guards); long actor=Follow(battle+0x9D0+party*8,actorGuards);
                if(IsObject(actor))
                {
                    long rider=Parameter(actor,0x40,actorGuards,out var riderGuards);
                    long monstie=Parameter(actor,0x48,actorGuards,out var monstieGuards);
                    if(rider!=0)
                    {
                        Add("riderHp",rider+0x18,NumberKind.UInt16,65535,riderGuards);
                        Add("hearts",actor+_layout.HeartsOffset,NumberKind.Byte,9,actorGuards);
                        Add("turns",battle+_layout.TurnOffset,NumberKind.Int32,9999,actorGuards);
                        Add("damage",rider+0x54,NumberKind.UInt16,65535,riderGuards);
                    }
                    if(monstie!=0)
                    {
                        Add("monstieHp",monstie+0x18,NumberKind.UInt16,65535,monstieGuards);
                        Add("kinship",monstie+_layout.RidingOffset+8,NumberKind.Byte,100,monstieGuards,dirtyFlag:actor+_layout.HeartsOffset+4);
                        Add("kinship",actor+_layout.HeartsOffset+0x46,NumberKind.Byte,100,actorGuards);
                        Add("damage",monstie+0x54,NumberKind.UInt16,65535,monstieGuards);
                    }
                }
              }
              if(_layout.RetreatSupported||_layout.ExperienceSupported)
                for(int party=0;party<2;party++)
                {
                    var partyPath=new List<PointerGuard>(guards);long controller=Follow(battle+0x9F8+party*8,partyPath);
                    if(!IsObject(controller))continue;
                    // Native B89DF0 resolves four unit slots within each side's
                    // two controllers. The controller itself is not an enemy unit.
                    for(int slot=0;slot<4;slot++)
                    {
                        var path=new List<PointerGuard>(partyPath);long unit=Follow(controller+0x20+slot*8,path);
                        if(!IsObject(unit))continue;
                        path.Add(new(unit,Pointer(unit)));
                        long parameter=Follow(unit+0xB0,path);
                        if(IsObject(parameter))
                        {
                            path.Add(new(parameter,Pointer(parameter)));
                            if(_layout.RetreatSupported)Add("retreat",parameter+0x1F48,NumberKind.UInt16,65535,path);
                            if(_layout.ExperienceSupported)Add("battleExp",parameter+0x1FE8,NumberKind.Int32,999999,path);
                        }
                    }
                }
            }
        }
        catch(Exception ex) when(ex is Win32Exception or InvalidOperationException or ArgumentException) { }
        if(forceInventory||_ticks%10==0)
        {
          foreach(var feature in Features.Where(f=>f.Category=="Inventory"))_targets.Remove(feature.Id);
          SaveManagerLocated=false;
          try
          {
            var guards=new List<PointerGuard>();long save=Follow(_layout.SaveSlot,guards);
            if(IsObject(save))
            {
                SaveManagerLocated=true;
                long inventory=save+_layout.InventoryOffset; var data=Memory.Read(inventory,4+2000*8);
                if(data.Length==4+2000*8)
                {
                    var owned=_items.Where(i=>i.Id>0 && BitConverter.ToUInt16(data,4+i.Id*8)==i.Id && BitConverter.ToUInt16(data,6+i.Id*8)>0).ToArray();
                    // A title-screen save manager exists but has no owned entries.
                    // Never write an uninitialised save block just because it exists.
                    if(owned.Length>0)
                    {
                        Add("money",inventory,NumberKind.Int32,9999999,guards);
                        foreach(var item in owned)
                        {
                            if(item.KeyItem || item.Category>4 || item.Capacity<1) continue;
                            string id=item.Id==497?"caps":item.Category switch {0=>"items",1=>"support",2=>"materials",3=>"facility",4=>"growth",_=>""};
                            if(id!="") Add(id,inventory+6+item.Id*8,NumberKind.UInt16,item.Capacity,guards,item.Id);
                        }
                    }
                }
            }
          }
          catch(Exception ex) when(ex is Win32Exception or InvalidOperationException or ArgumentException) { }
        }
        foreach(var feature in Features)
        {
            if(ExtraController.Ids.Contains(feature.Id)||AdventureController.Ids.Contains(feature.Id)||feature.Category=="Movement"||feature.Id is "radar" or "gatherRange" or "autoGather")continue;
            feature.Available=feature.Id=="battleExp"?_layout.ExperienceSupported:feature.Id!="retreat"||_layout.RetreatSupported;
            feature.Bound=_targets.TryGetValue(feature.Id,out var targets)&&targets.Count>0;
            feature.Waiting=!feature.Available?"game routine unavailable":feature.Category=="Battle"||feature.Id=="retreat"?"waiting for battle":feature.Id=="caps"?"waiting for owned bottle caps":"waiting for owned items / solo save";
            if(!feature.Bound) { feature.Address=0;feature.Current="—";continue; }
            var first=targets![0];
            if(!Valid(first)) {feature.Bound=false;feature.Address=0;feature.Current="—";continue;}
            if(!feature.IsSlider)feature.Kind=first.Kind;feature.Address=first.Address;
            var current=Numbers.Decode(Memory.Read(first.Address,Numbers.Width(first.Kind)),first.Kind);
            feature.Current=feature.IsSlider?feature.MultiplierLabel:feature.IsBulk?$"{targets.Count} stacks":current.ToString("N0");
            feature.LastObserved=current;
        }
        bool inBattle=_targets.TryGetValue("riderHp",out var allies)&&allies.Count>0;
        bool foreground=false;if(Memory is ProcessMemory pm){Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out uint pid);foreground=pid==pm.ProcessId;}
        _exploration?.Tick(inBattle,foreground);
        _movement?.Tick();
        _adventure?.Tick(inBattle);
        _extra?.Tick(inBattle);
    }
    long Parameter(long actor,int offset,List<PointerGuard> guards,out List<PointerGuard> path)
    {
        path=[..guards]; long array=Follow(actor+offset,path);if(array==0)return 0;
        long parameter=Follow(array+_layout!.ParameterOffset,path);if(parameter==0 || Memory!.Query(parameter) is null)return 0;return parameter;
    }
    bool Valid(AutomaticTarget target)
    {
        if(Memory is null || !Memory.IsAlive) return false;
        foreach(var guard in target.Guards) if(Pointer(guard.Address)!=guard.Value) return false;
        if(target.ItemId is int id)
        {
            var row=Memory.Read(target.Address-2,4);
            if(row.Length!=4 || BitConverter.ToUInt16(row)!=id || BitConverter.ToUInt16(row,2)==0) return false;
        }
        return Memory.Query(target.Address) is not null;
    }
    void Write(Feature feature,AutomaticTarget target,double value)
    {
        if(!Valid(target)) return;
        var bytes=Memory!.Read(target.Address,Numbers.Width(target.Kind));if(bytes.Length!=Numbers.Width(target.Kind))return;
        double current=Numbers.Decode(bytes,target.Kind);if(current<0 || current>target.Maximum)return;
        value=Math.Min(value,target.Maximum);
        if(feature.Id=="damage")
        {
            double original=_restoreAttack.TryGetValue(target.Address,out var saved)&&current==saved.Written&&Valid(saved.Target)?saved.Original:current;
            value=Math.Min(30000,Math.Round(original*value));_restoreAttack[target.Address]=(target,original,value);
        }
        if(feature.Id=="battleExp")
        {
            double original=_restoreExperience.TryGetValue(target.Address,out var saved)&&current==saved.Written&&Valid(saved.Target)?saved.Original:current;
            value=Math.Min(999999,Math.Round(original*value));_restoreExperience[target.Address]=(target,original,value);
        }
        if(feature.Id=="retreat")
        { if(_restoreRetreat.TryGetValue(target.Address,out var saved)) _restoreRetreat[target.Address]=(target,current==saved.Written?saved.Original:current,value); else _restoreRetreat.Add(target.Address,(target,current,value)); }
        if(value!=current) Memory.Write(target.Address,Numbers.Encode(value,target.Kind));
        if(target.DirtyFlag!=0 && (value!=current||_pendingSignals.Contains(feature.Id)))
        {
            var flags=Memory.Read(target.DirtyFlag,1);
            if(flags.Length==1 && Memory.Query(target.DirtyFlag) is not null)Memory.Write(target.DirtyFlag,[(byte)(flags[0]|4)]);
        }
    }
    public double Apply(Feature feature)
    {
        if(Memory?.IsAlive!=true) throw new InvalidOperationException("Start Stories 2 and wait for automatic connection.");
        Refresh(true);if(!_targets.TryGetValue(feature.Id,out var targets)||targets.Count==0) throw new InvalidOperationException(feature.Category=="Battle"?"This control will be ready in battle. You can arm its switch now.":"Load a solo save and own at least one item in this category first.");
        var value=feature.CheckedTarget();_pendingSignals.Add(feature.Id);foreach(var target in targets) Write(feature,target,value);_pendingSignals.Remove(feature.Id);Refresh(true);return value;
    }
    public void SetActive(Feature feature,bool active)
    {
        if(active && Memory?.IsAlive!=true) throw new InvalidOperationException("Start Stories 2 and wait for automatic connection.");
        if(active&&!feature.Available)throw new InvalidOperationException("This control's game routine is unavailable. It has not been enabled.");
        if(active) {feature.CheckedTarget();_pendingSignals.Add(feature.Id);} else _pendingSignals.Remove(feature.Id);
        feature.Active=active;
        if(!active&&feature.Id=="damage") RestoreAttack();
        if(!active&&feature.Id=="retreat") RestoreRetreat();
        if(!active&&feature.Id=="battleExp") RestoreExperience();
        if(!active&&feature.Category=="Movement")_movement?.Restore(feature.Id);
        if(!active&&ExtraController.Ids.Contains(feature.Id))_extra?.Restore(feature.Id);
        if(!active&&AdventureController.Ids.Contains(feature.Id))_adventure?.Restore(feature.Id);
        if(!active&&feature.Id is "radar" or "gatherRange" or "autoGather") _exploration?.Restore(feature.Id);
    }
    void RestoreAttack()
    {
        foreach(var saved in _restoreAttack.Values)
            try
            {
                if(!Valid(saved.Target))continue;
                if(Numbers.Decode(Memory!.Read(saved.Target.Address,2),NumberKind.UInt16)==saved.Written)
                    Memory.Write(saved.Target.Address,Numbers.Encode(saved.Original,NumberKind.UInt16));
            }
            catch(Exception ex) when(ex is Win32Exception or InvalidOperationException or ArgumentException) { }
        _restoreAttack.Clear();
    }
    void RestoreRetreat()
    {
        foreach(var saved in _restoreRetreat.Values)
            try
            {
                if(Valid(saved.Target)&&Numbers.Decode(Memory!.Read(saved.Target.Address,2),NumberKind.UInt16)==saved.Written)
                    Memory.Write(saved.Target.Address,Numbers.Encode(saved.Original,NumberKind.UInt16));
            }
            catch(Exception ex) when(ex is Win32Exception or InvalidOperationException or ArgumentException) { }
        _restoreRetreat.Clear();
    }
    void RestoreExperience()
    {
        foreach(var s in _restoreExperience.Values)
            try {if(Valid(s.Target)&&Numbers.Decode(Memory!.Read(s.Target.Address,4),NumberKind.Int32)==s.Written)Memory.Write(s.Target.Address,Numbers.Encode(s.Original,NumberKind.Int32));}
            catch(Exception ex) when(ex is Win32Exception or InvalidOperationException or ArgumentException){}
        _restoreExperience.Clear();
    }
    public void Tick()
    {
        if(Memory is null)return;
        if(!Memory.IsAlive) { Detach();Fault?.Invoke("Game closed","Automatic controls stopped. They will reconnect with the game.");return; }
        try
        {
            if(++_ticks%20==0 && !_layout!.Verify(Memory)) throw new InvalidOperationException("Game routines changed. Controls stopped; close other trainers and reconnect.");
            Refresh();
            foreach(var feature in Features.Where(f=>f.Active))
                if(!feature.IsBulk||_ticks%4==0)
                if(_targets.TryGetValue(feature.Id,out var targets))
                { double value=feature.CheckedTarget();foreach(var target in targets) Write(feature,target,value);_pendingSignals.Remove(feature.Id); }
        }
        catch(Exception ex) when(ex is Win32Exception or InvalidOperationException or ArgumentException)
        { Detach(false);Fault?.Invoke("Automatic controls stopped",ex.Message); }
    }
    public void StopAll() { foreach(var feature in Features) feature.Active=false;_pendingSignals.Clear();RestoreAttack();RestoreRetreat();RestoreExperience();_movement?.Restore();_exploration?.Restore();_adventure?.Restore();_extra?.Restore(); }
    public void Detach(bool restoreAttack=true)
    {
        if(restoreAttack) StopAll();else {try{_extra?.RestoreCode();}catch{}foreach(var feature in Features)feature.Active=false;_restoreAttack.Clear();_restoreRetreat.Clear();_restoreExperience.Clear();_pendingSignals.Clear();}
        _editor=null;_extra=null;_movement=null;_exploration=null;_adventure=null;Memory?.Dispose();Memory=null;_layout=null;_targets.Clear();Pid=0;BattleManagerLocated=false;SaveManagerLocated=false;
        foreach(var feature in Features) { feature.Available=false;feature.Bound=false;feature.Address=0;feature.Current="—";feature.Waiting="waiting for game"; }
    }
    public void Dispose()=>Detach();
}
