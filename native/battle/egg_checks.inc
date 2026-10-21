void eggChecks(){
 using namespace battle;auto priorImage=image;
 auto world=reinterpret_cast<uintptr_t>(VirtualAlloc(nullptr,0x2000000,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));require(world!=0,"allocate isolated egg table world");image=world;
 auto put=[](uintptr_t a,uintptr_t v){*reinterpret_cast<uintptr_t*>(a)=v;};
 auto u=[](uintptr_t a)->unsigned&{return *reinterpret_cast<unsigned*>(a);};
 auto manager=world+0x10000,eggTable=world+0x20000,denTable=world+0x21000,eggRows=world+0x22000,denRows=world+0x23000;
 put(world+0x1debc10,manager);put(manager,world+0x100);put(manager+0x758,eggTable);put(manager+0x6c8,denTable);
 put(eggTable,world+0x172dc00);put(denTable,world+0x172d630);u(eggTable+0xd0)=13;u(denTable+0xd0)=30;put(eggTable+0xe0,eggRows);put(denTable+0xe0,denRows);
 auto populate=[&](bool loaded){
  for(unsigned i=0;i<13;++i){auto row=world+0x30000+i*0x100;put(eggRows+i*8,row);put(row,world+0x1723520);auto& id=eggdata::eggIdentity[i];
   u(row+0x10)=id[0];u(row+0x20)=id[1];u(row+0x24)=id[2];*reinterpret_cast<unsigned char*>(row+0x28)=static_cast<unsigned char>(id[3]);*reinterpret_cast<unsigned char*>(row+0x29)=static_cast<unsigned char>(id[4]);
   memcpy(reinterpret_cast<void*>(row+0x2c),id+5,32);memcpy(reinterpret_cast<void*>(row+0x4c),loaded?eggdata::loadedEgg[i]:eggdata::stockEgg[i],64);
  }
  for(unsigned i=0;i<30;++i){auto row=world+0x40000+i*0x100;put(denRows+i*8,row);put(row,world+0x1722c10);auto& id=eggdata::denIdentity[i];u(row+0x10)=id[0];u(row+0x20)=id[1];memcpy(reinterpret_cast<void*>(row+0x30),id+2,16);memcpy(reinterpret_cast<void*>(row+0x44),loaded?eggdata::loadedDen[i]:eggdata::stockDen[i],16);}
 };
 populate(true);EggTableLedger eggs{true},dens{false};auto first=q(eggRows),firstDen=q(denRows);
 auto beforeWrites=writes.load();require(eggs.update(4,1)==2&&dens.update(4)==2&&writes==beforeWrites,"current archive egg and den defaults require no initial writes");
 require(eggs.update(0,0)==2&&!memcmp(reinterpret_cast<void*>(first+0x4c),eggdata::stockEgg[0],64),"Normal explicitly uses stock odds despite installed Rainbow archive");
 require(dens.update(0)==2&&!memcmp(reinterpret_cast<void*>(firstDen+0x44),eggdata::stockDen[0],16),"Normal dens restore stock weights despite installed all-rare archive");
 bool exact=true,sums=true,multiplayer=true,locked=true;
 for(unsigned m=0;m<5;++m){exact&=eggs.update(m,0)==2;
  for(auto& r:eggs.records){auto weights=reinterpret_cast<unsigned*>(r.address);if(eggdata::eggIdentity[r.index][4]){multiplayer&=!memcmp(weights,eggdata::stockEgg[r.index],64);continue;}
   exact&=!memcmp(weights,eggdata::eggPresets[m][r.index],48);
   for(unsigned pull=0;pull<4;++pull)sums&=weights[pull]+weights[4+pull]+weights[8+pull]==eggdata::stockEgg[r.index][pull]+eggdata::stockEgg[r.index][4+pull]+eggdata::stockEgg[r.index][8+pull];
  }
 }
 require(exact&&sums,"every egg preset matches desktop arithmetic and preserves probability totals");require(multiplayer,"egg presets leave all multiplayer rows unchanged");
 require(u(first+0x6c)==100&&u(first+0x4c)==0,"Rainbow permits the highest tier even where stock first pull has none");
 eggs.update(3,0);require(u(first+0x6c)==0&&u(first+0x5c)==100,"best available respects originally eligible egg tiers");
 auto retreat=q(eggRows+6*8);require(!memcmp(reinterpret_cast<void*>(retreat+0x4c),eggdata::stockEgg[6],48),"boost and best-available leave retreat quality odds alone");
 eggs.update(4,1);require(u(retreat+0x6c)==100,"Rainbow covers solo retreat nests like the original trainer");
 bool noLost=true;for(auto& r:eggs.records)if(!eggdata::eggIdentity[r.index][4])for(unsigned n=0;n<4;++n)noLost&=u(r.row+0x7c+n*4)==0;
 require(noLost,"no-empty control zeroes solo lost chances independently");
 eggs.update(4,0);require(u(first+0x84)==30&&u(first+0x88)==50,"empty-pull Off restores stock later-pull chances");
 auto stamp=writes.load();for(int i=0;i<20;++i)eggs.update(4,0);require(writes==stamp,"unchanged settings never reapply egg writes");
 for(unsigned m=0;m<6;++m){exact&=dens.update(m)==2;for(auto& r:dens.records){auto weights=reinterpret_cast<unsigned*>(r.address);exact&=!memcmp(weights,eggdata::denPresets[m][r.index],16);unsigned a=0,b=0;for(int i=0;i<4;++i){a+=weights[i];b+=eggdata::stockDen[r.index][i];}sums&=a==b;if(!eggdata::stockDen[r.index][2])locked&=weights[2]==0;}}
 require(exact&&sums,"all six den presets match original trainer and preserve totals");require(locked,"no den preset introduces a locked super-rare tier");
 bool identities=true;for(auto& r:eggs.records)identities&=eggIdentityMatches(r);for(auto& r:dens.records)identities&=eggIdentityMatches(r);require(identities,"keys eligibility and risk fields remain untouched across presets");
 eggs.restoreOwned();dens.restoreOwned();require(!memcmp(reinterpret_cast<void*>(first+0x4c),eggdata::loadedEgg[0],64)&&!memcmp(reinterpret_cast<void*>(firstDen+0x44),eggdata::loadedDen[0],16),"stop restores captured archive baseline instead of stock selection");
 populate(false);require(eggs.update(4,1)==2&&dens.update(5)==2,"the same engine accepts a verified stock-loaded session");eggs.restoreOwned();dens.restoreOwned();require(!memcmp(reinterpret_cast<void*>(first+0x4c),eggdata::stockEgg[0],64),"stock-loaded baseline restores stock on stop");
 populate(true);eggs.update(0,0);u(first+0x4c)=77;auto later=q(eggRows+5*8);unsigned laterBefore=u(later+0x4c);require(eggs.update(1,1)==3&&u(later+0x4c)==laterBefore,"one foreign probability change blocks whole-table preset application");eggs.restoreOwned();require(u(first+0x4c)==77,"stop preserves foreign probability changes");
 populate(true);eggs.update(0,0);u(first+0x10)^=1;require(eggs.update(1,0)==3,"changed row key blocks writes even when pointer and type match");eggs.restoreOwned();require(u(first+0x4c)==90,"changed row identity blocks restoration");
 populate(true);eggs.update(0,0);put(manager+0x758,0);eggs.restoreOwned();require(u(first+0x4c)==90,"stale resource owner blocks restoration");put(manager+0x758,eggTable);
 populate(true);u(eggTable+0xd0)=12;require(eggs.update(4,1)==1,"unexpected egg row count is rejected");u(eggTable+0xd0)=13;
 u(first+0x29)=1;require(eggs.update(4,1)==1,"solo and multiplayer identities cannot be confused by duplicate key");populate(true);
 u(first+0x4c)=999;require(eggs.update(4,1)==1,"unknown archive probability modifications are rejected");populate(true);
 put(first,world+0x1722c10);require(eggs.update(4,1)==1,"wrong row type is rejected before writes");populate(true);
 require(eggs.update(9,0)==3&&dens.update(9)==3,"invalid egg and den selections are rejected");
 populate(true);eggs.update(0,0);auto last=q(eggRows+12*8);u(last+0x10)^=1;
 require(eggs.update(1,1)==3&&u(first+0x4c)==90,"last-row identity failure prevents earlier rows from being changed");eggs.restoreOwned();
 populate(true);eggs.update(0,0);DWORD protection=0;VirtualProtect(reinterpret_cast<void*>(first),4096,PAGE_READONLY,&protection);
 require(eggs.update(4,1)==3&&u(first+0x4c)==90,"fresh write-permission checks reject read-only egg rows");
 eggs.restoreOwned();VirtualProtect(reinterpret_cast<void*>(first),4096,protection,&protection);
 populate(true);eggs.update(0,0);auto replacement=world+0x50000;memcpy(reinterpret_cast<void*>(replacement),reinterpret_cast<void*>(eggTable),0x100);put(manager+0x758,replacement);
 // A resource reload supplies fresh archive-backed rows. The old owner's
 // captures cannot restore through the replaced manager resource pointer.
 populate(true);require(eggs.update(0,0)==2&&u(first+0x4c)==90,"resource replacement rebinds fresh verified tables");
 eggs.restoreOwned();require(u(first+0x6c)==100,"rebound resource restores its own captured archive baseline");
 eggs.restoreOwned();dens.restoreOwned();VirtualFree(reinterpret_cast<void*>(world),0,MEM_RELEASE);image=priorImage;
}

