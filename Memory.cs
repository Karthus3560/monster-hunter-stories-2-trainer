using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RidersToolkit;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct MemoryInfo
    {
        public nint BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State, Protect, Type;
    }
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool WriteProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern nuint VirtualQueryEx(SafeProcessHandle process, nint address, out MemoryInfo info, nuint size);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool VirtualProtectEx(SafeProcessHandle process,nint address,nuint size,uint protect,out uint previous);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool FlushInstructionCache(SafeProcessHandle process,nint address,nuint size);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
}
public sealed record Region(long Base, long AllocationBase, long Length, uint Protect);
public sealed record ScanProgress(long BytesRead, int Matches, string Stage);
public sealed record ScanResult(List<long> Addresses, long BytesRead, double Value, NumberKind Kind, int Passes, bool SawChange);

public interface IProcessMemory : IDisposable
{
    bool IsAlive { get; }
    Region? Query(long address);
    byte[] Read(long address, int count);
    void Write(long address, byte[] bytes);
    IEnumerable<Region> Regions();
}

internal interface IFieldItemCostMemory { void SetFieldItemCost(long moduleBase,bool free); }

public sealed class ProcessMemory : IProcessMemory,IFieldItemCostMemory
{
    readonly Process _process;
    readonly SafeProcessHandle _handle;
    public int ProcessId => _process.Id;
    public long WriteOperations {get;private set;}
    public bool IsAlive { get { try { return !_process.HasExited && !_handle.IsClosed; } catch { return false; } } }
    public ProcessMemory(Process process)
    {
        _process=process;
        _handle=Native.OpenProcess(0x0010|0x0020|0x0008|0x0400, false, process.Id);
        if (_handle.IsInvalid) {int error=Marshal.GetLastWin32Error();_handle.Dispose();_process.Dispose();throw new Win32Exception(error, "Cannot access the game. If it was started as administrator, start this trainer at the same level.");}
    }
    public static bool Writable(uint protect) => (protect & 0x100) == 0 && (protect & 0xFF) is 0x04 or 0x08;
    public Region? Query(long address)
    {
        if (address < 65536 || Native.VirtualQueryEx(_handle,(nint)address,out var info,(nuint)Marshal.SizeOf<Native.MemoryInfo>())==0) return null;
        // Only committed writable PRIVATE data. Never patch executable code, DLLs or mapped files.
        return info.State==0x1000 && info.Type==0x20000 && Writable(info.Protect)
            ? new(info.BaseAddress.ToInt64(),info.AllocationBase.ToInt64(),checked((long)info.RegionSize),info.Protect) : null;
    }
    public IEnumerable<Region> Regions()
    {
        long address=65536;
        while (address < 0x7FFFFFFF0000 && IsAlive)
        {
            if (Native.VirtualQueryEx(_handle,(nint)address,out var info,(nuint)Marshal.SizeOf<Native.MemoryInfo>())==0) yield break;
            long length=checked((long)info.RegionSize);
            if (length<=0 || info.BaseAddress.ToInt64()>long.MaxValue-length) yield break;
            if (info.State==0x1000 && info.Type==0x20000 && Writable(info.Protect)) yield return new(info.BaseAddress.ToInt64(),info.AllocationBase.ToInt64(),length,info.Protect);
            address=info.BaseAddress.ToInt64()+length;
        }
    }
    public byte[] Read(long address,int count)
    {
        if (count<1 || count>4*1024*1024) throw new ArgumentOutOfRangeException(nameof(count));
        var data=new byte[count];
        Native.ReadProcessMemory(_handle,(nint)address,data,(nuint)count,out var read);
        if (read==0) throw new Win32Exception(Marshal.GetLastWin32Error(),"Game data is no longer readable. Waiting for a valid game context.");
        if (read!=(nuint)count) Array.Resize(ref data,(int)read);
        return data;
    }
    public void Write(long address,byte[] bytes)
    {
        if (!IsAlive) throw new InvalidOperationException("The game has closed.");
        var region=Query(address) ?? throw new InvalidOperationException("The value is no longer in writable game data.");
        if (bytes.Length<1 || bytes.Length>8 || address>region.Base+region.Length-bytes.Length) throw new InvalidOperationException("The write crosses the value's memory region.");
        if (!Native.WriteProcessMemory(_handle,(nint)address,bytes,(nuint)bytes.Length,out var written)||written!=(nuint)bytes.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(),"The game did not accept the change.");
        WriteOperations++;
    }
    bool _fieldCostFree;long _fieldModule;
    void IFieldItemCostMemory.SetFieldItemCost(long moduleBase,bool free)
    {
        if(!IsAlive)throw new InvalidOperationException("The game has closed.");
        long site=moduleBase+0x541912;byte[] code=Read(site,11);
        byte[] expected=Convert.FromHexString(_fieldCostFree?"41B800000000E8530C4700":"41B801000000E8530C4700");
        if(!code.SequenceEqual(expected))throw new InvalidOperationException("The field item-use routine changed. Close other trainers and reconnect.");
        if(free==_fieldCostFree)return;
        long a=site+2;byte[] value=BitConverter.GetBytes(free?0:1);
        // The only executable-memory write in this app: the aligned quantity
        // immediate for the inspected camp item-use call. No jump, hook or cave.
        if(!Native.VirtualProtectEx(_handle,(nint)a,4,0x40,out uint previous))throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot adjust the field item-use instruction.");
        Exception? failure=null;bool written=false;
        try
        {
            if(!Read(site,11).SequenceEqual(expected))throw new InvalidOperationException("Field item routine changed during activation.");
            if(!Native.WriteProcessMemory(_handle,(nint)a,value,4,out var count)||count!=4)throw new Win32Exception(Marshal.GetLastWin32Error(),"The game rejected the field-item setting.");
            written=true;_fieldCostFree=free;_fieldModule=moduleBase;WriteOperations++;
            if(!Native.FlushInstructionCache(_handle,(nint)a,4))throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not refresh the field-item instruction.");
        }
        catch(Exception ex){failure=ex;}
        finally
        {
            if(!Native.VirtualProtectEx(_handle,(nint)a,4,previous,out _))failure??=new Win32Exception(Marshal.GetLastWin32Error(),"Could not restore game code protection.");
        }
        if(failure is not null)
        {
            if(written&&free)try{((IFieldItemCostMemory)this).SetFieldItemCost(moduleBase,false);}catch{}
            throw failure;
        }
    }
    public void Dispose()
    {
        if(_fieldCostFree&&IsAlive)try{((IFieldItemCostMemory)this).SetFieldItemCost(_fieldModule,false);}catch{}
        _handle.Dispose();_process.Dispose();
    }
}

