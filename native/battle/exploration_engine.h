#pragma once
#include <set>
namespace battle {
inline unsigned explorationTicks=0,explorationMarkers=0,explorationRanges=0;
struct FieldActor{uintptr_t address,vtable;Path path;};
inline bool nativeType(uintptr_t actor,const char* expected){
 auto inside=[](uintptr_t a,size_t n){return a>=image&&a<=image+ImageSize-n;};
 auto vt=q(actor);if(!inside(vt,0x28))return false;auto fn=q(vt+0x20);unsigned char code[8]{};
 if(!inside(fn,8)||!copyRead(fn,code,8)||code[0]!=0x48||code[1]!=0x8d||code[2]!=5||code[7]!=0xc3)return false;
 int displacement=0;memcpy(&displacement,code+3,4);auto metadata=fn+7+displacement;
 if(!inside(metadata,16))return false;auto name=q(metadata+8);char text[64]{};
 return inside(name,64)&&copyRead(name,text,64)&&strlen(expected)<64&&!memcmp(text,expected,strlen(expected)+1);
}
inline bool fieldScene(Path& path,uintptr_t& player,uintptr_t& controller){
 auto save=follow(image+0x1db2eb0,path);player=save?follow(save+0x33398,path):0;
 controller=player?follow(player+0x4888,path):0;
 auto state=controller?follow(controller+0xd968,path):0;
 return state&&follow(controller+0xd918,path);
}
inline bool fieldActors(const Path& field,std::vector<FieldActor>& actors){
 Path root=field;auto manager=follow(image+0x1d386c0,root);auto list=manager?follow(manager+0x100,root):0;
 if(!list)return false;auto first=dataPointer(list+0x10,root),end=q(list+0x18);root.push_back({list+0x18,end});
 if(!first||end<first||(end-first)%40||end-first>40*512||!accessible(first,end-first))return false;
 std::set<uintptr_t> seen;unsigned packages=0,visited=0;
 for(auto entry=first;entry<end;entry+=40){
  Path groupPath=root;auto group=follow(entry+0x20,groupPath);if(!group)continue;
  int count=0;if(!read(group+0x2d0,count)||count<0||count>2048)continue;
  auto slots=dataPointer(group+0x2e0,groupPath);if(!slots||!accessible(slots,count*8))continue;
  groupPath.push_back({group+0x2d0,q(group+0x2d0)});
  for(int i=0;i<count;++i){
   if(++packages>8192)return false;
   Path packagePath=groupPath;auto slot=follow(slots+i*8,packagePath);if(!slot)continue;
   auto package=follow(slot+0x18,packagePath);
   if(!package){if(q(slot+0x18))continue;packagePath.push_back({slot+0x18,0});package=follow(slot+0x10,packagePath);}
   if(!package||q(q(package)+0x80)!=image+0x49b1d0)continue;
   int n=0;if(!read(package+0xe8,n)||n<0||n>4096)continue;
   auto objects=dataPointer(package+0xf8,packagePath);if(!objects||!accessible(objects,n*8))continue;
   packagePath.push_back({package+0xe8,q(package+0xe8)});
   for(int j=0;j<n;++j){
    if(++visited>16384)return false;
    Path path=packagePath;auto wrapper=follow(objects+j*8,path);
    if(!wrapper||q(q(wrapper)+0x28)!=image+0x3a48a0)continue;
    auto actor=follow(wrapper+0x298,path);
    if(actor&&seen.insert(actor).second)actors.push_back({actor,q(actor),std::move(path)});
    if(actors.size()>4096)return false;
   }
  }
 }
 return valid(root);
}
#include "field_refresh.inc"
inline bool ordinaryMaterial(int tool,int type){return (tool==1||tool==2||tool==4)&&type>=1&&type<=16&&type!=12&&type!=14&&type!=15;}
inline bool materialKind(uintptr_t actor,Path& path){
 auto manager=follow(image+0x1debc10,path);auto resource=manager?follow(manager+0x1e0,path):0;int id=0,count=0;
 if(!resource||!read(actor+0x2280,id)||!read(resource+0xd0,count)||id<0||id>=count||count>10000)return false;
 auto rows=dataPointer(resource+0xe0,path);auto row=rows?follow(rows+id*8,path):0;int tool=0,type=0;
 return row&&read(row+0x2c,tool)&&read(row+0x30,type)&&ordinaryMaterial(tool,type);
}
inline void fieldRadar(const FieldActor& actor){
 auto vt=actor.vtable-image;uintptr_t address=0;Path path=actor.path;
 if(vt==0x15f2528||vt==0x15f1f68||vt==0x15d0288||vt==0x15d0728)address=actor.address+0x27d9;
 else if(vt==0x15ef848){
  unsigned flags=0;float marker=0;auto body=follow(actor.address+0x120,path);
  if(body&&read(actor.address+0x18,flags)&&(flags&0x4000)&&(flags&7)==2&&(flags&0x4800)==0x4800&&read(body+0x22c0,marker)&&std::isfinite(marker)&&marker!=0)address=actor.address+0x962;
 }
 unsigned char current=0;if(address&&read(address,current)&&current<=1&&apply<unsigned char>(15,address,1,1,path,true)){++explorationMarkers;radarTouchAt=GetTickCount64();}
}
inline void fieldRange(const FieldActor& actor){
 if(actor.vtable!=image+0x15f2528)return;Path path=actor.path;if(!materialKind(actor.address,path))return;
 auto sphere=follow(actor.address+0x2770,path);float radius=0;
 if(!sphere||q(sphere)!=image+0x1600040||!read(sphere+0x2c,radius)||!std::isfinite(radius)||radius<1||radius>200)return;
 if(apply<float>(16,sphere+0x2c,4,200.f,path,true,true))++explorationRanges;
}
inline void fasterGather(){
 if(!settings[17]||inBattle){restore(17);return;}
 Path path;auto save=follow(image+0x1db2eb0,path),player=save?follow(save+0x33398,path):0;
 auto stablePath=path;
 auto controller=player?follow(player+0x4888,path):0;auto action=controller?follow(controller+0xd940,path):0;
 if(!action||!(nativeType(action,"cPlActGather_PickUp")||nativeType(action,"cPlActGather_Mining")||nativeType(action,"cPlActGather_Net"))||q(action+0x30)!=player){restore(17);return;}
 path.push_back({action+0x30,player});float rate=0;
 if(valid(path)&&read(player+0x66c,rate)&&std::isfinite(rate)&&rate>0&&rate<=10)apply<float>(17,player+0x66c,4,10.f,stablePath,true);
}
inline void retreat(){
 if(!settings[18]||!inBattle){restore(18);return;}
 Path root;auto manager=follow(image+0x1dec560,root);if(!manager)return;
 for(int side=0;side<2;++side){Path path=root;auto controller=follow(manager+0x9f8+side*8,path);if(!controller)continue;
  for(int slot=0;slot<4;++slot){Path unitPath=path;auto unit=follow(controller+0x20+slot*8,unitPath);auto parameter=unit?follow(unit+0xb0,unitPath):0;
   if(parameter)apply<unsigned short>(18,parameter+0x1f48,100,65535,unitPath,true);
  }
 }
}
inline void prayer(){
 auto effect=settings[19].load();if(!effect){restore(19);return;}
 Path path;auto save=follow(image+0x1db2eb0,path);int count=0;
 if(!save||!read(save+0x9a5c8,count)||count<=0||count>1000){restore(19);return;}
 auto record=save+0x50d9ec;int duration=0,current=0;
 if(!read(record,duration)||!read(record+8,current)||duration<0||duration>11||current<0||current>8)return;
 apply<int>(19,record,0,11,path,true);apply<unsigned char>(19,record+4,0,255,path,true);apply<int>(19,record+8,effect,8,path,true);
}
inline unsigned radarCursor=0;
inline void exploration(){
 fasterGather();retreat();prayer();
 if(!(settings[15]||settings[16]))return;
 // A temporary field-controller gap must not actively hide existing markers.
 if(inBattle||!knownFieldReady)return;
 auto deadline=GetTickCount64()+4;
 for(unsigned checked=0;checked<32&&!knownFieldActors.empty()&&GetTickCount64()<deadline;++checked){
  if(radarCursor>=knownFieldActors.size())radarCursor=0;
  const auto& actor=knownFieldActors[radarCursor++];
  if(settings[15])fieldRadar(actor);if(settings[16])fieldRange(actor);
 }
}
}
