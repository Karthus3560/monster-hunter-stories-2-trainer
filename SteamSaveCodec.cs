using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace RidersToolkit;

// Save-format research: Andoryuuta (MIT) and AsteriskAmpersand. The key
// generator is a managed implementation; encrypted data and account IDs stay local.
internal static class SteamSaveCodec
{
    internal const int Size=8421552;
    static readonly Lazy<uint[]> Seeds=new(()=>
    {
        using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/RidersToolkit;component/Assets/save-key-seeds.json"))!.Stream;
        return JsonSerializer.Deserialize<uint[]>(stream)??throw new InvalidDataException("Save key seed data is unavailable.");
    });
    internal static byte[] Key(uint seed)
    {
        unchecked
        {
            uint value=seed^0xAC9365,a=362436069,b=521288629,c=88675123,x=0;
            for(int i=0;i<100;i++)
            {
                x=a;a=b;b=c;uint mix=value^(0x65AC9365u>>(int)(value&3));
                value=mix^(mix>>3)^(mix>>4)^(mix<<3)^(mix<<4);
                c^=value^(value<<15)^((value^(value<<15))>>4)^(c>>21);
            }
            const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_!#$%&'()*+,-./:;<=>?@[\\]^_`{|}~ ";
            byte[] key=new byte[56];
            for(int i=0;i<key.Length;i++){uint t=x^(x<<15);x=a;a=b;b=c;c=t^c^((t^(c>>17))>>4);key[i]=(byte)alphabet[(int)(c%alphabet.Length)];}
            return key;
        }
    }
    static byte[] Swap(byte[] bytes){var copy=(byte[])bytes.Clone();for(int i=0;i<copy.Length;i+=4)Array.Reverse(copy,i,4);return copy;}
    internal static byte[] Crypt(byte[] bytes,byte[] key,bool encrypt)
    {
        if(bytes.Length%8!=0)throw new InvalidDataException("Save block alignment is invalid.");
        var input=Swap(bytes);var output=new byte[bytes.Length];var cipher=new BlowfishEngine();cipher.Init(encrypt,new KeyParameter(key));
        for(int i=0;i<input.Length;i+=8)cipher.ProcessBlock(input,i,output,i);
        return Swap(output);
    }
    internal static void Validate(byte[] data,byte[] key)
    {
        if(data.Length!=Size||BitConverter.ToUInt64(data)!=1||BitConverter.ToUInt32(data,8)!=24||BitConverter.ToUInt32(data,32)!=0x808070||data.AsSpan(data.Length-8).IndexOfAnyExcept((byte)0)>=0)
            throw new InvalidDataException("This is not the supported Stories 2 Steam save layout.");
        if(!data.AsSpan(12,20).SequenceEqual(Swap(SHA1.HashData(data.AsSpan(64)))))throw new InvalidDataException("The save checksum is invalid; no editing is allowed.");
        uint crc=0xFFFFFFFF;foreach(byte v in key){crc^=v;for(int i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0xEDB88320u:0);}
        if(BitConverter.ToUInt32(data,36)!=crc)throw new InvalidDataException("Save key identity verification failed.");
    }
    internal static (byte[] Data,byte[] Key) Decode(byte[] encrypted)
    {
        if(encrypted.Length!=Size)throw new InvalidDataException("Choose an existing Stories 2 Steam save slot. Its format or size is unsupported.");
        foreach(uint seed in Seeds.Value)
        {
            byte[] generated=Key(seed);
            foreach(int length in new[]{56,55})
            {
                byte[] key=generated[..length];
                if(BitConverter.ToUInt64(Crypt(encrypted[..8],key,false))!=1)continue;
                var data=Crypt(encrypted,key,false);Validate(data,key);
                if(!Crypt(data,key,true).SequenceEqual(encrypted))throw new InvalidDataException("The save did not pass exact roundtrip verification.");
                return(data,key);
            }
        }
        throw new InvalidDataException("This save's encryption is unsupported. Your save has not been changed.");
    }
    internal static byte[] DecodeWithKey(byte[] encrypted,byte[] key){var data=Crypt(encrypted,key,false);Validate(data,key);return data;}
    internal static byte[] Encode(byte[] data,byte[] key)
    {
        var copy=(byte[])data.Clone();Swap(SHA1.HashData(copy.AsSpan(64))).CopyTo(copy,12);Validate(copy,key);return Crypt(copy,key,true);
    }
}