public static class ValueScanner
{
    const int MaxMatches=2_000_000;
    public static ScanResult First(IProcessMemory memory,double value,NumberKind kind,CancellationToken token,IProgress<ScanProgress>? progress=null)
    {
        var pattern=Numbers.Encode(value,kind);
        var results=new List<long>(); long readTotal=0;
        foreach(var region in memory.Regions())
        {
            for(long offset=0;offset<region.Length;offset+=1024*1024)
            {
                token.ThrowIfCancellationRequested();
                var length=(int)Math.Min(1024*1024,region.Length-offset);
                byte[] data;
                try { data=memory.Read(region.Base+offset,length); } catch(Win32Exception) { continue; }
                readTotal+=data.Length;
                for(int i=0;i<=data.Length-pattern.Length;i+=pattern.Length)
                {
                    if (data.AsSpan(i,pattern.Length).SequenceEqual(pattern))
                    {
                        results.Add(region.Base+offset+i);
                        if(results.Count>MaxMatches) throw new InvalidOperationException("This number appears too often. Choose a larger distinctive value, or try a different number format.");
                    }
                }
                progress?.Report(new(readTotal,results.Count,"Reading writable game data"));
            }
        }
        if(!memory.IsAlive) throw new InvalidOperationException("The game closed during the search. Reconnect and try again.");
        return new(results,readTotal,Numbers.Decode(pattern,kind),kind,1,false);
    }
    public static ScanResult Refine(IProcessMemory memory,ScanResult previous,double value,string comparison,CancellationToken token,IProgress<ScanProgress>? progress=null)
    {
        value=Numbers.Decode(Numbers.Encode(value,previous.Kind),previous.Kind);
        int width=Numbers.Width(previous.Kind), inspected=0;
        var found=new List<long>();
        foreach(var address in previous.Addresses)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var bytes=memory.Read(address,width);
                if(bytes.Length!=width || memory.Query(address)==null) continue;
                var current=Numbers.Decode(bytes,previous.Kind);
                if(!double.IsFinite(current)) continue;
                bool matches=comparison switch {"changed"=>current!=previous.Value,"unchanged"=>current==previous.Value,"increased"=>current>previous.Value,"decreased"=>current<previous.Value,_=>current==value};
                if(matches) found.Add(address);
            }
            catch(Win32Exception) { }
            if(++inspected%4096==0) progress?.Report(new((long)inspected*width,found.Count,"Narrowing results"));
        }
        // Binding requires an exact, changed second observation, not a comparison guess.
        return new(found,(long)inspected*width,value,previous.Kind,previous.Passes+1,previous.SawChange || (comparison=="exact" && value!=previous.Value));
    }
    public static bool CanBind(ScanResult? result) => result is {Addresses.Count:1,Passes:>=2,SawChange:true};
}

