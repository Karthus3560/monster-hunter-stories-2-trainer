void battleItemsChecks(){
 using namespace battle;
 auto world=reinterpret_cast<uintptr_t>(VirtualAlloc(nullptr,0x600000,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));require(world!=0,"allocate isolated battle item inventory");
 auto save=world+0x10000,owner=world+0x1000;*reinterpret_cast<uintptr_t*>(owner)=save;Path path{{owner,save}};
 auto slot=reinterpret_cast<InventoryEntry*>(save+0x23ae84);auto definition=battleItemDefinitions[0];
 auto sample=[&](){preserveInventory(save,path,true);};
 settings[20]=1;*slot={definition.id,5};sample();slot->count=4;sample();require(slot->count==5,"desktop method restores one battle item use");
 slot->count=8;sample();require(slot->count==8,"battle pickups and reward increases remain allowed");slot->count=7;sample();require(slot->count==8,"increased count becomes the next preservation baseline");
 slot->count=6;sample();require(slot->count==6,"multi-item decreases are not overwritten");
 clearBattleInventory();*slot={definition.id,1};sample();*slot={0,0};sample();require(slot->id==definition.id&&slot->count==1,"last battle item restores ID and quantity together");
 settings[20]=0;slot->count=0;sample();require(slot->count==0&&inventoryOwner==0,"Off stops preservation and clears captures");
 settings[20]=1;*slot={definition.id,5};sample();slot->count=4;preserveInventory(save,path,false);require(slot->count==4&&inventoryOwner==0,"field and camp item use is untouched");
 *slot={definition.id,5};sample();slot->count=4;*reinterpret_cast<uintptr_t*>(owner)=0;sample();require(slot->count==4&&inventoryOwner==0,"stale save ownership blocks inventory writes");*reinterpret_cast<uintptr_t*>(owner)=save;
 *slot={definition.id,5};sample();slot->id=497;slot->count=4;sample();require(slot->id==497&&slot->count==4,"currency and changed slot IDs are excluded");
 *slot={definition.id,static_cast<unsigned short>(definition.capacity+1)};sample();require(battleInventory[0].id==0,"invalid item capacity clears capture");
 InventoryEntry expected{definition.id,5},replacement{definition.id,6};*slot={definition.id,4};require(!replaceInventoryEntry(reinterpret_cast<uintptr_t>(slot),expected,replacement)&&slot->count==4,"atomic slot commit refuses a newer game value");
 require(!battleItem(159)&&!battleItem(160)&&!battleItem(161)&&!battleItem(497),"Growth nutriments and Bottle Caps cannot be replenished");
 *slot={definition.id,5};sample();slot->count=4;DWORD old=0;auto page=reinterpret_cast<void*>(reinterpret_cast<uintptr_t>(slot)&~uintptr_t(4095));VirtualProtect(page,4096,PAGE_READONLY,&old);sample();require(slot->count==4,"read-only inventory is never written");DWORD ignored=0;VirtualProtect(page,4096,old,&ignored);
 clearBattleInventory();*slot={definition.id,5};sample();slot->count=4;stop();require(slot->count==4&&inventoryOwner==0,"stop discards battle baseline without restoring quantities");
 VirtualFree(reinterpret_cast<void*>(world),0,MEM_RELEASE);
}
