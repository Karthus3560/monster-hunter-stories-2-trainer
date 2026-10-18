using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace RidersToolkit;

internal sealed record ArcEntry(string Name,byte[] Header,byte[] Compressed,int Size)
{
    public byte[] Extract()
    {
        using var source=new MemoryStream(Compressed); using var z=new ZLibStream(source,CompressionMode.Decompress);
        var result=new byte[Size]; z.ReadExactly(result);
        if(z.ReadByte()!=-1) throw new InvalidDataException("Archive entry exceeds its declared size.");
        return result;
    }
}

internal sealed class GameArchive
{
    public List<ArcEntry> Entries { get; }=[];
    readonly byte[] _prefix;
    readonly byte[] _header;
    public static byte[] Crypt(byte[] input,bool encrypt)
    {
        if(input.Length%8!=0) throw new InvalidDataException("Invalid archive block alignment.");
        var swapped=(byte[])input.Clone(); Swap(swapped);
        var engine=new BlowfishEngine(); engine.Init(encrypt,new KeyParameter(Encoding.ASCII.GetBytes("QZHaM;-5:)dV#")));
        var output=new byte[input.Length];
        for(int i=0;i<input.Length;i+=8) engine.ProcessBlock(swapped,i,output,i);
        Swap(output); return output;
    }
    static void Swap(byte[] value) { for(int i=0;i<value.Length;i+=4) Array.Reverse(value,i,4); }
    public GameArchive(byte[] input)
    {
        if(input.Length<16 || input.Length>64*1024*1024 || Encoding.ASCII.GetString(input,0,4)!="ARCC" || BitConverter.ToUInt16(input,4)!=7)
            throw new InvalidDataException("Unsupported Stories 2 archive.");
        _header=input[..8]; int count=BitConverter.ToUInt16(input,6);
        var plain=new byte[input.Length]; _header.CopyTo(plain,0); Crypt(input[8..],false).CopyTo(plain,8);
        if(count<1 || 8+count*144>plain.Length) throw new InvalidDataException("Invalid archive index.");
        int first=int.MaxValue, previousEnd=8+count*144; var names=new HashSet<string>();
        for(int i=0;i<count;i++)
        {
            int at=8+i*144; var h=plain[at..(at+144)]; int end=Array.IndexOf(h,(byte)0,0,128);
            if(end<1) throw new InvalidDataException("Invalid archive name.");
            var name=Encoding.UTF8.GetString(h,0,end); if(!names.Add(name)) throw new InvalidDataException("Duplicate archive entry.");
            int compressed=checked((int)BitConverter.ToUInt32(h,132));
            uint flags=BitConverter.ToUInt32(h,136); int size=checked((int)(flags&0x3fffffff));
            int offset=checked((int)BitConverter.ToUInt32(h,140));
            if((flags&0xc0000000)!=0x40000000 || size>32*1024*1024 || compressed<1 || offset<previousEnd || offset>plain.Length-compressed)
                throw new InvalidDataException("Unsupported archive entry layout.");
            first=Math.Min(first,offset); previousEnd=offset+compressed;
            Entries.Add(new(name,h,plain[offset..previousEnd],size));
        }
        _prefix=plain[..first];
    }
    public byte[] Repack(IReadOnlyDictionary<string,byte[]> replacements)
    {
        if(replacements.Keys.Any(k=>Entries.All(e=>e.Name!=k))) throw new InvalidDataException("A requested table is missing.");
        using var output=new MemoryStream(); output.Write(_prefix);
        for(int i=0;i<Entries.Count;i++)
        {
            var entry=Entries[i]; var header=(byte[])entry.Header.Clone(); byte[] payload=entry.Compressed;
            if(replacements.TryGetValue(entry.Name,out var data))
            {
                using var compressed=new MemoryStream();
                using(var z=new ZLibStream(compressed,CompressionLevel.SmallestSize,true)) z.Write(data);
                payload=compressed.ToArray(); Array.Resize(ref payload,(payload.Length+7)&~7);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(132),checked((uint)payload.Length));
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(136),checked((uint)data.Length)|0x40000000);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(140),checked((uint)output.Length));
            long offset=output.Position; output.Position=8+i*144; output.Write(header); output.Position=offset; output.Write(payload);
        }
        while((output.Length-8)%8!=0) output.WriteByte(0);
        var plain=output.ToArray(); var result=new byte[plain.Length]; _header.CopyTo(result,0); Crypt(plain[8..],true).CopyTo(result,8); return result;
    }
}

internal sealed record XfsField(string Name,int Offset,byte Type,object Value);
internal sealed class XfsRow
{
    public Dictionary<string,XfsField> Fields { get; }=[];
    public uint Number(string name)=>Convert.ToUInt32(Fields[name].Value);
}