public sealed class TrainerEngine : IDisposable
{
    public IProcessMemory? Memory { get; private set; }
    public int Pid { get; private set; }
    public IReadOnlyList<Feature> Features { get; }
    public double ExperienceMultiplier { get; set; }=1;
    public event Action<string,string>? Fault;
    public TrainerEngine(IReadOnlyList<Feature> features) => Features=features;
    public void Attach(Process process) { Detach(); Memory=new ProcessMemory(process); Pid=process.Id; }
    internal void AttachTest(IProcessMemory memory) { Detach(); Memory=memory; }
    public void Bind(Feature feature,ScanResult result)
    {
        if(!ValueScanner.CanBind(result)) throw new InvalidOperationException("Narrow to one result using two different exact values before linking this control.");
        var region=Memory?.Query(result.Addresses[0]) ?? throw new InvalidOperationException("This result is no longer writable.");
        int width=Numbers.Width(result.Kind);
        var bytes=Memory!.Read(result.Addresses[0],width);
        if(bytes.Length!=width || Numbers.Decode(bytes,result.Kind)!=result.Value) throw new InvalidOperationException("The value changed since the last search. Search its current value again.");
        if(result.Value<feature.Minimum || result.Value>feature.Maximum) throw new InvalidOperationException("The result is outside this control's allowed range.");
        if(Features.Any(x=>x!=feature && x.Bound && Math.Abs(x.Address-result.Addresses[0])<Math.Max(width,Numbers.Width(x.Kind)))) throw new InvalidOperationException("Another control already uses this value. Clear that control before linking it here.");
        feature.Kind=result.Kind; feature.Address=result.Addresses[0]; feature.RegionBase=region.Base; feature.AllocationBase=region.AllocationBase;
        feature.Bound=true; feature.Active=false; feature.LastObserved=result.Value; feature.Current=result.Value.ToString("N0"); feature.ExperienceBaseline=(long)result.Value;
    }
    private double ReadChecked(Feature feature)
    {
        if(!feature.Bound || Memory is null || !Memory.IsAlive) throw new InvalidOperationException("Connect to the game and locate this value first.");
        var region=Memory.Query(feature.Address);
        if(region==null || region.Base!=feature.RegionBase || region.AllocationBase!=feature.AllocationBase) throw new InvalidOperationException("The memory region changed. Locate the value again.");
        int width=Numbers.Width(feature.Kind);
        var bytes=Memory.Read(feature.Address,width);
        if(bytes.Length!=width) throw new InvalidOperationException("The complete value could not be read.");
        var current=Numbers.Decode(bytes,feature.Kind);
        if(!double.IsFinite(current) || current<feature.Minimum || current>feature.Maximum) throw new InvalidOperationException("The value is no longer valid. Locate it again.");
        return current;
    }
    public double Apply(Feature feature)
    {
        var target=feature.CheckedTarget();
        ReadChecked(feature);
        Memory!.Write(feature.Address,Numbers.Encode(target,feature.Kind));
        feature.LastWritten=target; feature.LastObserved=target; feature.ExperienceBaseline=(long)target; feature.Current=target.ToString("N0");
        return target;
    }
    public void Tick()
    {
        if(Memory is null) return;
        if(!Memory.IsAlive) { Detach(); Fault?.Invoke("Game disconnected","All controls were stopped and located values cleared."); return; }
        foreach(var feature in Features.Where(f=>f.Bound))
        {
            try
            {
                var current=ReadChecked(feature);
                if(feature.Active)
                {
                    double target;
                    if(feature.IsExperience && ExperienceMultiplier>1)
                    {
                        var prior=feature.ExperienceBaseline ?? (long)current;
                        var delta=current-prior;
                        if(delta>0 && delta<=5_000_000) target=Math.Min(feature.Maximum,current+delta*(ExperienceMultiplier-1));
                        else target=current;
                        feature.ExperienceBaseline=(long)target;
                    }
                    else target=feature.CheckedTarget();
                    if(target!=current) { Memory.Write(feature.Address,Numbers.Encode(target,feature.Kind)); current=target; feature.LastWritten=target; }
                }
                else if(feature.IsExperience) feature.ExperienceBaseline=(long)current;
                feature.LastObserved=current; feature.Current=current.ToString(feature.Kind==NumberKind.Float32?"N2":"N0");
            }
            catch(Exception ex) when(ex is Win32Exception or InvalidOperationException or ArgumentException)
            {
                Clear(feature); Fault?.Invoke(feature.Title+" stopped",ex.Message);
            }
        }
    }
    public void StopAll() { foreach(var feature in Features) feature.Active=false; }
    public void Clear(Feature feature) { feature.Active=false; feature.Bound=false; feature.Address=0; feature.LastObserved=null; feature.Current="—"; feature.ExperienceBaseline=null; }
    public void Detach() { StopAll(); foreach(var feature in Features) Clear(feature); Memory?.Dispose(); Memory=null; Pid=0; }
    public void Dispose() => Detach();
}
