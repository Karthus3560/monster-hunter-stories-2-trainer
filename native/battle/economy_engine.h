#pragma once
#include "craft_items.h"
namespace battle {
inline std::atomic<unsigned> craftCommits=0,craftRestoredSlots=0,zennyApplications=0;
inline bool directChest(const FieldActor& actor,Path& path,int& kind,int& id){
 if(actor.vtable!=image+0x15d0288||!valid(actor.path))return false;
 int selector=0,equipment=0;
 if(!read(actor.address+0x29cc,kind)||!read(actor.address+0x29d0,id)||!read(actor.address+0x29d4,equipment)||!read(actor.address+0x29ec,selector)||selector!=0x7fff||equipment!=0)return false;
 path=actor.path;path.push_back({actor.address+0x29cc,q(actor.address+0x29cc)});path.push_back({actor.address+0x29d4,q(actor.address+0x29d4)});path.push_back({actor.address+0x29ec,q(actor.address+0x29ec)});return true;
}
inline void multiplyChest(const FieldActor& actor,unsigned multiplier){
 Path path;int kind=0,id=0;
 if(directChest(actor,path,kind,id)){
  int amount=0;if(kind==1&&ordinaryReward(id)&&read(actor.address+0x29e8,amount)&&amount>0&&amount<=99)apply<int>(23,actor.address+0x29e8,multiplier,99,path,true,true);
  return;
 }
 if(actor.vtable!=image+0x15d0728||!valid(actor.path))return;
 unsigned char alternate=0,amount=0;if(!read(actor.address+0x29c0,alternate)||alternate>1||!read(actor.address+(alternate?0x29c8:0x29c4),id)||!ordinaryReward(id)||!read(actor.address+0x27c8,amount)||amount==0||amount>99)return;
 path=actor.path;path.push_back({actor.address+0x29c0,q(actor.address+0x29c0)});path.push_back({actor.address+0x29c8,q(actor.address+0x29c8)});
 apply<unsigned char>(23,actor.address+0x27c8,multiplier,99,path,true,true);
}
inline void multiplyChestZenny(const FieldActor& actor,unsigned multiplier){
 Path path;int kind=0,id=0,amount=0;constexpr int limit=9999999;
 if(!directChest(actor,path,kind,id)||kind!=6||!read(actor.address+0x29e8,amount)||amount<0||amount>limit)return;
 Path savePath;auto save=follow(image+0x1db2eb0,savePath);int wallet=0;
 if(!save||!read(save+0x23ae80,wallet)||wallet<0||wallet>limit)return;
 path.insert(path.end(),savePath.begin(),savePath.end());auto address=actor.address+0x29e8;int baseline=amount;
 auto prior=saved.find(address);if(prior!=saved.end()&&prior->second.feature==24&&samePath(path,prior->second.path)&&!memcmp(&amount,prior->second.written,4))memcpy(&baseline,prior->second.original,4);
 if(baseline<=0||baseline>limit)return;
 int value=static_cast<int>(std::min(static_cast<unsigned long long>(limit-wallet),static_cast<unsigned long long>(baseline)*multiplier));
 Saved capture{};capture.feature=24;capture.address=address;capture.size=4;capture.path=path;memcpy(capture.original,&baseline,4);memcpy(capture.written,&value,4);saved[address]=std::move(capture);
 if(amount!=value&&valid(path)&&copyWrite(address,&value,4)){++writes;++zennyApplications;}
}
inline bool stageZennyReward(uintptr_t controller){
 auto choice=settings[24].load();if(!choice)return false;
 auto vt=q(controller);if(vt!=image+0x161bf10&&vt!=image+0x16528b0&&vt!=image+0x167ef20)return false;
 Path path;auto save=follow(image+0x1db2eb0,path);int current=0,target=0;constexpr int limit=9999999;
 if(!save||!read(save+0x23ae80,current)||!read(controller+0x5b4,target)||current<0||current>limit||target<=current||target>limit)return false;
 auto reward=static_cast<unsigned long long>(target-current)*rewardMultiplier(choice);
 int value=current+static_cast<int>(std::min(reward,static_cast<unsigned long long>(limit-current)));
 if(value==target)return false;
 if(valid(path)&&q(controller)==vt&&copyWrite(controller+0x5b4,&value,4)){++writes;++zennyApplications;return true;}return false;
}
inline const CraftItemDefinition* craftItem(unsigned id){
 auto begin=std::begin(craftItemDefinitions),end=std::end(craftItemDefinitions);
 auto it=std::lower_bound(begin,end,id,[](const auto& item,unsigned value){return item.id<value;});return it!=end&&it->id==id?it:nullptr;
}
struct CraftSnapshot{uintptr_t save=0;Path path;std::array<unsigned char,4+2000*8> items{};bool captured=false;};
inline void captureCraft(CraftSnapshot& capture){
 capture.save=follow(image+0x1db2eb0,capture.path);
 capture.captured=capture.save&&copyRead(capture.save+0x23ae80,capture.items.data(),capture.items.size())&&valid(capture.path);
}
inline void restoreCraft(const CraftSnapshot& capture){
 if(!capture.captured||!valid(capture.path))return;
 std::array<unsigned char,4+2000*8> after{};auto inventory=capture.save+0x23ae80;
 if(!copyRead(inventory,after.data(),after.size()))return;
 for(unsigned slot=0;slot<2000;++slot){
  InventoryEntry before{},now{};memcpy(&before,capture.items.data()+4+slot*8,4);memcpy(&now,after.data()+4+slot*8,4);auto definition=craftItem(before.id);
  if(!definition||before.count==0||before.count>definition->capacity||now.count>=before.count)continue;
  if(now.id!=before.id&&!(now.id==0&&now.count==0))continue;
  if(valid(capture.path)&&replaceInventoryEntry(inventory+4+slot*8,now,before)){++writes;++craftRestoredSlots;}
 }
}
template<class Fn>inline void craftingCommit(uintptr_t self,uintptr_t expectedVtable,Fn original){
 CraftSnapshot capture;
 bool protect=compatible&&settings[25]&&!inBattle&&q(self)==image+expectedVtable;
 if(protect)captureCraft(capture);
 original(reinterpret_cast<void*>(self));
 if(protect&&capture.captured){++craftCommits;restoreCraft(capture);}
}
}