// Reads the embedded schema from the installed x64 XFS tables. Updates touch only
// existing scalar bytes; schema, strings, object sizes and ordering stay intact.
internal sealed class XfsTable
{
    public byte[] Bytes { get; }
    public List<XfsRow> Rows { get; }=[];
    public List<XfsRow> AllRows { get; }=[];
    readonly List<(string Name,byte Type)[]> _classes=[];
    int _position;
    public XfsTable(byte[] bytes)
    {
        Bytes=(byte[])bytes.Clone();
        if(bytes.Length<48 || Encoding.ASCII.GetString(bytes,0,4)!="XFS\0" || BitConverter.ToUInt16(bytes,4)!=16)
            throw new InvalidDataException("Unsupported game table version.");
        int count=checked((int)BitConverter.ToUInt32(bytes,16)); if(count is <1 or >64) throw new InvalidDataException("Invalid table schema.");
        for(int i=0;i<count;i++)
        {
            int at=checked(24+(int)BitConverter.ToUInt64(bytes,24+i*8));
            int properties=checked((int)BitConverter.ToUInt32(bytes,at+8)); if(properties>128) throw new InvalidDataException("Invalid table properties.");
            var definitions=new (string,byte)[properties];
            for(int j=0;j<properties;j++)
            {
                int field=checked(at+16+j*48); int name=checked(24+(int)BitConverter.ToUInt64(bytes,field));
                int end=Array.IndexOf(bytes,(byte)0,name); if(end<name) throw new InvalidDataException("Invalid table string.");
                definitions[j]=(Encoding.UTF8.GetString(bytes,name,end-name),bytes[field+8]);
            }
            _classes.Add(definitions);
        }
        _position=checked(24+(int)BitConverter.ToUInt32(bytes,20)); ReadObject(0);
        if(_position!=bytes.Length) throw new InvalidDataException("Table data did not consume its declared length.");
    }
    void ReadObject(int depth)
    {
        if(depth>16) throw new InvalidDataException("Table nesting is too deep.");
        int start=_position; ushort reference=Read<ushort>(2,BitConverter.ToUInt16); _position+=2;
        int index=reference>>1; if((reference&1)==0 || index==0x7fff) return;
        if(index>=_classes.Count) throw new InvalidDataException("Invalid table class reference.");
        ulong length=Read<ulong>(8,BitConverter.ToUInt64); int end=checked(start+4+(int)length);
        if(end>Bytes.Length || end<_position) throw new InvalidDataException("Invalid table object size.");
        var row=new XfsRow();
        foreach(var definition in _classes[index])
        {
            uint count=Read<uint>(4,BitConverter.ToUInt32); if(count>4096) throw new InvalidDataException("Table array is too large.");
            for(uint i=0;i<count;i++)
            {
                int at=_position; object value;
                switch(definition.Type)
                {
                    case 1: case 2: ReadObject(depth+1); continue;
                    case 3: case 4: value=Read<byte>(1,(b,p)=>b[p]); break;
                    case 8: value=Read<sbyte>(1,(b,p)=>unchecked((sbyte)b[p])); break;
                    case 5: value=Read<ushort>(2,BitConverter.ToUInt16); break;
                    case 6: value=Read<uint>(4,BitConverter.ToUInt32); break;
                    case 10: value=Read<int>(4,BitConverter.ToInt32); break;
                    case 12: value=Read<float>(4,BitConverter.ToSingle); break;
                    case 14:
                        int zero=Array.IndexOf(Bytes,(byte)0,_position); if(zero<0 || zero>=end) throw new InvalidDataException("Invalid table text.");
                        value=Encoding.UTF8.GetString(Bytes,_position,zero-_position); _position=zero+1; break;
                    default: throw new InvalidDataException($"Unsupported game table property: {definition.Name} (type {definition.Type}).");
                }
                if(count!=1 || !row.Fields.TryAdd(definition.Name,new(definition.Name,at,definition.Type,value))) throw new InvalidDataException("Unexpected scalar table layout.");
            }
        }
        if(_position!=end) throw new InvalidDataException("Game table object size mismatch.");
        if(row.Fields.Count>0) AllRows.Add(row);
        if(row.Fields.ContainsKey("mKeyString")) Rows.Add(row);
    }
    T Read<T>(int size,Func<byte[],int,T> reader)
    { if(_position<0 || _position>Bytes.Length-size) throw new InvalidDataException("Truncated game table."); var v=reader(Bytes,_position); _position+=size; return v; }
    public void Scalar(XfsRow row,string name,double value)
    {
        var f=row.Fields[name]; if(!double.IsFinite(value))throw new InvalidDataException("Invalid scalar.");
        var kind=f.Type switch {3 or 4=>NumberKind.Byte,5=>NumberKind.UInt16,6 or 10=>NumberKind.Int32,12=>NumberKind.Float32,_=>throw new InvalidDataException("Unsupported scalar type.")};
        Numbers.Encode(value,kind).CopyTo(Bytes,f.Offset);
    }
    public void Set(XfsRow row,string name,uint value)
    {
        var field=row.Fields[name]; if(field.Type!=6) throw new InvalidDataException("A probability field changed type.");
        BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(field.Offset),value);
    }
}
