using System.ComponentModel;
using System.Text;

namespace RidersToolkit;

internal sealed class ExtraController(IProcessMemory memory,AutomaticLayout layout,IReadOnlyList<Feature> features)
{
    internal static readonly string[] Ids=["prayerBuff","damageTaken","fastGather","fieldItems"];
    internal static readonly (int Rva,string Hex)[] Samples=[
        (0x5418FA,"4084FF741E"),(0x541912,"41B8"),(0x541918,"E8530C4700"),(0x9B2570,"4863C2488D14C133C00FB74A06412BC885C9660F4FC16689420633C0C3"),
        (0xA94DF0,"488D81A0A50900C3"),(0xA94EE0,"83FA0177164863C2488D144048C1E204488D81ECD950004803C2C3"),
        (0x9B25FF,"486BD0784881C1640C0100"),(0xC0C380,"4863C74883C05F488D0C83"),
        (0x40D814,"488B4330C7806C0600000000803F"),(0xCDCE20,"488B81980C0000C3")];
    internal static bool Detect(byte[] image,long module,List<CodeProof> proofs)
    {
        if(!Samples.All(s=>s.Rva<=image.Length-Convert.FromHexString(s.Hex).Length&&image.AsSpan(s.Rva,Convert.FromHexString(s.Hex).Length).SequenceEqual(Convert.FromHexString(s.Hex))))return false;
        foreach(var s in Samples)proofs.Add(new(module+s.Rva,Convert.FromHexString(s.Hex)));return true;
    }
    sealed record Saved(string Id,long Address,byte[] Original,byte[] Written,List<PointerGuard> Guards);
    readonly Dictionary<long,Saved> _saved=[];
    long Q(long a)=>BitConverter.ToInt64(memory.Read(a,8));
    int I(long a)=>BitConverter.ToInt32(memory.Read(a,4));
    bool Object(long a)=>a>=65536&&memory.Query(a)!=null&&Q(a)>=layout.ModuleBase&&Q(a)<layout.ModuleBase+layout.ModuleSize;
    bool Valid(Saved s)=>memory.IsAlive&&memory.Query(s.Address)!=null&&s.Guards.All(g=>Q(g.Address)==g.Value);
    long Follow(long a,List<PointerGuard> guards){long p=Q(a);if(!Object(p))return 0;guards.Add(new(a,p));guards.Add(new(p,Q(p)));return p;}
    void Write(string id,long a,byte[] bytes,List<PointerGuard> guards)
    {
        if(memory.Query(a)==null||guards.Any(g=>Q(g.Address)!=g.Value))return;
        var current=memory.Read(a,bytes.Length);
        byte[] original=_saved.TryGetValue(a,out var s)&&Valid(s)&&current.SequenceEqual(s.Written)?s.Original:current;
        _saved[a]=new(id,a,original,bytes,[..guards]);if(!current.SequenceEqual(bytes))memory.Write(a,bytes);
    }
    bool _fieldFree;
    internal string? RestorationIssue {get;private set;}
    internal void RestoreCode()
    {
        if(!_fieldFree)return;
        if(!memory.IsAlive){_fieldFree=false;return;}
        try
        {
            if(memory is IFieldItemCostMemory code)code.SetFieldItemCost(layout.ModuleBase,false);
            _fieldFree=false;RestorationIssue=null;
        }
        catch(Exception ex)when(ex is Win32Exception or InvalidOperationException or ArgumentException)
        {RestorationIssue="Field item instruction could not be restored: "+ex.Message+" Save and restart the game to reload its original code.";}

    }
    public void Restore(string? id=null)
    {
        if(id is null or "fieldItems")RestoreCode();
        foreach(var (a,s) in _saved.ToArray())
        {
            if(id!=null&&s.Id!=id)continue;
            try{if(Valid(s)&&memory.Read(a,s.Written.Length).SequenceEqual(s.Written))memory.Write(a,s.Original);}catch(Exception ex)when(ex is Win32Exception or ArgumentException or InvalidOperationException){}
            _saved.Remove(a);
        }
    }
    Feature Mark(string id,bool bound,string waiting,string current)
    {
        var f=features.Single(x=>x.Id==id);f.Available=layout.ExtrasSupported;f.Bound=f.Available&&bound;f.Waiting=f.Available?waiting:"game routine unavailable";f.Current=f.Bound?current:"—";return f;
    }
    public void Tick(bool inBattle)
    {
        if(!layout.ExtrasSupported)return;
        var root=new List<PointerGuard>();long save=Follow(layout.SaveSlot,root);
        // A save object also exists on the title screen. Require a populated owned stable.
        bool loaded=save!=0&&I(save+0x9A5C8) is >0 and <=1000;
        var buff=Mark("prayerBuff",loaded,"load a solo save",features.Single(f=>f.Id=="prayerBuff").ChoiceLabel);
        var defense=Mark("damageTaken",loaded,"load a solo save",features.Single(f=>f.Id=="damageTaken").MultiplierLabel+" incoming · next battle");
        if(buff.Active&&buff.Bound)Prayer(buff,save,0,(int)buff.CheckedTarget(),root);else Restore(buff.Id);
        if(defense.Active&&defense.Bound)
        {
            var path=new List<PointerGuard>();long manager=Follow(layout.ModuleBase+0x1DEBC10,path),table=manager==0?0:Follow(manager+0xC98,path);
            bool ready=false;
            if(table!=0&&I(table+0xD0) is >6 and <=64)
            {
                long rows=Q(table+0xE0);path.Add(new(table+0xE0,rows));long row=rows<65536?0:Follow(rows+6*8,path);
                if(row!=0&&I(row+0x48)==3)
                {
                    ready=true;float percent=(float)((1-defense.CheckedTarget())*100);
                    for(int i=0;i<20;i++)if(BitConverter.ToSingle(memory.Read(row+0x4C+i*4,4)) is >=0 and <=100)Write(defense.Id,row+0x4C+i*4,BitConverter.GetBytes(percent),path);
                }
            }
            defense.Bound=ready;if(ready)Prayer(defense,save,1,6,root);else defense.Waiting="waiting for Prayer Pot data";
        }
        else Restore(defense.Id);
        var field=Mark("fieldItems",loaded&&memory is IFieldItemCostMemory,"load a solo save","field-menu items · live check pending");
        if(field.Active&&field.Bound)
        {((IFieldItemCostMemory)memory).SetFieldItemCost(layout.ModuleBase,true);_fieldFree=true;}
        else RestoreCode();
        Gathering(save,inBattle,root);
    }
    void Prayer(Feature f,long save,int bank,int prayer,List<PointerGuard> guards)
    {
        long record=save+0x50D9EC+bank*0x30;
        if(I(record) is <0 or >11||I(record+8) is <0 or >8){f.Bound=false;f.Waiting="Prayer Pot record unavailable";return;}
        // Native prayer effects persist without a charm countdown. The two effect
        // banks are both read by the game's MAX-effect resolver; defense uses bank 1.
        Write(f.Id,record,BitConverter.GetBytes(0),guards);Write(f.Id,record+4,[0],guards);Write(f.Id,record+8,BitConverter.GetBytes(prayer),guards);
    }
    string Type(long obj)
    {
        long vt=Q(obj),fn=Q(vt+0x20);if(fn<layout.ModuleBase||fn>layout.ModuleBase+layout.ModuleSize-8)return "";
        byte[] code=memory.Read(fn,8);if(!code.AsSpan(0,3).SequenceEqual(new byte[]{0x48,0x8D,0x05})||code[7]!=0xC3)return "";
        long meta=fn+7+BitConverter.ToInt32(code,3);if(meta<layout.ModuleBase||meta>layout.ModuleBase+layout.ModuleSize-16)return "";
        long name=Q(meta+8);if(name<layout.ModuleBase||name>layout.ModuleBase+layout.ModuleSize-64)return "";
        return Encoding.ASCII.GetString(memory.Read(name,64)).Split('\0')[0];
    }
    void Gathering(long save,bool battle,List<PointerGuard> root)
    {
        var f=Mark("fastGather",false,"start a manual plant, ore or insect gathering action","4× gathering animation");
        if(!f.Active||battle||save==0){Restore(f.Id);return;}
        var path=new List<PointerGuard>(root);long player=Follow(save+0x33398,path),ctrl=player==0?0:Q(player+0x4888);
        long action=Object(ctrl)?Q(ctrl+0xD940):0;
        if(!Object(action)||Type(action) is not ("cPlActGather_PickUp" or "cPlActGather_Mining" or "cPlActGather_Net")||Q(action+0x30)!=player){Restore(f.Id);return;}
        float rate=BitConverter.ToSingle(memory.Read(player+0x66C,4));if(!float.IsFinite(rate)||rate is <=0 or >10){Restore(f.Id);return;}
        f.Bound=true;Write(f.Id,player+0x66C,BitConverter.GetBytes(4f),path);
    }
}
