#pragma once
#include <wincodec.h>
#include <wrl/client.h>
#include <intrin.h>
#include <initializer_list>
namespace buttons {
using Microsoft::WRL::ComPtr;
inline uintptr_t base;
inline std::wstring directory;
inline std::vector<unsigned char> textureBytes;
inline void* texture=nullptr;
inline bool attempted=false;
inline std::atomic<bool> loaded=false;
inline unsigned iconApplications=0;
inline void log(const char*){}
inline void report(){}
template<class T>T& at(void* p,size_t o){return *reinterpret_cast<T*>(static_cast<char*>(p)+o);}
bool prepareTexture(){
 HRESULT init=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
 ComPtr<IWICImagingFactory> factory;ComPtr<IWICBitmapDecoder> decoder;ComPtr<IWICBitmapFrameDecode> frame;ComPtr<IWICBitmapScaler> scaler;ComPtr<IWICFormatConverter> converter;
 HRESULT hr=CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&factory));
 if(SUCCEEDED(hr))hr=factory->CreateDecoderFromFilename((directory+L"\\kinship-buttons.png").c_str(),nullptr,GENERIC_READ,WICDecodeMetadataCacheOnLoad,&decoder);
 if(SUCCEEDED(hr))hr=decoder->GetFrame(0,&frame);
 if(SUCCEEDED(hr))hr=factory->CreateBitmapScaler(&scaler);
 if(SUCCEEDED(hr))hr=scaler->Initialize(frame.Get(),1024,1024,WICBitmapInterpolationModeFant);
 if(SUCCEEDED(hr))hr=factory->CreateFormatConverter(&converter);
 if(SUCCEEDED(hr))hr=converter->Initialize(scaler.Get(),GUID_WICPixelFormat32bppRGBA,WICBitmapDitherTypeNone,nullptr,0,WICBitmapPaletteTypeCustom);
 std::vector<unsigned char> rgba(1024*1024*4);
 if(SUCCEEDED(hr))hr=converter->CopyPixels(nullptr,1024*4,static_cast<UINT>(rgba.size()),rgba.data());
 converter.Reset();scaler.Reset();frame.Reset();decoder.Reset();factory.Reset();
 if(SUCCEEDED(init))CoUninitialize();
 if(FAILED(hr)){log("PNG decoding failed; the original icon remains active");return false;}
 textureBytes.resize(24+1024*1024);
 uint32_t header[]{0x00584554,0x200030a3,1u|(1024u<<6)|(1024u<<19),1u|(55u<<8)|(1u<<16),24,0};
 memcpy(textureBytes.data(),header,24);
 auto out=textureBytes.data()+24;
 for(unsigned y=0;y<1024;y+=4)for(unsigned x=0;x<1024;x+=4){
  // Full RGBA BC7 mode 6: a panel needs blue/cream or parchment/ink
  // in the same bitmap. The previous alpha-only encoder discarded RGB.
  int pixels[16][4],endpoints[2][4],indices[16],pbits[2];
  for(unsigned j=0;j<4;++j)for(unsigned i=0;i<4;++i)
   for(unsigned c=0;c<4;++c)pixels[j*4+i][c]=rgba[((y+j)*1024+x+i)*4+c];
  int farthest=-1,first=0,last=0;
  for(int i=0;i<16;++i)for(int j=i;j<16;++j){
   int distance=0;for(int c=0;c<4;++c){int d=pixels[i][c]-pixels[j][c];distance+=d*d;}
   if(distance>farthest){farthest=distance;first=i;last=j;}
  }
  for(int e=0;e<2;++e){
   int* pixel=pixels[e?last:first];int bestError=1000000,bestParity=0;
   for(int parity=0;parity<2;++parity){int error=0;
    for(int c=0;c<4;++c){int q=std::clamp((pixel[c]-parity+1)/2,0,127)*2+parity;int d=q-pixel[c];error+=d*d;}
    if(error<bestError){bestError=error;bestParity=parity;}
   }
   pbits[e]=bestParity;
   for(int c=0;c<4;++c)endpoints[e][c]=std::clamp((pixel[c]-bestParity+1)/2,0,127)*2+bestParity;
  }
  constexpr int weights[]{0,4,9,13,17,21,26,30,34,38,43,47,51,55,60,64};
  for(int i=0;i<16;++i){int best=0,bestError=1000000;
   for(int j=0;j<16;++j){int error=0;for(int c=0;c<4;++c){
    int decoded=((64-weights[j])*endpoints[0][c]+weights[j]*endpoints[1][c]+32)>>6;
    int d=decoded-pixels[i][c];error+=d*d;
   }if(error<bestError){best=j;bestError=error;}}
   indices[i]=best;
  }
  if(indices[0]>=8){for(int c=0;c<4;++c)std::swap(endpoints[0][c],endpoints[1][c]);std::swap(pbits[0],pbits[1]);for(auto& index:indices)index=15-index;}
  memset(out,0,16);unsigned bit=0;
  auto put=[&](unsigned value,unsigned bits){for(unsigned i=0;i<bits;i++,bit++)if(value&(1u<<i))out[bit/8]|=static_cast<unsigned char>(1u<<(bit%8));};
  put(64,7);
  for(int c=0;c<4;++c)for(int e=0;e<2;++e)put(static_cast<unsigned>(endpoints[e][c]>>1),7);
  put(pbits[0],1);put(pbits[1],1);
  for(int i=0;i<16;i++)put(static_cast<unsigned>(indices[i]),i==0?3:4);
  out+=16;
 }
 FILE* packed=nullptr;_wfopen_s(&packed,(directory+L"\\encoded-buttons.tex").c_str(),L"wb");if(packed){fwrite(textureBytes.data(),1,textureBytes.size(),packed);fclose(packed);}
 return true;
}
bool loadTexture(){
 if(attempted)return loaded;
 attempted=true;
 if(!prepareTexture())return false;
 using Create=void*(__fastcall*)();
 texture=reinterpret_cast<Create>(base+0xef7130)();
 if(!texture){log("Native texture allocation failed");return false;}
 alignas(16) unsigned char stream[48]{};
 using InitStream=void*(__fastcall*)(void*,const void*,unsigned,unsigned);
 reinterpret_cast<InitStream>(base+0xeae510)(stream,textureBytes.data(),static_cast<unsigned>(textureBytes.size()),1);
 using Load=bool(__fastcall*)(void*,void*);
 bool ok=reinterpret_cast<Load>(base+0xef73f0)(texture,stream);
 loaded=ok&&at<void*>(texture,0x128)!=nullptr&&at<unsigned>(texture,0x138)==1024&&at<unsigned>(texture,0x13c)==1024;
 log(loaded?"Generated emblem loaded as a native 1024px texture":"Native texture loading failed; original icon retained");if(loaded){auto gpu=at<void*>(texture,0x128);char message[160];sprintf_s(message,"Native image %p; renderer %p; component flags %u",texture,gpu,at<unsigned>(gpu,0x18));log(message);}report();return loaded;
}

}
