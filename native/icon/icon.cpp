#include <Windows.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>
#include <string>
#include <atomic>
#include <algorithm>
#include "../battle/runtime_guard.h"
#include "icon_proofs.h"
using Microsoft::WRL::ComPtr;
extern "C" IMAGE_DOS_HEADER __ImageBase;
namespace {
uintptr_t base;
std::wstring directory;
std::vector<unsigned char> textureBytes;
void* texture=nullptr;
alignas(16) unsigned char customTile[48]{};
using VoidFn=void(__fastcall*)(void*);
VoidFn originalTick=nullptr;
void** tickSlot=nullptr;
std::atomic<bool> installed=false,loaded=false,failed=false;
std::atomic<unsigned> applications=0;
void* lastBitmap=nullptr;
bool attempted=false;
void log(const char* message){FILE* f=nullptr;_wfopen_s(&f,(directory+L"\\icon-runtime.log").c_str(),L"ab");if(f){fprintf(f,"%llu %s\n",GetTickCount64(),message);fclose(f);}}
void report(){FILE* f=nullptr;_wfopen_s(&f,(directory+L"\\icon-state.json").c_str(),L"wb");if(f){fprintf(f,"{\"installed\":%s,\"customTextureLoaded\":%s,\"failed\":%s,\"bitmapApplications\":%u,\"gameFilesModified\":false}\n",installed?"true":"false",loaded?"true":"false",failed?"true":"false",applications.load());fclose(f);}}
bool readable(const void* p,size_t size){MEMORY_BASIC_INFORMATION m{};if(!p||!VirtualQuery(p,&m,sizeof(m))||m.State!=MEM_COMMIT||m.Protect&(PAGE_GUARD|PAGE_NOACCESS))return false;return uintptr_t(p)+size<=uintptr_t(m.BaseAddress)+m.RegionSize;}
template<class T>T& at(void* p,size_t offset){return *reinterpret_cast<T*>(static_cast<char*>(p)+offset);}

// Convert the generated PNG to the engine's native BC7 texture representation.
// The game supplies the ink colour and selection tint; the artwork supplies alpha.
bool prepareTexture(){
 HRESULT init=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
 ComPtr<IWICImagingFactory> factory;ComPtr<IWICBitmapDecoder> decoder;ComPtr<IWICBitmapFrameDecode> frame;ComPtr<IWICBitmapScaler> scaler;ComPtr<IWICFormatConverter> converter;
 HRESULT hr=CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&factory));
 if(SUCCEEDED(hr))hr=factory->CreateDecoderFromFilename((directory+L"\\kinship-emblem.png").c_str(),nullptr,GENERIC_READ,WICDecodeMetadataCacheOnLoad,&decoder);
 if(SUCCEEDED(hr))hr=decoder->GetFrame(0,&frame);
 if(SUCCEEDED(hr))hr=factory->CreateBitmapScaler(&scaler);
 if(SUCCEEDED(hr))hr=scaler->Initialize(frame.Get(),256,256,WICBitmapInterpolationModeFant);
 if(SUCCEEDED(hr))hr=factory->CreateFormatConverter(&converter);
 if(SUCCEEDED(hr))hr=converter->Initialize(scaler.Get(),GUID_WICPixelFormat32bppRGBA,WICBitmapDitherTypeNone,nullptr,0,WICBitmapPaletteTypeCustom);
 std::vector<unsigned char> rgba(256*256*4);
 if(SUCCEEDED(hr))hr=converter->CopyPixels(nullptr,256*4,static_cast<UINT>(rgba.size()),rgba.data());
 converter.Reset();scaler.Reset();frame.Reset();decoder.Reset();factory.Reset();
 if(SUCCEEDED(init))CoUninitialize();
 if(FAILED(hr)){log("PNG decoding failed; the original icon remains active");return false;}
 textureBytes.resize(24+256*256);
 uint32_t header[]{0x00584554,0x200030a3,1u|(256u<<6)|(256u<<19),1u|(55u<<8)|(1u<<16),24,0};
 memcpy(textureBytes.data(),header,24);
 auto out=textureBytes.data()+24;
 for(unsigned y=0;y<256;y+=4)for(unsigned x=0;x<256;x+=4){
  unsigned char alpha[16];int lo=255,hi=0;
  for(unsigned j=0;j<4;j++)for(unsigned i=0;i<4;i++){int a=rgba[((y+j)*256+x+i)*4+3];alpha[j*4+i]=static_cast<unsigned char>(a);lo=std::min(lo,a);hi=std::max(hi,a);}
  // BC7 mode 6 matches the native Camp atlas format, including smooth alpha.
  int a0=hi,a1=lo,p0=a0&1,p1=a1&1;int indices[16];
  constexpr int weights[]{0,4,9,13,17,21,26,30,34,38,43,47,51,55,60,64};
  for(int i=0;i<16;i++){int best=0,distance=256;for(int j=0;j<16;j++){int a=((64-weights[j])*a0+weights[j]*a1+32)>>6;int d=abs(int(alpha[i])-a);if(d<distance){best=j;distance=d;}}indices[i]=best;}
  if(indices[0]>=8){std::swap(a0,a1);std::swap(p0,p1);for(auto& index:indices)index=15-index;}
  memset(out,0,16);unsigned bit=0;
  auto put=[&](unsigned value,unsigned bits){for(unsigned i=0;i<bits;i++,bit++)if(value&(1u<<i))out[bit/8]|=static_cast<unsigned char>(1u<<(bit%8));};
  put(64,7);for(int i=0;i<6;i++)put(127,7);put(static_cast<unsigned>(a0>>1),7);put(static_cast<unsigned>(a1>>1),7);put(static_cast<unsigned>(p0),1);put(static_cast<unsigned>(p1),1);
  for(int i=0;i<16;i++)put(static_cast<unsigned>(indices[i]),i==0?3:4);
  out+=16;
 }
 FILE* packed=nullptr;_wfopen_s(&packed,(directory+L"\\encoded-emblem.tex").c_str(),L"wb");if(packed){fwrite(textureBytes.data(),1,textureBytes.size(),packed);fclose(packed);}
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
 loaded=ok&&at<void*>(texture,0x128)!=nullptr&&at<unsigned>(texture,0x138)==256&&at<unsigned>(texture,0x13c)==256;
 log(loaded?"Generated emblem loaded as a native 256px texture":"Native texture loading failed; original icon retained");if(loaded){auto gpu=at<void*>(texture,0x128);char message[160];sprintf_s(message,"Native image %p; renderer %p; component flags %u",texture,gpu,at<unsigned>(gpu,0x18));log(message);}report();return loaded;
}
void apply(void* self){
 if(failed||at<int>(self,0x2b0)!=0)return;
 auto begin=at<unsigned char*>(self,0x298),end=at<unsigned char*>(self,0x2a0);
 if(!begin||end-begin!=16*24||!readable(begin,16*24)||*reinterpret_cast<int*>(begin+15*24+4)!=92)return;
 using Root=void*(__fastcall*)(void*,int);
 using Count=int(__fastcall*)(void*,int);
 using Index=int(__fastcall*)(void*,int,int);
 using Cell=void*(__fastcall*)(void*,int);
 auto root=reinterpret_cast<Root>(base+0x743440)(self,-1);if(!root)return;
 int count=reinterpret_cast<Count>(base+0x7434d0)(self,-1);if(count<1||count>16)return;
 for(int i=0;i<count;i++){
  if(reinterpret_cast<Index>(base+0x7433e0)(self,i,-1)!=15)continue;
  auto cell=reinterpret_cast<Cell>(base+0x7414c0)(root,i);if(!cell)return;
  int ids[]{3,4};struct Path{int* begin;int* end;int* capacity;} path{ids,ids+2,ids+2};
  using Bitmap=void*(__fastcall*)(void*,Path*);
  void* bitmap=reinterpret_cast<Bitmap>(base+0x4bc360)(cell,&path);if(!readable(bitmap,0x154))return;
  auto tile=at<void*>(bitmap,0x138);if(!readable(tile,48))return;
  if(!loadTexture()){failed=true;report();return;}
  if(tile!=customTile){
   memcpy(customTile,tile,48);short rect[]{0,0,256,256};memcpy(customTile+8,rect,8);float inverseSize[]{1.0f/256,1.0f/256};memcpy(customTile+0x20,inverseSize,8);memcpy(customTile+0x28,&texture,8);
   at<void*>(bitmap,0x138)=customTile;
  }
  float uv[]{0,0,256,256};using SetUV=void(__fastcall*)(void*,const float*);
  reinterpret_cast<SetUV>(base+0x10afbb0)(bitmap,uv);
  if(lastBitmap!=bitmap){lastBitmap=bitmap;++applications;log("Custom emblem assigned only to the Kinship Studio Camp card");report();}
 }
}
void __fastcall tick(void* self){originalTick(self);__try{apply(self);}__except(EXCEPTION_EXECUTE_HANDLER){failed=true;log("Icon integration stopped after an unexpected native layout; no further icon writes");report();}}
DWORD WINAPI run(void*){
 wchar_t module[MAX_PATH];GetModuleFileNameW(reinterpret_cast<HMODULE>(&__ImageBase),module,MAX_PATH);directory=module;directory.resize(directory.find_last_of(L"\\/"));base=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
 uintptr_t text=0;
 if(!runtimeguard::session(directory,text)||!verifyIconProofs(base)){log("Session mismatch; no changes");return 1;}
 tickSlot=reinterpret_cast<void**>(base+0x166b7e8)+9;
 if(*tickSlot!=reinterpret_cast<void*>(base+0x744c90)){log("Native callback mismatch; no changes");return 2;}
 DWORD previous;if(!VirtualProtect(tickSlot,8,PAGE_READWRITE,&previous))return 3;
 originalTick=reinterpret_cast<VoidFn>(*tickSlot);InterlockedExchangePointer(reinterpret_cast<void* volatile*>(tickSlot),reinterpret_cast<void*>(tick));DWORD ignored;VirtualProtect(tickSlot,8,previous,&ignored);installed=true;report();log("Custom icon callback installed; waiting for the Camp Menu");
 while(GetFileAttributesW((directory+L"\\icon.stop").c_str())==INVALID_FILE_ATTRIBUTES)Sleep(200);
 if(VirtualProtect(tickSlot,8,PAGE_READWRITE,&previous)){InterlockedCompareExchangePointer(reinterpret_cast<void* volatile*>(tickSlot),reinterpret_cast<void*>(originalTick),reinterpret_cast<void*>(tick));VirtualProtect(tickSlot,8,previous,&ignored);}
 // Keep the generated texture and tile alive until the process exits. Native
 // draw commands may still refer to them after the callback is removed.
 installed=false;report();log("Icon callback restored; close/reopen Camp Menu to clear cached icon");return 0;
}
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH){DisableThreadLibraryCalls(instance);HANDLE t=CreateThread(nullptr,0,run,nullptr,0,nullptr);if(t)CloseHandle(t);}return TRUE;}
