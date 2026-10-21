#pragma once
#include <Windows.h>
#include <atomic>
#include <array>
#include <vector>
#include <map>
#include <algorithm>
#include <cmath>
#include <cstring>
#include "battle_catalog.h"

// The same verified 1.6.1 data paths used by the desktop trainer. No code patches.
namespace battle {
constexpr int Count=29;
inline std::array<std::atomic<unsigned>,Count> settings{};
inline std::atomic<unsigned long long> writes=0;
inline std::atomic<unsigned> tickDurationMs=0,longestTickMs=0;
inline std::atomic<bool> inBattle=false, compatible=false;
inline uintptr_t image=0;
constexpr size_t ImageSize=86138880;
struct Guard {uintptr_t address,value;};
using Path=std::vector<Guard>;
struct Saved {int feature;uintptr_t address;size_t size;unsigned char original[8],written[8];Path path;};
inline std::map<uintptr_t,Saved> saved;

inline bool accessible(uintptr_t address,size_t size,bool write=false){
 if(address<65536||address>0x7fffffff0000ULL-size)return false;
 MEMORY_BASIC_INFORMATION m{};
 if(!VirtualQuery(reinterpret_cast<void*>(address),&m,sizeof(m))||m.State!=MEM_COMMIT||m.Protect&(PAGE_GUARD|PAGE_NOACCESS))return false;
 DWORD p=m.Protect&0xff;
 bool allowed=write?(p==PAGE_READWRITE||p==PAGE_WRITECOPY||p==PAGE_EXECUTE_READWRITE||p==PAGE_EXECUTE_WRITECOPY):
 (p==PAGE_READONLY||p==PAGE_READWRITE||p==PAGE_WRITECOPY||p==PAGE_EXECUTE_READ||p==PAGE_EXECUTE_READWRITE||p==PAGE_EXECUTE_WRITECOPY);
 return allowed&&address+size<=reinterpret_cast<uintptr_t>(m.BaseAddress)+m.RegionSize;
}
// SEH stays in leaf helpers: an object may be destroyed while scenes change.
inline bool copyRead(uintptr_t a,void* dst,size_t n){if(!accessible(a,n))return false;__try{memcpy(dst,reinterpret_cast<void*>(a),n);return true;}__except(EXCEPTION_EXECUTE_HANDLER){return false;}}
inline bool copyWrite(uintptr_t a,const void* src,size_t n){if(!accessible(a,n,true))return false;__try{memcpy(reinterpret_cast<void*>(a),src,n);return true;}__except(EXCEPTION_EXECUTE_HANDLER){return false;}}
template<class T> bool read(uintptr_t a,T& value){return copyRead(a,&value,sizeof(value));}
inline uintptr_t q(uintptr_t a){uintptr_t v=0;read(a,v);return v;}
inline bool object(uintptr_t a){auto v=q(a);return a>=65536&&v>=image&&v<image+ImageSize;}
inline uintptr_t follow(uintptr_t a,Path& p){auto v=q(a),vt=q(v);if(v<65536||vt<image||vt>=image+ImageSize)return 0;p.push_back({a,v});p.push_back({v,vt});return v;}
inline bool valid(const Path& p){for(auto g:p)if(q(g.address)!=g.value)return false;return true;}
inline bool samePath(const Path& a,const Path& b){if(a.size()!=b.size())return false;for(size_t i=0;i<a.size();++i)if(a[i].address!=b[i].address||a[i].value!=b[i].value)return false;return true;}
inline void restore(int feature=-1){
 for(auto it=saved.begin();it!=saved.end();){auto& s=it->second;if(feature!=-1&&s.feature!=feature){++it;continue;}
  unsigned char current[8]{};if(valid(s.path)&&copyRead(s.address,current,s.size)&&!memcmp(current,s.written,s.size)&&valid(s.path))copyWrite(s.address,s.original,s.size);
  it=saved.erase(it);
 }
}
template<class T> bool apply(int feature,uintptr_t a,double setting,T maximum,const Path& path,bool reversible=false,bool multiply=false){
 T current{};if(!valid(path)||!read(a,current)||!std::isfinite(static_cast<double>(current))||current<0||current>maximum)return false;
 T baseline=current;
 auto prior=saved.find(a);
 if(prior!=saved.end()&&prior->second.feature==feature&&prior->second.size==sizeof(T)&&samePath(prior->second.path,path)&&valid(prior->second.path)&&!memcmp(&current,prior->second.written,sizeof(T)))memcpy(&baseline,prior->second.original,sizeof(T));
 double computed=multiply?static_cast<double>(baseline)*setting:setting;
 T value=static_cast<T>(std::min(static_cast<double>(maximum),computed));
 if(reversible){Saved s{};s.feature=feature;s.address=a;s.size=sizeof(T);s.path=path;memcpy(s.original,&baseline,sizeof(T));memcpy(s.written,&value,sizeof(T));saved[a]=std::move(s);}
 if(current==value)return true;
 if(valid(path)&&copyWrite(a,&value,sizeof(T))){++writes;return true;}return false;
}
inline void inventoryStop();
inline void eggStop();
inline void stop(){eggStop();inventoryStop();for(auto& s:settings)s=0;restore();inBattle=false;}
inline uintptr_t parameter(uintptr_t actor,size_t field,Path& p){auto wrapper=follow(actor+field,p);return wrapper?follow(wrapper+0xB0,p):0;}
inline void defense(){
 unsigned choice=settings[9];if(!choice){restore(9);return;}
 Path root;auto save=follow(image+0x1db2eb0,root);int owned=0;if(!save||!read(save+0x9a5c8,owned)||owned<=0||owned>1000){restore(9);return;}
 Path path;auto manager=follow(image+0x1debc10,path);auto table=manager?follow(manager+0xc98,path):0;int count=0;
 if(!table||!read(table+0xd0,count)||count<=6||count>64){restore(9);return;}
 auto rows=q(table+0xe0);path.push_back({table+0xe0,rows});auto row=rows>=65536?follow(rows+6*8,path):0;int kind=0;
 if(!row||!read(row+0x48,kind)||kind!=3){restore(9);return;}
 auto record=save+0x50d9ec+0x30;int duration=0,prayer=0;if(!read(record,duration)||!read(record+8,prayer)||duration<0||duration>11||prayer<0||prayer>8){restore(9);return;}
 float protection=catalog::protection(choice);
 for(int i=0;i<20;++i)apply<float>(9,row+0x4c+i*4,protection,100.f,path,true);
 apply<int>(9,record,0,11,root,true);apply<unsigned char>(9,record+4,0,255,root,true);apply<int>(9,record+8,6,8,root,true);
}
inline void movement();
inline void exploration();
inline void inventory();
inline void preserveBattleItems();
inline void refreshFieldActors();
inline void eggHunt();
inline void tick(){
 if(!compatible)return;
 for(int f=0;f<Count;++f)if(!settings[f])restore(f);
 // Bound stale-capture housekeeping; every actual write still checks ownership.
 static uintptr_t pruneCursor=0;auto it=saved.upper_bound(pruneCursor);if(it==saved.end())it=saved.begin();
 auto pruneDeadline=GetTickCount64()+3;
 for(unsigned examined=0;it!=saved.end()&&examined<32&&GetTickCount64()<pruneDeadline;++examined){
  pruneCursor=it->first;if(!valid(it->second.path))it=saved.erase(it);else ++it;
 }
 Path root;auto manager=follow(image+0x1dec560,root);bool found=false;
 if(manager){
  for(int side=0;side<2;++side){Path path=root;auto actor=follow(manager+0x9d0+side*8,path);if(!actor)continue;
   Path riderPath=path,monstiePath=path;auto rider=parameter(actor,0x40,riderPath),monstie=parameter(actor,0x48,monstiePath);unsigned short hp=0;
   if(rider&&read(rider+0x18,hp)){
    found=true;
    if(settings[0])apply<unsigned short>(0,rider+0x18,9999,65535,riderPath);
    if(settings[3])apply<unsigned char>(3,actor+0x3e58,3,9,path);
    if(settings[5])apply<int>(5,manager+0x237c,1,9999,root);
    if(auto c=settings[4].load())apply<unsigned short>(4,rider+0x54,catalog::attack(c),30000,riderPath,true,true);
   }
   if(monstie&&read(monstie+0x18,hp)){
    if(settings[1])apply<unsigned short>(1,monstie+0x18,9999,65535,monstiePath);
    if(settings[2]){
     bool ready=apply<unsigned char>(2,monstie+0x1ff0,100,100,monstiePath);
     apply<unsigned char>(2,actor+0x3e9e,100,100,path);
     unsigned char flags=0;if(ready&&valid(path)&&read(actor+0x3e5c,flags)){flags|=4;if(valid(path))copyWrite(actor+0x3e5c,&flags,1);}
    }
    if(auto c=settings[4].load())apply<unsigned short>(4,monstie+0x54,catalog::attack(c),30000,monstiePath,true,true);
   }
   int riding=0;if(settings[7]&&read(actor+0x3eb4,riding)&&riding==2){Path mounted=path;mounted.push_back({actor+0x3eb4,q(actor+0x3eb4)});apply<unsigned char>(7,actor+0x3e59,2,3,mounted,true);}
  }
 }
 inBattle=found;
 if(found&&settings[6]){
  double multiplier=catalog::exp(settings[6]);
  for(int side=0;side<2;++side){Path p=root;auto controller=follow(manager+0x9f8+side*8,p);if(!controller)continue;
   for(int slot=0;slot<4;++slot){Path unitPath=p;auto unit=follow(controller+0x20+slot*8,unitPath);auto param=unit?follow(unit+0xb0,unitPath):0;if(param)apply<int>(6,param+0x1fe8,multiplier,999999,unitPath,true,true);}
  }
 }
 if(found&&settings[8]){Path p;auto clock=follow(image+0x1ea2ff0,p);float rate=0;if(clock&&read(clock+0x94,rate)&&rate>0&&rate<=4)apply<float>(8,clock+0x94,catalog::speed(settings[8]),4.f,p,true,true);}
 else restore(8);
 if(!found){restore(4);restore(6);restore(7);}
 eggHunt();preserveBattleItems();refreshFieldActors();defense();movement();exploration();inventory();
}
}

#include "movement_engine.h"

#include "exploration_engine.h"

#include "inventory_engine.h"

#include "economy_engine.h"

#include "egg_engine.h"
