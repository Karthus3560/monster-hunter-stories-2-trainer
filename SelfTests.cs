using System.Diagnostics;
using System.IO;

namespace RidersToolkit;

internal static class SelfTests
{
    static readonly List<string> Results=[];
    static int _failures,_skipped;
    static bool _portable;
    static readonly HashSet<string> InstalledChecks=[
        "Workshop archive rebuild keeps rainbow eggs, zeros costs only, and lowers gene requirements",
        "Installed archive preview, schema validation and all-entry preservation",
        "Rainbow and rare-only presets cover solo pulls and preserve den unlocks",
        "Luck installation, update, interruption recovery and exact restore on copies",
        "Item category metadata comes from the user's installed archive"];

    static void Check(string name,Action run)
    {
        if(_portable&&InstalledChecks.Contains(name)){_skipped++;Results.Add("SKIP  "+name+" (requires installed game)");return;}
        try { run(); Results.Add("PASS  "+name); }
        catch(Exception ex) { _failures++; Results.Add("FAIL  "+name+": "+ex.Message); }
    }
    static void Require(bool condition,string message="Assertion failed") { if(!condition) throw new Exception(message); }
    static void Reject(Action action) { bool rejected=false; try { action(); } catch(ArgumentException) { rejected=true; } catch(InvalidOperationException) { rejected=true; } Require(rejected,"Expected input to be rejected"); }
    public static int Run(bool portable=false)
    {
        _portable=portable;_failures=0;_skipped=0;Results.Clear();
        // These tests exercise asynchronous file I/O; do not block its continuations on WPF's startup dispatcher.
        Task.Run(()=>AppUpdateTests.Run(Check)).GetAwaiter().GetResult();
        ModifierTests.Run(Check);
        AdventureTests.Run(Check);
        BorderlessWindowTests.Run(Check);
        ExtraTests.Run(Check);
        EggAdditionTests.Run(Check);
        OfflineEditorTests.Run(Check);
        Check("Integer, counter and float encode/decode",()=>
        {
            foreach(var kind in Enum.GetValues<NumberKind>()) Require(Numbers.Decode(Numbers.Encode(42,kind),kind)==42);
            Require(Numbers.Decode(Numbers.Encode(2.5,NumberKind.Float32),NumberKind.Float32)==2.5);
        });
        Check("Invalid, nonfinite and overflowing inputs rejected",()=>
        {
            Reject(()=>Numbers.Encode(-1,NumberKind.Int32)); Reject(()=>Numbers.Encode(1.5,NumberKind.Int32));
            Reject(()=>Numbers.Encode(double.NaN,NumberKind.Float32)); Reject(()=>Numbers.Encode(256,NumberKind.Byte));
            Reject(()=>Numbers.Encode(65536,NumberKind.UInt16)); Reject(()=>Numbers.Parse("NaN"));
        });
        Check("Executable / guard pages are never writable",()=>
        { Require(ProcessMemory.Writable(4)); Require(ProcessMemory.Writable(8)); Require(!ProcessMemory.Writable(0x40)); Require(!ProcessMemory.Writable(0x104)); });
        Check("First scan preserves ambiguity",()=>
        {
            using var memory=new FakeMemory(); memory.Put(0,123456); memory.Put(16,123456);
            var scan=ValueScanner.First(memory,123456,NumberKind.Int32,default);
            Require(scan.Addresses.Count==2); Require(!ValueScanner.CanBind(scan));
        });
        Check("Changed exact second search isolates one candidate",()=>
        {
            using var memory=new FakeMemory(); memory.Put(0,123456); memory.Put(16,123456);
            var first=ValueScanner.First(memory,123456,NumberKind.Int32,default); memory.Put(16,123455);
            var result=ValueScanner.Refine(memory,first,123455,"exact",default);
            Require(ValueScanner.CanBind(result)); Require(result.Addresses[0]==memory.Base+16);
        });
        Check("Unchanged observations cannot unlock editing",()=>
        {
            using var memory=new FakeMemory(); memory.Put(0,123456);
            var first=ValueScanner.First(memory,123456,NumberKind.Int32,default);
            Require(!ValueScanner.CanBind(ValueScanner.Refine(memory,first,123456,"exact",default)));
        });
        Check("Cancellation stops memory scanning",()=>
        {
            using var memory=new FakeMemory(); using var cancel=new CancellationTokenSource(); cancel.Cancel();
            bool caught=false; try { ValueScanner.First(memory,99,NumberKind.Int32,cancel.Token); } catch(OperationCanceledException) { caught=true; } Require(caught);
        });
        Check("Binding rechecks the most recent observation",()=>
        {
            var feature=Catalog.Create()[0]; using var engine=new TrainerEngine([feature]); var memory=new FakeMemory(); engine.AttachTest(memory); memory.Put(0,70);
            var result=new ScanResult([memory.Base],4,70,NumberKind.Int32,2,true); memory.Put(0,60);
            Reject(()=>engine.Bind(feature,result)); Require(!feature.Bound);
        });
        Check("Apply, freeze and Disable all operate on real values",()=>
        {
            var feature=Catalog.Create()[0]; using var engine=new TrainerEngine([feature]); var memory=new FakeMemory(); engine.AttachTest(memory); memory.Put(0,70);
            engine.Bind(feature,new([memory.Base],4,70,NumberKind.Int32,2,true)); feature.Target="100"; engine.Apply(feature); Require(memory.Get(0)==100);
            feature.Active=true; memory.Put(0,22); engine.Tick(); Require(memory.Get(0)==100);
            engine.StopAll(); memory.Put(0,65); engine.Tick(); Require(memory.Get(0)==65 && !feature.Active);
        });
        Check("Writes outside the feature's bounds are rejected",()=>
        {
            var feature=Catalog.Create()[2]; using var engine=new TrainerEngine([feature]); var memory=new FakeMemory(); engine.AttachTest(memory); memory.Put(0,60);
            engine.Bind(feature,new([memory.Base],4,60,NumberKind.Int32,2,true)); feature.Target="999";
            Reject(()=>engine.Apply(feature)); Require(memory.Get(0)==60);
        });
        Check("Region replacement invalidates a maintained value",()=>
        {
            var feature=Catalog.Create()[0]; using var engine=new TrainerEngine([feature]); var memory=new FakeMemory(); engine.AttachTest(memory); memory.Put(0,70);
            engine.Bind(feature,new([memory.Base],4,70,NumberKind.Int32,2,true)); feature.Active=true; memory.Generation++;
            engine.Tick(); Require(!feature.Bound&&!feature.Active); Require(memory.Get(0)==70);
        });
        Check("Overlapping controls cannot share a location",()=>
        {
            var f=Catalog.Create(); using var engine=new TrainerEngine([f[0],f[1]]); var memory=new FakeMemory(); engine.AttachTest(memory); memory.Put(0,70);
            engine.Bind(f[0],new([memory.Base],4,70,NumberKind.Int32,2,true)); Reject(()=>engine.Bind(f[1],new([memory.Base],4,70,NumberKind.Int32,2,true)));
        });
        Check("EXP multiplier applies each observed gain only once",()=>
        {
            var feature=new Feature{Id="riderXp",Title="Numeric EXP fixture",Description="Development fixture",Category="Development",Glyph="",Maximum=230000000,Target="1000"}; using var engine=new TrainerEngine([feature]); var memory=new FakeMemory(); engine.AttachTest(memory); memory.Put(0,1000);
            engine.Bind(feature,new([memory.Base],4,1000,NumberKind.Int32,2,true)); feature.Active=true; engine.ExperienceMultiplier=3;
            memory.Put(0,1010); engine.Tick(); Require(memory.Get(0)==1030); engine.Tick(); Require(memory.Get(0)==1030);
            memory.Put(0,500); engine.Tick(); Require(memory.Get(0)==500);
        });
        Check("Disconnect clears every location and active control",()=>
        {
            var feature=Catalog.Create()[0]; using var engine=new TrainerEngine([feature]); var memory=new FakeMemory(); engine.AttachTest(memory); memory.Put(0,70);
            engine.Bind(feature,new([memory.Base],4,70,NumberKind.Int32,2,true)); feature.Active=true; memory.Alive=false; engine.Tick(); Require(!feature.Active&&!feature.Bound&&engine.Memory==null);
        });
        Check("Actual Windows child-process read, scan, refine and write",RealProcessTest);
        Check("Float searches use representable 32-bit values",()=>
        {
            using var memory=new FakeMemory(); memory.Write(memory.Base,Numbers.Encode(0.1,NumberKind.Float32));
            var first=ValueScanner.First(memory,0.1,NumberKind.Float32,default);
            memory.Write(memory.Base,Numbers.Encode(0.2,NumberKind.Float32));
            var second=ValueScanner.Refine(memory,first,0.2,"exact",default);
            Require(ValueScanner.CanBind(second)); Require(second.Value==(double)(float)0.2);
        });
        Check("Backups, verified restore, tamper detection and profiles",StorageTest);
        Check("Steam manifests detect custom library locations and launch arguments",SteamTest);
        Check("Probability adjustments preserve totals and unavailable tiers",()=>
        {
            var random=new Random(1729);
            for(int n=0;n<1000;n++)
            {
                uint[] values=Enumerable.Range(0,4).Select(_=>(uint)random.Next(1000)).ToArray(); values[n%4]=0;
                var adjusted=GameDataMods.Reweight(values,[1,20,2000,1],false);
                Require(adjusted.Sum(v=>(long)v)==values.Sum(v=>(long)v)); Require(adjusted[n%4]==0);
                var best=GameDataMods.Reweight(values,[1,1,1,1],true);
                Require(best.Sum(v=>(long)v)==values.Sum(v=>(long)v)); Require(best[Array.FindLastIndex(values,v=>v>0)]==values.Sum(v=>(long)v));
            }
            Require(GameDataMods.Reweight([90,10,0],[1,3,10],false).SequenceEqual(new uint[]{75,25,0}));
            Require(GameDataMods.Reweight([0,0,0],[1,3,10],true).All(v=>v==0));
        });
        Check("Archive replacement is atomic, verified and rejects concurrent changes",ReplacementTest);
        Check("Installed archive preview, schema validation and all-entry preservation",ArchiveTest);
        Check("Rainbow and rare-only presets cover solo pulls and preserve den unlocks",RainbowTest);
        Check("Luck installation, update, interruption recovery and exact restore on copies",LuckInstallationTest);
        AutomaticTests.Run(Check);
        ExplorationTests.Run(Check);
        var report=string.Join(Environment.NewLine,Results)+$"{Environment.NewLine}{Results.Count-_failures-_skipped}/{Results.Count-_skipped} passed. {_skipped} installed-game checks skipped. No game or save files were changed.";
        Directory.CreateDirectory(Storage.Root); File.WriteAllText(Path.Combine(Storage.Root,"self-test-report.txt"),report);
        return _failures==0?0:1;
    }
    static void SteamTest()
    {
        string temp=Path.Combine(Path.GetTempPath(),"riders-steamtest-"+Guid.NewGuid().ToString("N"));
        try
        {
            string steam=Path.Combine(temp,"steam"),library=Path.Combine(temp,"another library"),game=Path.Combine(library,"steamapps","common","My Stories 2");
            Directory.CreateDirectory(Path.Combine(steam,"steamapps")); Directory.CreateDirectory(game);
            File.WriteAllBytes(Path.Combine(steam,"steam.exe"),[]); File.WriteAllBytes(Path.Combine(game,"game.exe"),[]);
            string escaped=library.Replace("\\","\\\\");
            File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"// libraries\n\"libraryfolders\" { \"0\" { \"path\" \""+escaped+"\" } }");
            string manifest=Path.Combine(library,"steamapps","appmanifest_1277400.acf");
            File.WriteAllText(manifest,"\"AppState\" { \"appid\" \"1277400\" \"installdir\" \"My Stories 2\" }");
            var found=SteamInstallation.Find([steam]); Require(found?.GameDirectory==game);
            var launch=SteamInstallation.LaunchInfo(found!); Require(launch.FileName==Path.Combine(steam,"steam.exe"));
            Require(launch.ArgumentList.SequenceEqual(new[]{"-applaunch","1277400"})); Require(!launch.UseShellExecute);
            File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\" { \"1\" \""+escaped+"\" }");
            Require(SteamInstallation.Find([steam]) is not null,"Older library format failed");
            File.WriteAllText(manifest,"\"AppState\" { \"appid\" \"1277400\" \"installdir\" \"..\\\\..\\\\escape\" }");
            Require(SteamInstallation.Find([steam]) is null,"Escaping install directory was accepted");
        }
        finally { RemoveScratch(temp,"riders-steamtest-"); }
    }
    static void RemoveScratch(string path,string prefix)
    {
        var parent=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if(Directory.GetParent(Path.GetFullPath(path))?.FullName==parent&&Path.GetFileName(path).StartsWith(prefix,StringComparison.Ordinal)&&Directory.Exists(path)) Directory.Delete(path,true);
    }
    static void ReplacementTest()
    {
        string temp=Path.Combine(Path.GetTempPath(),"riders-replacetest-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            string file=Path.Combine(temp,"fixture.arc"); byte[] original=[1,2,3,4],modified=[5,6,7,8]; File.WriteAllBytes(file,original);
            string hash=Storage.Hash(file); Reject(()=>GameDataMods.ReplaceVerified(file,modified,hash,()=>true));
            Require(File.ReadAllBytes(file).SequenceEqual(original));
            Reject(()=>GameDataMods.ReplaceVerified(file,modified,new string('0',64),()=>false)); Require(File.ReadAllBytes(file).SequenceEqual(original));
            GameDataMods.ReplaceVerified(file,modified,hash,()=>false); Require(File.ReadAllBytes(file).SequenceEqual(modified));
            GameDataMods.ReplaceVerified(file,original,Storage.Hash(file),()=>false); Require(File.ReadAllBytes(file).SequenceEqual(original));
            Require(Directory.GetFiles(temp).Length==1,"Staging files were left behind");
        }
        finally { RemoveScratch(temp,"riders-replacetest-"); }
    }
    static void ArchiveTest()
    {
        var game=SteamInstallation.Find() ?? throw new Exception("Installed Stories 2 needed for this read-only archive test");
        var path=GameDataMods.ArchivePath(game.GameDirectory); var before=Storage.Hash(path);
        var original=GameDataMods.Original(game.GameDirectory); var archive=new GameArchive(original);
        Require(archive.Entries.Count==2598);
        Require(GameArchive.Crypt(GameArchive.Crypt(original[8..],false),true).SequenceEqual(original[8..]),"Archive encryption does not round-trip");
        Require(GameDataMods.Build(original,new(0,0,false)).Archive.SequenceEqual(original),"Original preset changed bytes");
        foreach(var setting in new[]{new LuckSettings(1,1,false),new LuckSettings(2,2,true),new LuckSettings(3,3,true),new LuckSettings(4,4,true),new LuckSettings(4,5,false)})
        {
            var plan=GameDataMods.Build(original,setting); Require(plan.EggGroups>0&&plan.DenGroups>0); var modified=new GameArchive(plan.Archive);
            int changed=0;
            for(int i=0;i<archive.Entries.Count;i++)
            {
                var oldEntry=archive.Entries[i]; var newEntry=modified.Entries[i];
                if(oldEntry.Compressed.SequenceEqual(newEntry.Compressed)) continue;
                changed++; bool egg=oldEntry.Name==@"table\dungeon_nest_rareness_tbl";
                Require(egg||oldEntry.Name==@"table\dungeon_create_ptn_tbl","Unrelated table changed");
                var oldTable=new XfsTable(oldEntry.Extract()); var newTable=new XfsTable(newEntry.Extract()); var allowed=new HashSet<int>();
                Require(oldTable.Rows.Count==newTable.Rows.Count);
                for(int r=0;r<oldTable.Rows.Count;r++)
                {
                    var oldRow=oldTable.Rows[r]; var newRow=newTable.Rows[r];
                    bool excluded=egg&&(oldRow.Number("mMultiDungeon")!=0||oldRow.Number("mHomingNest")!=0&&setting.Eggs!=4);
                    foreach(var field in oldRow.Fields.Values)
                    {
                        bool probability=egg?field.Name.StartsWith("mProbEgg"):field.Name.StartsWith("mDungeonProb");
                        if(probability&&!excluded) for(int k=0;k<4;k++) allowed.Add(field.Offset+k);
                    }
                    if(excluded) continue;
                    for(int k=0;k<(egg?4:1);k++)
                    {
                        string[] names=egg?new[]{"mProbEggNormal","mProbEggRare","mProbEggSRare"}.Select(n=>$"{n}[{k}]").ToArray():["mDungeonProbNormal","mDungeonProbRare","mDungeonProbSRare","mDungeonProbHigher"];
                        var oldWeights=names.Select(oldRow.Number).ToArray(); var newWeights=names.Select(newRow.Number).ToArray();
                        Require(oldWeights.Sum(v=>(long)v)==newWeights.Sum(v=>(long)v));
                        for(int j=0;j<names.Length;j++) if(oldWeights[j]==0&&!(egg&&setting.Eggs==4&&j==2)) Require(newWeights[j]==0,"Unavailable tier unlocked");
                        if(egg&&setting.Eggs==3&&oldWeights.Any(v=>v>0)) Require(newWeights.Count(v=>v>0)==1,"Best egg tier was not isolated");
                        if(egg&&setting.NoLostEggs) Require(newRow.Number($"mProbEggLost[{k}]")==0);
                        if(egg&&!setting.NoLostEggs) Require(newRow.Number($"mProbEggLost[{k}]")==oldRow.Number($"mProbEggLost[{k}]"));
                    }
                }
                for(int at=0;at<oldTable.Bytes.Length;at++) if(!allowed.Contains(at)) Require(oldTable.Bytes[at]==newTable.Bytes[at],"Unexpected table byte changed");
            }
            Require(changed==2,"Expected only the two selected tables to change");
        }
        Require(Storage.Hash(path)==before,"Installed game file changed during preview");
    }
    static void RainbowTest()
    {
        var game=SteamInstallation.Find() ?? throw new Exception("Installed Stories 2 needed for read-only preview");
        string path=GameDataMods.ArchivePath(game.GameDirectory),before=Storage.Hash(path);
        var original=GameDataMods.Original(game.GameDirectory);
        foreach(int denMode in new[]{4,5})
        {
            var settings=new LuckSettings(4,denMode,true); settings.Validate();
            var plan=GameDataMods.Build(original,settings); var archive=new GameArchive(plan.Archive);
            var eggs=new XfsTable(archive.Entries.Single(e=>e.Name==@"table\dungeon_nest_rareness_tbl").Extract());
            int groups=0,retreat=0;
            foreach(var row in eggs.Rows.Where(r=>r.Number("mMultiDungeon")==0))
                for(int i=0;i<4;i++)
                {
                    Require(row.Number($"mProbEggNormal[{i}]")==0&&row.Number($"mProbEggRare[{i}]")==0&&row.Number($"mProbEggSRare[{i}]")==100,"A solo quality roll is not exclusively SRare");
                    Require(row.Number($"mProbEggLost[{i}]")==0,"Empty pulls remain");
                    groups++; if(row.Number("mHomingNest")!=0) retreat++;
                }
            Require(groups==28&&retreat==4,"Expected all seven solo rows, including the retreat row");
            var oldArchive=new GameArchive(original);
            var oldDens=new XfsTable(oldArchive.Entries.Single(e=>e.Name==@"table\dungeon_create_ptn_tbl").Extract());
            var dens=new XfsTable(archive.Entries.Single(e=>e.Name==@"table\dungeon_create_ptn_tbl").Extract());
            int rare=0,super=0;
            for(int i=0;i<dens.Rows.Count;i++)
            {
                string[] names=["mDungeonProbNormal","mDungeonProbRare","mDungeonProbSRare","mDungeonProbHigher"];
                var old=oldDens.Rows[i];var row=dens.Rows[i];uint total=(uint)names.Sum(n=>(long)old.Number(n));
                bool useSuper=denMode==5&&old.Number(names[2])>0;
                Require(row.Number(names[0])==0&&row.Number(names[3])==0);
                Require(row.Number(names[1])==(useSuper?0:total)&&row.Number(names[2])==(useSuper?total:0),"Rare-only selection or story fallback failed");
                if(useSuper) super++; else rare++;
            }
            Require(rare==(denMode==4?30:15)&&super==(denMode==4?0:15));
        }
        Require(GameDataMods.Exclusive([0,0,0],2).All(v=>v==0),"Empty groups acquired probability");
        Require(GameDataMods.Exclusive([100,0,0,0],-1).SequenceEqual(new uint[]{100,0,0,0}),"No-eligible-den fallback changed");
        Reject(()=>new LuckSettings(5,0,false).Validate()); Reject(()=>new LuckSettings(0,6,false).Validate());
        Require(Storage.Hash(path)==before,"Rainbow preview changed game data");
    }
    static void LuckInstallationTest()
    {
        var installed=SteamInstallation.Find() ?? throw new Exception("Installed archive needed for read-only source");
        var original=GameDataMods.Original(installed.GameDirectory);
        string temp=Path.Combine(Path.GetTempPath(),"riders-lucktest-"+Guid.NewGuid().ToString("N"));
        try
        {
            Storage.TestRoot=Path.Combine(temp,"app"); string game=Path.Combine(temp,"game"),path=GameDataMods.ArchivePath(game);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path,original);
            GameDataMods.InstallCore(game,new(1,1,false),()=>false);
            string first=Storage.Hash(path); Require(first!=GameDataMods.StockHash); Require(GameDataMods.Original(game).SequenceEqual(original));
            Require(GameDataMods.Status(game).StartsWith("Luck settings installed"));
            Require(GameDataMods.InstalledSettings(game)==new LuckSettings(1,1,false),"Installed selection did not reload");
            int checks=0;
            Reject(()=>GameDataMods.InstallCore(game,new(3,3,true),()=>++checks>1));
            Require(Storage.Hash(path)==first,"Interrupted update replaced the archive");
            Require(GameDataMods.Original(game).SequenceEqual(original),"Interrupted journal could not recover previous settings");
            GameDataMods.InstallCore(game,new(4,5,true),()=>false); Require(Storage.Hash(path)!=first);
            var expected=GameDataMods.Build(original,new(4,5,true)); Require(File.ReadAllBytes(path).SequenceEqual(expected.Archive),"Update accumulated prior boosts");
            GameDataMods.InstallCore(game,new(0,0,false),()=>false); Require(File.ReadAllBytes(path).SequenceEqual(original),"Restore was not byte-for-byte exact");
            Require(GameDataMods.InstalledSettings(game)==new LuckSettings(0,0,false),"Restored selection did not reload");
            var changed=(byte[])original.Clone(); changed[^1]^=1; File.WriteAllBytes(path,changed);
            Reject(()=>GameDataMods.InstallCore(game,new(1,1,false),()=>false)); Require(File.ReadAllBytes(path).SequenceEqual(changed),"Another mod was overwritten");
            File.WriteAllBytes(path,original); GameDataMods.InstallCore(game,new(1,1,false),()=>false);
            string backup=Directory.GetFiles(Path.Combine(Storage.Root,"GameData"),"original.table.arc",SearchOption.AllDirectories).Single(); File.WriteAllBytes(backup,[1,2,3]);
            bool rejected=false; try { GameDataMods.InstallCore(game,new(0,0,false),()=>false); } catch(InvalidDataException) { rejected=true; }
            Require(rejected,"Corrupted backup was accepted"); Require(Storage.Hash(path)==first);
        }
        finally { Storage.TestRoot=null; RemoveScratch(temp,"riders-lucktest-"); }
    }
    static void StorageTest()
    {
        string temp=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"riders-selftest-"+Guid.NewGuid().ToString("N")));
        try
        {
            Storage.TestRoot=Path.Combine(temp,"app"); Storage.TestSaves=Path.Combine(temp,"saves");
            Directory.CreateDirectory(Storage.TestSaves); Directory.CreateDirectory(Storage.BackupRoot); Directory.CreateDirectory(Storage.ProfileRoot);
            var save=Path.Combine(Storage.TestSaves,"mhr_slot_1.sav"); var sys=Path.Combine(Storage.TestSaves,"mhr_sys.sav");
            File.WriteAllBytes(save,[1,2,3,4]); File.WriteAllBytes(sys,[8,9]);
            var backup=Storage.Backup(); Require(Storage.Backups().Count==1);
            File.WriteAllBytes(save,[5,6,7,8]); Storage.Restore(backup);
            Require(File.ReadAllBytes(save).SequenceEqual(new byte[]{1,2,3,4})); Require(Storage.Backups().Count==2);
            File.WriteAllBytes(Path.Combine(backup.Path,"mhr_slot_1.sav"),[99,99]);
            bool rejected=false; try { Storage.Restore(backup); } catch(InvalidDataException) { rejected=true; }
            Require(rejected); Require(File.ReadAllBytes(save).SequenceEqual(new byte[]{1,2,3,4}));
            var profile=new Profile("Test",4,[new("riderHp","99","F1",true)]);
            Storage.SaveProfile(profile); var read=Storage.LoadProfile("Test"); Require(read.ExperienceMultiplier==4&&read.Features[0].Target=="99");
            Reject(()=>Storage.SaveProfile(profile with{Name="../escape"}));
        }
        finally
        {
            Storage.TestRoot=null; Storage.TestSaves=null;
            // Delete only the exact GUID-named scratch directory created by this test.
            var parent=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if(Directory.GetParent(temp)?.FullName==parent && Path.GetFileName(temp).StartsWith("riders-selftest-",StringComparison.Ordinal) && Directory.Exists(temp)) Directory.Delete(temp,true);
        }
    }
    static void RealProcessTest()
    {
        var file=Path.Combine(Path.GetTempPath(),"riders-fixture-"+Guid.NewGuid().ToString("N"));
        var start=new ProcessStartInfo(Environment.ProcessPath!,"--fixture"){UseShellExecute=false,CreateNoWindow=true};
        start.Environment["RIDERS_FIXTURE_PATH"]=file;
        using var child=Process.Start(start)!;
        try
        {
            var watch=Stopwatch.StartNew(); while(!File.Exists(file) && watch.ElapsedMilliseconds<10000) Thread.Sleep(30);
            Require(File.Exists(file),"Fixture failed to start");
            var data=File.ReadAllText(file).Split('|'); var address=long.Parse(data[1]);
            using var memory=new ProcessMemory(Process.GetProcessById(child.Id));
            Require(BitConverter.ToInt32(memory.Read(address+128,4))==914537);
            using var bounded=new BoundedMemory(memory,address,4096);
            var first=ValueScanner.First(bounded,914537,NumberKind.Int32,default);
            Require(first.Addresses.SequenceEqual(new[]{address+128}));
            memory.Write(address+128,BitConverter.GetBytes(914536));
            var second=ValueScanner.Refine(bounded,first,914536,"exact",default);
            Require(ValueScanner.CanBind(second));
            memory.Write(address+128,BitConverter.GetBytes(765432));
            Require(BitConverter.ToInt32(memory.Read(address+128,4))==765432);
            // Exercise the same Win32 protection/write/flush/restore path against
            // private fixture bytes. This never attaches to Stories 2.
            long costSite=address+254;byte[] instruction=Convert.FromHexString("41B801000000E8530C4700");
            memory.Write(costSite,instruction[..8]);memory.Write(costSite+8,instruction[8..]);uint protection=memory.Query(costSite)!.Protect;
            ((IFieldItemCostMemory)memory).SetFieldItemCost(costSite-0x541912,true);
            Require(BitConverter.ToInt32(memory.Read(costSite+2,4))==0&&memory.Query(costSite)!.Protect==protection);
            ((IFieldItemCostMemory)memory).SetFieldItemCost(costSite-0x541912,false);
            Require(memory.Read(costSite,11).SequenceEqual(instruction)&&memory.Query(costSite)!.Protect==protection);

        }
        finally
        {
            File.WriteAllText(file+".stop",""); if(!child.WaitForExit(5000)) child.Kill();
            File.Delete(file); File.Delete(file+".stop");
        }
    }
    sealed class BoundedMemory(IProcessMemory inner,long address,long size) : IProcessMemory
    {
        public bool IsAlive=>inner.IsAlive;
        public Region? Query(long p)=>inner.Query(p);
        public byte[] Read(long p,int count)=>inner.Read(p,count);
        public void Write(long p,byte[] data)=>inner.Write(p,data);
        public IEnumerable<Region> Regions() { var r=inner.Query(address)!; yield return new(address,r.AllocationBase,size,r.Protect); }
        public void Dispose() { }
    }
    sealed class FakeMemory : IProcessMemory
    {
        public long Base=0x100000;
        public long Generation=0;
        public bool Alive=true;
        readonly byte[] _data=Enumerable.Repeat((byte)0xAB,4096).ToArray();
        public bool IsAlive=>Alive;
        public Region? Query(long p)=>Alive&&p>=Base&&p<Base+_data.Length?new(Base,Base+Generation,_data.Length,4):null;
        public IEnumerable<Region> Regions() { yield return new(Base,Base+Generation,_data.Length,4); }
        public byte[] Read(long p,int count)=>_data.AsSpan((int)(p-Base),count).ToArray();
        public void Write(long p,byte[] data)=>data.CopyTo(_data,(int)(p-Base));
        public void Put(int offset,int value)=>BitConverter.GetBytes(value).CopyTo(_data,offset);
        public int Get(int offset)=>BitConverter.ToInt32(_data,offset);
        public void Dispose() { Alive=false; }
    }
}
