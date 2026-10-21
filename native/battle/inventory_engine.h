#pragma once
#include "ordinary_items.h"
#include "battle_items.h"
namespace battle {
struct InventoryEntry{unsigned short id=0,count=0;};
static_assert(sizeof(InventoryEntry)==4);
inline std::array<InventoryEntry,2000> battleInventory{};
inline uintptr_t inventoryOwner=0;
inline Path inventoryOwnerPath;
inline const BattleItemDefinition* battleItem(unsigned id){
 auto first=std::begin(battleItemDefinitions),last=std::end(battleItemDefinitions);
 auto it=std::lower_bound(first,last,id,[](const auto& item,unsigned value){return item.id<value;});
 return it!=last&&it->id==id?it:nullptr;
}
inline void clearBattleInventory(){battleInventory={};inventoryOwner=0;inventoryOwnerPath.clear();}
// Commit ID/count together only if the slot is still exactly as observed.
inline bool replaceInventoryEntry(uintptr_t address,const InventoryEntry& expected,const InventoryEntry& replacement){
 if(address%4||!accessible(address,4,true))return false;
 LONG from=0,to=0;memcpy(&from,&expected,4);memcpy(&to,&replacement,4);
 __try{return InterlockedCompareExchange(reinterpret_cast<volatile LONG*>(address),to,from)==from;}
 __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
inline void preserveInventory(uintptr_t save,const Path& path,bool battleActive){
 if(!settings[20]||!battleActive||!save||!valid(path)){clearBattleInventory();return;}
 if(inventoryOwner!=save||!samePath(path,inventoryOwnerPath)){clearBattleInventory();inventoryOwner=save;inventoryOwnerPath=path;}
 std::array<unsigned char,4+2000*8> snapshot{};
 auto inventory=save+0x23ae80;
 if(!copyRead(inventory,snapshot.data(),snapshot.size())){clearBattleInventory();return;}
 for(unsigned slot=0;slot<battleInventory.size();++slot){
  InventoryEntry now{};memcpy(&now,snapshot.data()+4+slot*8,4);auto prior=battleInventory[slot];
  bool last=now.id==0&&now.count==0&&prior.count==1&&battleItem(prior.id);
  auto definition=battleItem(last?prior.id:now.id);
  if(!definition||now.count>definition->capacity){battleInventory[slot]={};continue;}
  if((prior.id==now.id||last)&&static_cast<int>(prior.count)-now.count==1){
   auto address=inventory+4+slot*8;
   if(valid(path)&&replaceInventoryEntry(address,now,prior)){++writes;now=prior;}
   else {battleInventory[slot]={};continue;}
  }
  battleInventory[slot]=now;
 }
}
inline void preserveBattleItems(){
 Path path;auto save=follow(image+0x1db2eb0,path);preserveInventory(save,path,inBattle);
}
inline bool ordinaryReward(int id){return std::binary_search(std::begin(ordinaryItemIds),std::end(ordinaryItemIds),id);}
inline unsigned inventoryTicks=0;
inline std::vector<FieldActor> materialRewards;
inline void inventoryStop(){clearBattleInventory();materialRewards.clear();clearFieldCache();}
inline bool ingredientActor(uintptr_t vt){return vt==image+0x15f2528;}
inline void multiplyMaterial(const FieldActor& actor,unsigned multiplier){
 if(!ingredientActor(actor.vtable)||!valid(actor.path))return;
 auto lot=q(actor.address+0x27c0);if(!accessible(lot,0x20))return;
 unsigned char kind=0,quantity=0;int id=0;
 if(!read(lot+0x14,kind)||kind!=1||!read(lot+0x18,id)||!ordinaryReward(id)||!read(actor.address+0x27c8,quantity)||quantity==0||quantity>99)return;
 Path path=actor.path;path.push_back({actor.address+0x27c0,lot});path.push_back({lot+0x18,q(lot+0x18)});
 apply<unsigned char>(21,actor.address+0x27c8,multiplier,99,path,true,true);
}
inline void multiplyChest(const FieldActor& actor,unsigned multiplier);
inline void multiplyChestZenny(const FieldActor& actor,unsigned multiplier);
inline void enemyLoot(unsigned multiplier){
 Path root;auto manager=follow(image+0x1dec560,root);if(!manager)return;
 for(int side=0;side<2;++side){Path controllerPath=root;auto controller=follow(manager+0x9f8+side*8,controllerPath);if(!controller)continue;
  for(int slot=0;slot<4;++slot){Path path=controllerPath;auto unit=follow(controller+0x20+slot*8,path);auto parameter=unit?follow(unit+0xb0,path):0;
   if(!parameter||q(q(parameter)+0x330)!=image+0xb7ec30)continue;
   auto data=parameter+0x46c;
   for(unsigned j=0;j<9;++j){unsigned short id=0;unsigned char quantity=0;
    if(!read(data+j*2,id)||!ordinaryReward(id)||!read(data+0x12+j,quantity)||quantity==0)continue;
    Path lootPath=path;auto guard=data+std::min(j/4*8,10u);lootPath.push_back({guard,q(guard)});
    apply<unsigned char>(22,data+0x12+j,multiplier,99,lootPath,true,true);
   }
  }
 }
}
inline unsigned rewardMultiplier(unsigned setting){constexpr unsigned m[]{1,2,5,10};return m[std::min(setting,3u)];}
inline unsigned fieldRewardCursor=0;
inline void inventory(){
 auto gathering=settings[21].load(),enemy=settings[22].load(),chest=settings[23].load(),money=settings[24].load();
 if(inBattle){if(enemy)enemyLoot(rewardMultiplier(enemy));return;}
 if((!gathering&&!chest&&!money)||!knownFieldReady)return;
 auto deadline=GetTickCount64()+4;
 for(unsigned checked=0;checked<32&&!knownFieldActors.empty()&&GetTickCount64()<deadline;++checked){
  if(fieldRewardCursor>=knownFieldActors.size())fieldRewardCursor=0;
  const auto& actor=knownFieldActors[fieldRewardCursor++];
  if(gathering)multiplyMaterial(actor,rewardMultiplier(gathering));if(chest)multiplyChest(actor,rewardMultiplier(chest));if(money)multiplyChestZenny(actor,rewardMultiplier(money));
 }
}
}
