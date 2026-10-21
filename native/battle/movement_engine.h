#pragma once
// Ported from MovementController.cs and AdventureController.cs. All writes
// use the existing owner-guarded, reversible multiplier capture in battle.
namespace battle {
inline std::array<bool,5> movementReady{};
inline unsigned movementTicks=0;
inline uintptr_t dataPointer(uintptr_t address,Path& path){
 auto pointer=q(address);if(!accessible(pointer,8))return 0;
 path.push_back({address,pointer});return pointer;
}
inline void movementTable(unsigned feature,size_t offset,size_t vtable,std::initializer_list<size_t> fields){
 Path root;auto manager=follow(image+0x1debc10,root);
 auto table=manager?follow(manager+offset,root):0;int count=0;
 if(!table||!read(table+0xd0,count)||count<1||count>256)return;
 auto rows=dataPointer(table+0xe0,root);if(!rows||!accessible(rows,count*8))return;
 root.push_back({table+0xd0,q(table+0xd0)});
 for(int i=0;i<count;++i){Path path=root;auto row=follow(rows+i*8,path);
  if(!row||q(row)!=image+vtable)continue;
  for(auto field:fields){float speed=0;
   if(!read(row+field,speed)||!std::isfinite(speed)||speed<0||speed>30)continue;
   movementReady[feature-10]=true;
   if(settings[feature])apply<float>(feature,row+field,catalog::speed(settings[feature]),30.f,path,true,true);
  }
 }
}
inline bool airController(uintptr_t controller){
 auto inside=[](uintptr_t p,size_t n){return p>=image&&p<=image+ImageSize-n;};
 auto vtable=q(controller);if(!inside(vtable,0x28))return false;
 auto fn=q(vtable+0x20);unsigned char code[8]{};
 if(!inside(fn,8)||!copyRead(fn,code,8)||code[0]!=0x48||code[1]!=0x8d||code[2]!=5||code[7]!=0xc3)return false;
 int displacement=0;memcpy(&displacement,code+3,4);auto metadata=fn+7+displacement;
 if(!inside(metadata,16))return false;
 auto name=q(metadata+8);char type[64]{};
 return inside(name,64)&&copyRead(name,type,64)&&!memcmp(type,"cPlCtrlAirRide",14);
}
inline void movement(){
 movementReady.fill(false);
 // Parameter tables are small, but only need periodic refresh. Slider changes
 // apply on the next maintenance tick; captures prevent repeated compounding.
 if(++movementTicks%10==1){
  movementTable(10,0x300,0x175b900,{0x18,0x1c,0x20});
  movementTable(11,0x308,0x175b728,{0x40,0x44,0x5c});
  movementTable(13,0x318,0x175b9c0,{0x38,0x3c});
 }
 Path path;auto save=follow(image+0x1db2eb0,path);
 auto player=save?follow(save+0x33398,path):0;
 auto state=player?follow(player+0x4890,path):0;if(!state)return;
 auto controller=q(player+0x4888);float speed=0;
 if(accessible(controller,0xd970)&&airController(controller)&&q(controller+0xd968)==state&&read(state+0x148,speed)&&std::isfinite(speed)&&speed>0&&speed<=200){
  movementReady[2]=true;
  if(settings[12])apply<float>(12,state+0x148,catalog::speed(settings[12]),200.f,path,true,true);
 }
 if(inBattle){restore(14);return;}
 for(unsigned i=0;i<7;++i){auto address=state+0x250+i*4;float value=0;
  if(!read(address,value)||!std::isfinite(value)||value<=0||value>10)continue;
  movementReady[4]=true;
  if(settings[14])apply<float>(14,address,catalog::speed(settings[14]),10.f,path,true,true);
 }
}
}
