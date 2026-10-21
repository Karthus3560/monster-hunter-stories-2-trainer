void inventoryChecks(){
 using namespace battle;auto priorImage=image;auto world=reinterpret_cast<uintptr_t>(VirtualAlloc(nullptr,0x2000000,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));require(world!=0,"allocate isolated Inventory world");image=world;
 auto qput=[](uintptr_t a,uintptr_t v){*reinterpret_cast<uintptr_t*>(a)=v;};
 require(!ordinaryReward(0)&&!ordinaryReward(497)&&ordinaryReward(ordinaryItemIds[0]),"ordinary loot whitelist excludes empty IDs and Bottle Caps");
 auto actor=world+0x10000,lot=world+0x14000,owner=world+0x16000;
 qput(actor,world+0x15f2528);qput(owner,actor);Path path{{owner,actor},{actor,world+0x15f2528}};
 qput(actor+0x27c0,lot);*reinterpret_cast<unsigned char*>(lot+0x14)=1;*reinterpret_cast<int*>(lot+0x18)=ordinaryItemIds[0];*reinterpret_cast<unsigned char*>(actor+0x27c8)=3;
 FieldActor material{actor,world+0x15f2528,path};multiplyMaterial(material,5);
 require(*reinterpret_cast<unsigned char*>(actor+0x27c8)==15,"material loot multiplies native reward quantity");
 for(int i=0;i<30;++i)multiplyMaterial(material,5);require(*reinterpret_cast<unsigned char*>(actor+0x27c8)==15,"material loot maintenance never compounds");
 multiplyMaterial(material,10);require(*reinterpret_cast<unsigned char*>(actor+0x27c8)==30,"loot multiplier edit retains original reward baseline");restore(21);require(*reinterpret_cast<unsigned char*>(actor+0x27c8)==3,"normal loot restores unreleased original reward quantity");
 *reinterpret_cast<int*>(lot+0x18)=497;multiplyMaterial(material,10);require(*reinterpret_cast<unsigned char*>(actor+0x27c8)==3,"material multiplier refuses Bottle Cap rewards");
 *reinterpret_cast<int*>(lot+0x18)=ordinaryItemIds[0];*reinterpret_cast<unsigned char*>(lot+0x14)=2;multiplyMaterial(material,10);require(*reinterpret_cast<unsigned char*>(actor+0x27c8)==3,"material multiplier refuses non-item reward lots");*reinterpret_cast<unsigned char*>(lot+0x14)=1;
 auto manager=world+0x20000,controller=world+0x24000,unit=world+0x28000,parameter=world+0x30000;
 qput(world+0x1dec560,manager);qput(manager,world+0x1000);qput(manager+0x9f8,controller);qput(controller,world+0x1000);qput(controller+0x20,unit);qput(unit,world+0x1000);qput(unit+0xb0,parameter);qput(parameter,world+0x9000);qput(world+0x9330,world+0xb7ec30);
 auto data=parameter+0x46c;*reinterpret_cast<unsigned short*>(data)=ordinaryItemIds[0];*reinterpret_cast<unsigned char*>(data+0x12)=2;
 enemyLoot(5);require(*reinterpret_cast<unsigned char*>(data+0x12)==10,"enemy loot uses verified native item reward slots");
 for(int i=0;i<30;++i)enemyLoot(5);require(*reinterpret_cast<unsigned char*>(data+0x12)==10,"enemy loot maintenance never compounds");restore(22);
 multiplyMaterial(material,5);enemyLoot(2);require(*reinterpret_cast<unsigned char*>(actor+0x27c8)==15&&*reinterpret_cast<unsigned char*>(data+0x12)==4,"gathering5x and battle2x operate independently");
 restore(21);require(*reinterpret_cast<unsigned char*>(actor+0x27c8)==3&&*reinterpret_cast<unsigned char*>(data+0x12)==4,"disabling gathering leaves battle multiplier intact");restore(22);
 *reinterpret_cast<unsigned short*>(data)=497;enemyLoot(5);require(*reinterpret_cast<unsigned char*>(data+0x12)==2,"enemy loot excludes currency rewards");
 VirtualFree(reinterpret_cast<void*>(world),0,MEM_RELEASE);image=priorImage;
}
