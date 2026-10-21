#pragma once
#include "egg_table_data.h"
namespace battle {
// This ledger is deliberately separate from generic restore(feature): Normal
// means verified stock odds, whereas stopping restores the loaded archive odds.
inline std::atomic<unsigned> eggTableStatus=0,denTableStatus=0,eggTableChanges=0;
struct EggRecord {
 uintptr_t row=0,address=0;unsigned index=0;bool egg=false;Path path;
 std::array<unsigned,16> original{},written{};
 size_t bytes()const{return egg?64:16;}
};
inline bool eggIdentityMatches(const EggRecord& r){
 unsigned key=0;if(!read(r.row+0x10,key))return false;
 if(r.egg){
  const auto& id=eggdata::eggIdentity[r.index];unsigned level=0,rare=0;unsigned char homing=0,multi=0;unsigned risk[8]{};
  return key==id[0]&&read(r.row+0x20,level)&&level==id[1]&&read(r.row+0x24,rare)&&rare==id[2]
   &&read(r.row+0x28,homing)&&homing==id[3]&&read(r.row+0x29,multi)&&multi==id[4]
   &&copyRead(r.row+0x2c,risk,sizeof(risk))&&!memcmp(risk,id+5,sizeof(risk));
 }
 const auto& id=eggdata::denIdentity[r.index];unsigned spot=0,progress[4]{};
 return key==id[0]&&read(r.row+0x20,spot)&&spot==id[1]&&copyRead(r.row+0x30,progress,sizeof(progress))&&!memcmp(progress,id+2,sizeof(progress));
}
inline bool eggRecordValid(const EggRecord& r){return valid(r.path)&&eggIdentityMatches(r);}
struct EggTableLedger {
 bool egg=false,bound=false,conflict=false;uintptr_t resource=0,rows=0;Path root;std::vector<EggRecord> records;
 unsigned lastMode=~0u,lastLost=~0u;
 size_t offset()const{return egg?0x758:0x6c8;}
 unsigned count()const{return egg?13:30;}
 bool currentRoot()const{return bound&&valid(root);}
 void restoreOwned(){
  for(auto& r:records){std::array<unsigned,16> current{};
   if(eggRecordValid(r)&&copyRead(r.address,current.data(),r.bytes())&&!memcmp(current.data(),r.written.data(),r.bytes())&&eggRecordValid(r))copyWrite(r.address,r.original.data(),r.bytes());
  }
  records.clear();root.clear();resource=rows=0;bound=false;conflict=false;lastMode=lastLost=~0u;
 }
 bool bind(){
  Path p;auto manager=follow(image+0x1debc10,p);auto table=manager?follow(manager+offset(),p):0;
  if(!table||q(table)!=image+(egg?0x172dc00:0x172d630))return false;
  unsigned n=0;if(!read(table+0xd0,n)||n!=count())return false;
  auto list=q(table+0xe0);if(list<65536)return false;
  p.push_back({table+0xd0,q(table+0xd0)});p.push_back({table+0xe0,list});
  std::vector<EggRecord> candidates;bool stock=true,loaded=true;
  for(unsigned i=0;i<n;++i){
   EggRecord r{};r.index=i;r.egg=egg;r.path=p;r.row=follow(list+i*8,r.path);r.address=r.row+(egg?0x4c:0x44);
   if(!r.row||q(r.row)!=image+(egg?0x1723520:0x1722c10)||!eggRecordValid(r)||!copyRead(r.address,r.original.data(),r.bytes())||!accessible(r.address,r.bytes(),true))return false;
   const auto* normal=egg?eggdata::stockEgg[i]:eggdata::stockDen[i];
   const auto* installed=egg?eggdata::loadedEgg[i]:eggdata::loadedDen[i];
   stock&=!memcmp(r.original.data(),normal,r.bytes());loaded&=!memcmp(r.original.data(),installed,r.bytes());
   r.written=r.original;candidates.push_back(std::move(r));
  }
  if((!stock&&!loaded)||!valid(p))return false;
  root=std::move(p);resource=table;rows=list;records=std::move(candidates);bound=true;conflict=false;lastMode=lastLost=~0u;return true;
 }
 unsigned update(unsigned mode,unsigned noLost=0){
  if(mode>(egg?4u:5u)||noLost>1)return 3;
  if(!currentRoot()){restoreOwned();if(!bind())return 1;}
  if(conflict)return 3;
  if(mode==lastMode&&(!egg||noLost==lastLost))return 2;
  // Validate every row, owner and last-written value before changing any row.
  std::vector<std::array<unsigned,16>> targets;
  for(auto& r:records){std::array<unsigned,16> current{},target=r.original;
   if(!eggRecordValid(r)||!copyRead(r.address,current.data(),r.bytes())||memcmp(current.data(),r.written.data(),r.bytes())||!accessible(r.address,r.bytes(),true)){conflict=true;return 3;}
   if(egg){
    if(!eggdata::eggIdentity[r.index][4]){
     memcpy(target.data(),eggdata::eggPresets[mode][r.index],48);
     for(unsigned i=0;i<4;++i)target[12+i]=noLost?0:eggdata::stockEgg[r.index][12+i];
    }
   }else memcpy(target.data(),eggdata::denPresets[mode][r.index],16);
   targets.push_back(target);
  }
  // Small, contiguous probability blocks only. Eligibility, risk and species
  // tables are never written; no egg actor position fields are touched.
  std::vector<std::array<unsigned,16>> previous;for(auto& r:records)previous.push_back(r.written);
  for(size_t i=0;i<records.size();++i){auto& r=records[i];auto& target=targets[i];
   if(!memcmp(target.data(),r.written.data(),r.bytes()))continue;
   std::array<unsigned,16> current{};
   if(!eggRecordValid(r)||!copyRead(r.address,current.data(),r.bytes())||memcmp(current.data(),r.written.data(),r.bytes())||!eggRecordValid(r)||!copyWrite(r.address,target.data(),r.bytes())){
    conflict=true;
    // Roll back only bytes still owned by this attempt.
    for(size_t j=0;j<i;++j){auto& prior=records[j];std::array<unsigned,16> now{};
     if(eggRecordValid(prior)&&copyRead(prior.address,now.data(),prior.bytes())&&!memcmp(now.data(),prior.written.data(),prior.bytes())&&eggRecordValid(prior)&&copyWrite(prior.address,previous[j].data(),prior.bytes()))prior.written=previous[j];
    }
    return 3;
   }
   r.written=target;++writes;++eggTableChanges;
  }
  lastMode=mode;lastLost=noLost;return 2;
 }
};
inline EggTableLedger eggOdds{true},denOdds{false};
inline void eggHunt(){eggTableStatus=eggOdds.update(settings[26],settings[28]);denTableStatus=denOdds.update(settings[27]);}
inline void eggStop(){eggOdds.restoreOwned();denOdds.restoreOwned();eggTableStatus=denTableStatus=0;}
}
