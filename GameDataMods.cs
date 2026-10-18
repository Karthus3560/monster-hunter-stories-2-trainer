using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RidersToolkit;

public sealed record LuckSettings(int Eggs,int Dens,bool NoLostEggs,bool FreeCrafting=false,bool FreeForging=false,bool GenesLevelOne=false)
{
    public bool IsStock=>Eggs==0&&Dens==0&&!NoLostEggs&&!FreeCrafting&&!FreeForging&&!GenesLevelOne;
    public void Validate() { if(Eggs is <0 or >4 || Dens is <0 or >5) throw new ArgumentException("Choose one of the listed luck settings."); }
}
internal sealed record LuckPlan(byte[] Archive,int EggGroups,int DenGroups,int LostGroups,string Example);
internal sealed record LuckState(string ArchivePath,string OriginalHash,string AppliedHash,LuckSettings Settings,string? PreviousHash=null);

internal static class GameDataMods
{
    public const string StockHash="95F699316078F44C1E18C21A90DBF245EA6B6FEE1654E0115B39F508110DE7C4";
    const string EggsTable=@"table\dungeon_nest_rareness_tbl",DensTable=@"table\dungeon_create_ptn_tbl";
    public static string ArchivePath(string game)=>Path.Combine(game,"nativeDX11x64","archive","table","table.arc");
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    static string Folder(string path)=>Path.Combine(Storage.Root,"GameData",Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()))[..16]);
    static LuckState? State(string path)
    {
        var file=Path.Combine(Folder(path),"state.json");
        if(!File.Exists(file)) return null;
        var state=JsonSerializer.Deserialize<LuckState>(File.ReadAllText(file)) ?? throw new InvalidDataException("Unreadable game-data backup record.");
        if(!string.Equals(state.ArchivePath,Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)||state.OriginalHash!=StockHash)
            throw new InvalidDataException("Game-data backup belongs to a different installation.");
        return state;
    }
    public static string Status(string? game)
    {
        if(game is null) return "Install not found · refresh Steam detection";
        string path=ArchivePath(game); if(!File.Exists(path)) return "Game table archive is missing";
        var hash=Storage.Hash(path);
        if(hash==StockHash) return "Not applied · game is using its original egg and den odds";
        var state=State(path);
        if(state?.AppliedHash==hash)
        {
            state.Settings.Validate();
            string[] eggs=["original","lucky","very lucky","highest available","rainbow solo eggs"],dens=["original","more rare","rare hunt","super-rare hunt","all rare","best unlocked rare"];
            return $"Luck settings installed · eggs: {eggs[state.Settings.Eggs]} · dens: {dens[state.Settings.Dens]} · workshop: {(state.Settings.FreeCrafting||state.Settings.FreeForging||state.Settings.GenesLevelOne?"on":"off")} · restart required";
        }
        if(state?.PreviousHash==hash) return "Luck settings installed · previous settings retained after interrupted update";
        return "Game data differs · changes blocked to preserve other mods";
    }
    public static LuckSettings? InstalledSettings(string? game)
    {
        if(game is null) return null;
        string path=ArchivePath(game); if(!File.Exists(path)) return null;
        string hash=Storage.Hash(path); if(hash==StockHash) return new(0,0,false);
        var state=State(path);
        if(state?.AppliedHash!=hash) return null;
        state.Settings.Validate(); return state.Settings;
    }
    public static byte[] Original(string game)
    {
        var path=ArchivePath(game); var current=File.ReadAllBytes(path); var hash=Hash(current);
        if(hash==StockHash) return current;
        var state=State(path);
        if(state is null || (state.AppliedHash!=hash && state.PreviousHash!=hash)) throw new InvalidOperationException("The table archive changed outside this trainer. Restore or review your other mods before applying these settings.");
        var original=File.ReadAllBytes(Path.Combine(Folder(path),"original.table.arc"));
        if(Hash(original)!=StockHash) throw new InvalidDataException("The original game-data backup did not pass verification.");
        return original;
    }
    public static LuckPlan Build(byte[] original,LuckSettings settings)
    {
        settings.Validate();
        if(Hash(original)!=StockHash) throw new InvalidDataException("Luck settings currently support only the inspected Stories 2 1.6.1 table archive.");
        var archive=new GameArchive(original); var replacements=new Dictionary<string,byte[]>();
        var eggs=new XfsTable(archive.Entries.Single(e=>e.Name==EggsTable).Extract());
        var dens=new XfsTable(archive.Entries.Single(e=>e.Name==DensTable).Extract());
        if(eggs.Rows.Count!=13 || dens.Rows.Count!=30) throw new InvalidDataException("The game's egg or den schema differs from the inspected build.");
        int eggGroups=0,denGroups=0,lostGroups=0;
        foreach(var row in eggs.Rows)
        {
            // Rainbow mode includes the solo retreat-nest row. Earlier presets
            // retain their original scope; multiplayer generation stays untouched.
            if(row.Number("mMultiDungeon")!=0 || row.Number("mHomingNest")!=0&&settings.Eggs!=4) continue;
            for(int i=0;i<4;i++)
            {
                var names=new[]{"mProbEggNormal","mProbEggRare","mProbEggSRare"}.Select(n=>$"{n}[{i}]").ToArray();
                var values=names.Select(row.Number).ToArray();
                var factors=settings.Eggs switch {1=>new double[]{1,3,10},2=>[1,6,35],_=>[1,1,1]};
                // These three weights feed the nest's separate quality roll
                // (+29D0..+29D8 in the inspected runtime), not its species list.
                // Unlike "highest available", rainbow mode also replaces a zero
                // SRare quality weight, including the first normal-nest pull.
                if(settings.Eggs!=0 && SetWeights(eggs,row,names,settings.Eggs==4?Exclusive(values,2):Reweight(values,factors,settings.Eggs==3))) eggGroups++;
                string lost=$"mProbEggLost[{i}]";
                if(settings.NoLostEggs && row.Number(lost)>0) { eggs.Set(row,lost,0); lostGroups++; }
            }
        }
        string example=settings.Eggs==4?"Rainbow preset: every solo quality group selects the highest (SRare) egg quality, including retreat nests. Species and story unlocks are unchanged. Rainbow glow still needs an in-game check.":"";
        string denExample="No den adjustment selected.";
        foreach(var row in dens.Rows)
        {
            string[] names=["mDungeonProbNormal","mDungeonProbRare","mDungeonProbSRare","mDungeonProbHigher"];
            var values=names.Select(row.Number).ToArray();
            var factors=settings.Dens switch {1=>new double[]{1,5,20,1},2=>[1,20,200,1],3=>[1,20,2000,1],_=>[1,1,1,1]};
            var changed=settings.Dens>=4?Exclusive(values,settings.Dens==5&&values[2]>0?2:values[1]>0?1:-1):Reweight(values,factors,false);
            if(settings.Dens!=0 && SetWeights(dens,row,names,changed)) denGroups++;
            if(settings.Dens!=0 && values[2]>0)
            {
                double total=values.Sum(v=>(double)v);
                denExample=$"Example eligible area: rare {values[1]/total:P1} → {changed[1]/total:P1}; super-rare {values[2]/total:P1} → {changed[2]/total:P1}. Area and story rules still apply.";
            }
        }
        var originalEggs=archive.Entries.Single(e=>e.Name==EggsTable).Extract();
        var originalDens=archive.Entries.Single(e=>e.Name==DensTable).Extract();
        if(!eggs.Bytes.SequenceEqual(originalEggs)) replacements.Add(EggsTable,eggs.Bytes);
        if(!dens.Bytes.SequenceEqual(originalDens)) replacements.Add(DensTable,dens.Bytes);
        int workshop=0;
        foreach(var entry in archive.Entries.Where(e=>
            settings.FreeCrafting&&e.Name==@"table\item_mix" ||
            settings.FreeForging&&e.Name is @"table\armor_param" or @"table\wep_param" ||
            settings.GenesLevelOne&&e.Name==@"table\gene_table"))
        {
            var table=new XfsTable(entry.Extract()); int changed=0;
            foreach(var row in table.AllRows)
            {
                string[] fields=entry.Name==@"table\item_mix"?["mItemNum[0]","mItemNum[1]","mItemNum[2]"]:
                    entry.Name==@"table\gene_table"?["mOpenLevel"]:
                    ["mForgeDefaultPoint[0]","mForgeDefaultPoint[1]","mDefaultForgePrice","mPrice"];
                foreach(string field in fields)
                    if(row.Fields.TryGetValue(field,out var f)&&Convert.ToDouble(f.Value)> (settings.GenesLevelOne&&field=="mOpenLevel"?1:0))
                    {table.Scalar(row,field,field=="mOpenLevel"?1:0);changed++;}
            }
            if(changed==0)throw new InvalidDataException("Workshop table has no expected fields: "+entry.Name);
            replacements.Add(entry.Name,table.Bytes); workshop+=changed;
        }
        if(workshop>0) example+=$"\nWorkshop: {workshop:N0} recipe, forge or gene-level values adjusted. Recipes and story unlocks are retained.";
        var modified=replacements.Count==0?original:archive.Repack(replacements);
        // Reopen and compare every entry before any installation can be offered.
        var verified=new GameArchive(modified);
        for(int i=0;i<archive.Entries.Count;i++)
        {
            var before=archive.Entries[i]; var after=verified.Entries[i];
            if(before.Name!=after.Name) throw new InvalidDataException("Archive ordering changed unexpectedly.");
            if(replacements.TryGetValue(before.Name,out var expected))
            { if(!after.Extract().SequenceEqual(expected)) throw new InvalidDataException("A modified table failed round-trip verification."); }
            else if(!before.Header[..140].SequenceEqual(after.Header[..140]) || !before.Compressed.SequenceEqual(after.Compressed))
                throw new InvalidDataException("An unrelated archive entry changed.");
        }
        return new(modified,eggGroups,denGroups,lostGroups,string.Join("\n\n",new[]{example,denExample}.Where(s=>s.Length>0)));
    }
    static bool SetWeights(XfsTable table,XfsRow row,string[] names,uint[] updated)
    {
        bool changed=false;
        for(int i=0;i<names.Length;i++) if(row.Number(names[i])!=updated[i]) { table.Set(row,names[i],updated[i]); changed=true; }
        return changed;
    }
    internal static uint[] Exclusive(uint[] values,int selected)
    {
        if(selected<0) return (uint[])values.Clone();
        if(selected>=values.Length) throw new ArgumentOutOfRangeException(nameof(selected));
        ulong total=values.Aggregate(0UL,(sum,value)=>sum+value);
        if(total>uint.MaxValue) throw new InvalidDataException("Probability total is too large.");
        var output=new uint[values.Length]; output[selected]=(uint)total; return output;
    }
    public static uint[] Reweight(uint[] values,double[] factors,bool best)
    {
        if(values.Length!=factors.Length || factors.Any(f=>!double.IsFinite(f)||f<=0)) throw new ArgumentException("Invalid probability weights.");
        ulong total=values.Aggregate(0UL,(a,v)=>a+v); if(total>uint.MaxValue) throw new InvalidDataException("Probability total is too large.");
        var output=new uint[values.Length]; if(total==0) return output;
        if(best) { output[Array.FindLastIndex(values,v=>v>0)]=(uint)total; return output; }
        double weighted=values.Select((v,i)=>v*factors[i]).Sum();
        var fractions=new double[values.Length]; uint used=0;
        for(int i=0;i<values.Length;i++) { double exact=total*(values[i]*factors[i]/weighted); output[i]=(uint)Math.Floor(exact); fractions[i]=exact-output[i]; used+=output[i]; }
        // Largest remainder keeps the original sum exactly, including percentage tables.
        foreach(var i in Enumerable.Range(0,values.Length).Where(i=>values[i]>0).OrderByDescending(i=>fractions[i]).Take(checked((int)(total-used)))) output[i]++;
        return output;
    }
    public static bool IsGameRunning(string game)
    {
        string exe=Path.Combine(game,"game.exe");
        foreach(var p in Process.GetProcessesByName("game"))
            using(p) { try { if(string.Equals(p.MainModule?.FileName,exe,StringComparison.OrdinalIgnoreCase)) return true; } catch { return true; } }
        return false;
    }
    public static void Install(string game,LuckSettings settings)
    {
        if(IsGameRunning(game)) throw new InvalidOperationException("Close Stories 2 before applying or restoring game-data settings.");
        if(Storage.Hash(Path.Combine(game,"game.exe"))!=Storage.KnownHash) throw new InvalidOperationException("The game version changed. These settings need a compatibility review.");
        InstallCore(game,settings,()=>IsGameRunning(game));
    }
    internal static void InstallCore(string game,LuckSettings settings,Func<bool> running)
    {
        if(running()) throw new InvalidOperationException("Close Stories 2 before applying game data.");
        var path=ArchivePath(game); var directory=Folder(path); Directory.CreateDirectory(directory);
        using var operationLock=new FileStream(Path.Combine(directory,"operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var original=Original(game); var expected=Storage.Hash(path); var plan=Build(original,settings);
        string backup=Path.Combine(directory,"original.table.arc");
        if(!File.Exists(backup)) File.WriteAllBytes(backup,original);
        if(Storage.Hash(backup)!=StockHash) throw new InvalidDataException("The original archive backup failed verification.");
        // Record the intended hash before replacing, so even an interrupted operation
        // can identify either the untouched original or this trainer's exact output.
        var state=new LuckState(Path.GetFullPath(path),StockHash,Hash(plan.Archive),settings,expected);
        string statePath=Path.Combine(directory,"state.json"), stateTemp=statePath+".tmp";
        File.WriteAllText(stateTemp,JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true})); File.Move(stateTemp,statePath,true);
        ReplaceVerified(path,plan.Archive,expected,running);
    }
    internal static void ReplaceVerified(string path,byte[] contents,string expected,Func<bool> running)
    {
        string temp=path+".riders-"+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { stream.Write(contents); stream.Flush(true); }
            if(Storage.Hash(temp)!=Hash(contents)) throw new IOException("Staged game data did not verify.");
            if(running()) throw new InvalidOperationException("Close Stories 2 before replacing game data.");
            if(Storage.Hash(path)!=expected) throw new InvalidOperationException("The archive changed while preparing settings. Nothing was replaced.");
            File.Replace(temp,path,null);
            if(Storage.Hash(path)!=Hash(contents)) throw new IOException("Installed game data did not verify. Restore the original archive backup.");
        }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
}
