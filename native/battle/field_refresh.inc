// Actor-reference cache only. Protection and owner checks remain fresh on writes.
inline bool radarActorType(uintptr_t vt){auto offset=vt-image;return offset==0x15f2528||offset==0x15f1f68||offset==0x15d0288||offset==0x15d0728||offset==0x15ef848;}
struct IncrementalFieldScan{
 Path root,groupPath,packagePath;
 uintptr_t entry=0,end=0,slots=0,objects=0;
 int slotCount=0,slotIndex=0,objectCount=0,objectIndex=0,stage=0;
 unsigned packages=0,visited=0;
 bool active=false;
 bool begin(const Path& field){
  *this={};root=field;auto manager=follow(image+0x1d386c0,root);auto list=manager?follow(manager+0x100,root):0;
  if(!list)return false;entry=dataPointer(list+0x10,root);end=q(list+0x18);root.push_back({list+0x18,end});
  active=entry&&end>=entry&&(end-entry)%40==0&&end-entry<=40*512&&accessible(entry,end-entry);return active;
 }
 // 1=actor, 0=pass finished/invalid, -1=budget exhausted. Each hierarchy step costs one unit.
 int next(FieldActor& out,unsigned& budget,ULONGLONG deadline){
  while(active&&budget&&GetTickCount64()<deadline){
   --budget;
   if(stage==0){
    if(entry>=end){active=false;return 0;}
    groupPath=root;auto group=follow(entry+0x20,groupPath);entry+=40;if(!group)continue;
    if(!read(group+0x2d0,slotCount)||slotCount<0||slotCount>2048)continue;
    slots=dataPointer(group+0x2e0,groupPath);if(!slots||!accessible(slots,slotCount*8))continue;
    groupPath.push_back({group+0x2d0,q(group+0x2d0)});slotIndex=0;stage=1;
   }else if(stage==1){
    if(slotIndex>=slotCount){stage=0;continue;}
    if(++packages>8192){active=false;return 0;}
    packagePath=groupPath;auto slot=follow(slots+slotIndex++*8,packagePath);if(!slot)continue;
    auto package=follow(slot+0x18,packagePath);
    if(!package){if(q(slot+0x18))continue;packagePath.push_back({slot+0x18,0});package=follow(slot+0x10,packagePath);}
    if(!package||q(q(package)+0x80)!=image+0x49b1d0)continue;
    if(!read(package+0xe8,objectCount)||objectCount<0||objectCount>4096)continue;
    objects=dataPointer(package+0xf8,packagePath);if(!objects||!accessible(objects,objectCount*8))continue;
    packagePath.push_back({package+0xe8,q(package+0xe8)});objectIndex=0;stage=2;
   }else{
    if(objectIndex>=objectCount){stage=1;continue;}
    // Pure traversal is bounded and copyRead-guarded. Full path is checked before any write.
    if(++visited>16384){active=false;return 0;}
    Path path=packagePath;auto wrapper=follow(objects+objectIndex++*8,path);
    if(!wrapper||q(q(wrapper)+0x28)!=image+0x3a48a0)continue;
    auto actor=follow(wrapper+0x298,path);auto vt=actor?q(actor):0;
    if(actor&&radarActorType(vt)){out={actor,vt,std::move(path)};return 1;}
   }
  }
  return active?-1:0;
 }
};
inline IncrementalFieldScan fieldScan;
inline std::vector<FieldActor> knownFieldActors;
inline std::vector<unsigned> fieldActorEpochs;
inline std::map<uintptr_t,size_t> fieldActorIndices;
inline Path knownFieldRoot;
inline bool knownFieldReady=false;
inline unsigned fieldScanEpoch=0;
inline ULONGLONG nextFieldScan=0;
inline std::atomic<unsigned> fieldScanPasses=0,fieldScanBatchMs=0;
inline std::atomic<unsigned> knownFieldCount=0;
inline std::atomic<ULONGLONG> radarTouchAt=0;
inline void clearFieldCache(){fieldScan={};knownFieldActors.clear();fieldActorEpochs.clear();fieldActorIndices.clear();knownFieldRoot.clear();knownFieldReady=false;knownFieldCount=0;nextFieldScan=0;}
inline void publishFieldActor(FieldActor actor){
 auto found=fieldActorIndices.find(actor.address);
 if(found==fieldActorIndices.end()){
  if(knownFieldActors.size()>=4096)return;
  fieldActorIndices[actor.address]=knownFieldActors.size();knownFieldActors.push_back(std::move(actor));fieldActorEpochs.push_back(fieldScanEpoch);
 }else{knownFieldActors[found->second]=std::move(actor);fieldActorEpochs[found->second]=fieldScanEpoch;}
 knownFieldCount=static_cast<unsigned>(knownFieldActors.size());
}
inline void refreshFieldActors(){
 if(inBattle||!(settings[15]||settings[16]||settings[21]||settings[23]||settings[24])){clearFieldCache();return;}
 Path field;uintptr_t player=0,controller=0;
 if(!fieldScene(field,player,controller)){clearFieldCache();return;}
 if(!samePath(field,knownFieldRoot)||!valid(knownFieldRoot)||(fieldScan.active&&!valid(fieldScan.root))){clearFieldCache();knownFieldRoot=field;}
 knownFieldReady=true;auto start=GetTickCount64();
 if(!fieldScan.active){
  if(start<nextFieldScan)return;
  if(!fieldScan.begin(field)){knownFieldReady=false;nextFieldScan=start+100;return;}
  ++fieldScanEpoch;
 }
 unsigned budget=256;auto deadline=start+4;int outcome=-1;
 while(budget&&GetTickCount64()<deadline){FieldActor actor;outcome=fieldScan.next(actor,budget,deadline);if(outcome==1)publishFieldActor(std::move(actor));else break;}
 if(outcome==0){
  // Only completed, stable passes can retire absent actors. A changed path retries promptly.
  if(valid(fieldScan.root)&&fieldScan.entry>=fieldScan.end&&fieldScan.stage==0){
   std::vector<FieldActor> retained;std::vector<unsigned> epochs;fieldActorIndices.clear();
   for(size_t i=0;i<knownFieldActors.size();++i)if(fieldActorEpochs[i]==fieldScanEpoch){fieldActorIndices[knownFieldActors[i].address]=retained.size();retained.push_back(std::move(knownFieldActors[i]));epochs.push_back(fieldScanEpoch);}
   knownFieldActors=std::move(retained);fieldActorEpochs=std::move(epochs);knownFieldCount=static_cast<unsigned>(knownFieldActors.size());++fieldScanPasses;
  }
  nextFieldScan=GetTickCount64()+250;
 }
 fieldScanBatchMs=static_cast<unsigned>(GetTickCount64()-start);
}
