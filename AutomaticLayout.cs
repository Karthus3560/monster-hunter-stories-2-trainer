using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace RidersToolkit;

internal sealed record CodeProof(long Address,byte[] Expected);
internal sealed record ItemDefinition(int Id,int Category,int Capacity,bool KeyItem);
internal sealed record AutomaticLayout(long ModuleBase,int ModuleSize,long BattleSlot,long SaveSlot,int ParameterOffset,int RidingOffset,int HeartsOffset,int TurnOffset,int InventoryOffset,List<CodeProof> Proofs)
{
    public bool RetreatSupported {get;init;}
    public bool FieldSupported {get;init;}
    public bool MovementSupported {get;init;}
    public bool ExperienceSupported {get;init;}
    public bool AdventureSupported {get;init;}
    public bool ExtrasSupported {get;init;}
    internal const string HealthPattern="48 8B 41 40 48 89 5C 24 20 ?? ?? 48 85 C0 74 ?? 48 8B ?? ?? ?? 00 00 48 85 ?? 74 ?? 48 8B ?? ?? ?? 00 00 48 85 ?? 74 ?? E8";
    internal const string HeartsPattern="0F B6 81 ?? ?? 00 00 48 8B D9 84 C0 75 ?? B0 01 48 83 C4";
    internal const string BattlePattern="48 8B 05 ?? ?? ?? ?? FF 80 ?? ?? 00 00 48 8B 0D ?? ?? ?? ?? 48 8D 81 ?? ?? 00 00 48 85 C0 74 ?? 66 FF 40 10";
    internal const string MoneyPattern="48 8B 0D ?? ?? ?? ?? 48 85 C9 74 ?? E8 ?? ?? ?? ?? 48 8B C8 E8 ?? ?? ?? ?? 8B C8 8D 90 81 69 67 FF";
    internal const string ItemsPattern="81 FF D0 07 00 00 73 ?? 48 8D 1C FB 48 83 C3 04 74 ?? 0F B7 43 02";
    internal const string PartyPattern="83 FA 01 77 ?? 48 63 C2 48 8B 44 C1 10 C3";
    internal const string GaugePattern="48 8B 43 48 48 85 C0 74 ?? 48 8B 88 ?? ?? 00 00 48 85 C9 74 ?? 0F B6 81 ?? ?? 00 00 48 83 C4 20 5B C3";
    internal const string GaugeChangePattern="0F B6 8A ?? ?? 00 00 EB ?? 32 C9 40 3A CE 48 8B 74 24 30 74 ?? 80 8B ?? ?? 00 00 04";
    public static AutomaticLayout Detect(IProcessMemory memory,long moduleBase,int moduleSize)
    {
        if(moduleSize is <4096 or >192*1024*1024) throw new InvalidDataException("Unexpected game module size.");
        var image=new byte[moduleSize];
        for(int offset=0;offset<moduleSize;offset+=65536)
        {
            int size=Math.Min(65536,moduleSize-offset);
            try
            {
                var bytes=memory.Read(moduleBase+offset,size); bytes.CopyTo(image,offset);
                if(bytes.Length==size) continue;
            }
            catch(Win32Exception) { }
            for(int page=0;page<size;page+=4096)
                try { memory.Read(moduleBase+offset+page,Math.Min(4096,size-page)).CopyTo(image,offset+page); } catch(Win32Exception) { }
        }
        return DetectImage(image,moduleBase);
    }
    internal static byte?[] Pattern(string text)=>text.Split(' ').Select(t=>t=="??"?(byte?)null:Convert.ToByte(t,16)).ToArray();
    internal static int Unique(byte[] image,string text)
    {
        var pattern=Pattern(text); int found=-1; byte first=pattern[0]!.Value; int at=0;
        while(at<=image.Length-pattern.Length)
        {
            at=Array.IndexOf(image,first,at); if(at<0 || at>image.Length-pattern.Length) break;
            bool matches=true; for(int i=1;i<pattern.Length;i++) if(pattern[i] is byte expected&&image[at+i]!=expected) { matches=false;break; }
            if(matches) { if(found>=0) throw new InvalidDataException("A game signature is ambiguous. Automatic controls were not enabled."); found=at; }
            at++;
        }
        return found>=0?found:throw new InvalidDataException("A required game routine is unavailable. Wait for the title screen and reconnect. Another trainer or a game update may also change it.");
    }
    internal static AutomaticLayout DetectImage(byte[] image,long moduleBase)
    {
        var proofs=new List<CodeProof>();
        int Find(string pattern) { int at=Unique(image,pattern); proofs.Add(new(moduleBase+at,image.AsSpan(at,Pattern(pattern).Length).ToArray())); return at; }
        int health=Find(HealthPattern),hearts=Find(HeartsPattern),battle=Find(BattlePattern),money=Find(MoneyPattern);
        Find(ItemsPattern); Find(PartyPattern);int gauge=Find(GaugePattern),changed=Find(GaugeChangePattern);
        long Rip(int instruction,int displacement,int length)
        {
            long offset=instruction+length+(long)BitConverter.ToInt32(image,instruction+displacement);
            if(offset<0 || offset>image.Length-8) throw new InvalidDataException("Game routine references an unexpected memory location.");
            return moduleBase+offset;
        }
        long battleSlot=Rip(battle,3,7);
        if(Rip(battle+13,3,7)!=battleSlot) throw new InvalidDataException("Battle manager references do not agree.");
        long saveSlot=Rip(money,3,7); int getter=checked(money+17+BitConverter.ToInt32(image,money+13));
        if(getter<0 || getter>image.Length-8 || !image.AsSpan(getter,3).SequenceEqual(new byte[]{0x48,0x8D,0x81}) || image[getter+7]!=0xC3)
            throw new InvalidDataException("The inventory accessor changed.");
        proofs.Add(new(moduleBase+getter,image.AsSpan(getter,8).ToArray()));
        int parameter=BitConverter.ToInt32(image,health+19),riding=BitConverter.ToInt32(image,health+31),heart=BitConverter.ToInt32(image,hearts+3),turn=BitConverter.ToInt32(image,battle+9),inventory=BitConverter.ToInt32(image,getter+3);
        // These structure fields were traced in the user's inspected 1.6.1 runtime.
        // Fail closed on another layout rather than interpreting arbitrary objects.
        if(parameter!=0xB0 || riding!=0x1FE8 || heart!=0x3E58 || turn!=0x237C || inventory!=0x23AE80 || BitConverter.ToInt32(image,gauge+12)!=parameter || BitConverter.ToInt32(image,gauge+24)!=riding+8 || BitConverter.ToInt32(image,changed+3)!=riding+8 || BitConverter.ToInt32(image,changed+23)!=heart+4)
            throw new InvalidDataException("This game uses a different battle or inventory structure.");
        int[] retreatSites=[0xD368CA,0xD36F5A,0xD37589,0xD37BB9];
        byte[] retreatCode=Convert.FromHexString("6BCA64442BC10FB78F481F0000443BC17307808B6845000001");
        (int Rva,string Hex)[] retreatAccessors=[
            (0xB89E16,"488B54C1104885D2740F4183F80377094963C0488B44C220C3"),
            (0xCF0542,"488D1480488D0CD1418BD04881C1C0090000"),
            (0xCF64D0,"80B9FC310000007515488B81980F00004885C07409807833007403B001C3"),
            (0xD36887,"488B8348450000488BBBB00000004885C07449807852007443")
        ];
        bool retreatSupported=retreatSites.All(at=>at<=image.Length-retreatCode.Length&&image.AsSpan(at,retreatCode.Length).SequenceEqual(retreatCode))
            &&retreatAccessors.All(s=>s.Rva<=image.Length-Convert.FromHexString(s.Hex).Length&&image.AsSpan(s.Rva,Convert.FromHexString(s.Hex).Length).SequenceEqual(Convert.FromHexString(s.Hex)));
        if(retreatSupported)
        {
            foreach(int at in retreatSites)proofs.Add(new(moduleBase+at,retreatCode));
            foreach(var sample in retreatAccessors)proofs.Add(new(moduleBase+sample.Rva,Convert.FromHexString(sample.Hex)));
        }
        bool fieldSupported=ExplorationController.Detect(image,moduleBase,proofs);
        bool movementSupported=MovementController.Detect(image,moduleBase,proofs);
        byte[] expCode=Convert.FromHexString("488B8EB00000004885C90F84890300008B81E81F000041B83F420F00488B154F160B0101820C230000");
        bool experienceSupported=image.Length>=0xD3AEEE+expCode.Length&&image.AsSpan(0xD3AEEE,expCode.Length).SequenceEqual(expCode);
        if(experienceSupported)proofs.Add(new(moduleBase+0xD3AEEE,expCode));
        bool adventureSupported=fieldSupported&&AdventureController.Detect(image,moduleBase,proofs);
        bool extrasSupported=ExtraController.Detect(image,moduleBase,proofs);
        return new(moduleBase,image.Length,battleSlot,saveSlot,parameter,riding,heart,turn,inventory,proofs){RetreatSupported=retreatSupported,FieldSupported=fieldSupported,MovementSupported=movementSupported,ExperienceSupported=experienceSupported,AdventureSupported=adventureSupported,ExtrasSupported=extrasSupported};
    }
    public bool Verify(IProcessMemory memory)=>Proofs.All(p=>memory.Read(p.Address,p.Expected.Length).SequenceEqual(p.Expected));
    public static IReadOnlyList<ItemDefinition> LoadItems(string game)
    {
        var archive=new GameArchive(GameDataMods.Original(game));
        var table=new XfsTable(archive.Entries.Single(e=>e.Name==@"table\item_data").Extract());
        var items=table.Rows.Select(r=>new ItemDefinition((int)r.Number("mItemId"),(int)r.Number("mCategoryType"),(int)r.Number("mCapa"),r.Number("mIsGimmickKeyItem")!=0)).ToArray();
        if(items.Any(i=>i.Id is <0 or >=2000 || i.Category is <0 or >5 || i.Capacity is <0 or >65535)||items.Select(i=>i.Id).Distinct().Count()!=items.Length)
            throw new InvalidDataException("The item catalog could not be validated.");
        return items;
    }
}

internal sealed class AutomaticConnection : IDisposable
{
    public required ProcessMemory Memory {get;init;}
    public required AutomaticLayout Layout {get;init;}
    public required IReadOnlyList<ItemDefinition> Items {get;init;}
    public int Pid=>Memory.ProcessId;
    public static AutomaticConnection Open(Process process,string game)
    {
        if(Storage.Hash(Path.Combine(game,"game.exe"))!=Storage.KnownHash) { process.Dispose(); throw new InvalidOperationException("The game executable changed. Automatic controls need a compatibility review."); }
        var memory=new ProcessMemory(process);
        try
        {
            var module=process.MainModule ?? throw new InvalidOperationException("The game module is still loading.");
            return new(){Memory=memory,Layout=AutomaticLayout.Detect(memory,module.BaseAddress.ToInt64(),module.ModuleMemorySize),Items=AutomaticLayout.LoadItems(game)};
        }
        catch { memory.Dispose();throw; }
    }
    public void Dispose()=>Memory.Dispose();
}
