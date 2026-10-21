#include "battle_engine.h"
#include <cstdio>
#include <cstdlib>
void require(bool passed,const char* name){if(!passed){fprintf(stderr,"FAIL: %s\n",name);exit(1);}printf("PASS: %s\n",name);}
#include "movement_checks.inc"
#include "exploration_checks.inc"
#include "inventory_checks.inc"
#include "battle_items_checks.inc"
#include "economy_checks.inc"
#include "egg_checks.inc"
int main(){
 using namespace battle;
 auto block=reinterpret_cast<uintptr_t>(VirtualAlloc(nullptr,65536,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));require(block!=0,"allocate isolated fixtures");
 image=block;auto owner=block+256,a=block+512,guard=block+128;
 *reinterpret_cast<uintptr_t*>(owner)=block+1024;*reinterpret_cast<uintptr_t*>(guard)=owner;
 Path path;require(follow(guard,path)==owner,"validated owner chain");
 auto value=reinterpret_cast<unsigned short*>(a);*value=123;
 bool covered[29]{};bool legal=true;unsigned offered=0;
 for(unsigned tab=0;tab<catalog::Tabs;++tab){
  legal&=catalog::counts[tab]>0&&catalog::counts[tab]<=catalog::Capacity;
  for(unsigned row=0;row<catalog::counts[tab];++row){
   const auto& r=catalog::rows[tab][row];legal&=r.feature>=0&&r.feature<29&&!covered[r.feature];
   covered[r.feature]=true;++offered;
   for(unsigned v=0;v<=catalog::maxima[r.feature];++v){
    const int selected=catalog::selection(tab,row,v);
    legal&=selected>=0&&selected<static_cast<int>(r.count);
    legal&=catalog::applyChoice(tab,row,v,selected)==v;
    for(unsigned c=0;c<r.count;++c)legal&=catalog::valid(r.feature,catalog::applyChoice(tab,row,v,c));
   }
  }
 }
 for(bool c:covered)legal&=c;
 require(legal&&catalog::Tabs==7&&offered==29,"seven categories expose each feature exactly once and all choices remain valid");
 require(catalog::selection(0,0,0)==0&&catalog::selection(0,0,1)==1,"native Off and On map directly to setting state");
 require(catalog::applyChoice(0,3,0,0)==1&&catalog::applyChoice(0,3,20,2)==19,"damage stepper adjusts in both directions");
 require(catalog::applyChoice(1,0,36,2)==36&&catalog::applyChoice(1,0,0,0)==0,"stepper clamps at both endpoints");
 require(catalog::applyChoice(1,0,18,1)==18,"center value confirmation is inert");
 require(catalog::selection(5,2,0)==-1&&catalog::selection(99,0,0)==-1,"short categories expose no leftover choices");
 require(catalog::step(4,0,-1)==0&&catalog::step(4,36,1)==36,"attack adjustment clamps both endpoints");
 require(catalog::attack(1)==1.25&&catalog::attack(36)==10,"quarter-step attack range");
 require(catalog::exp(76)==20&&catalog::speed(8)==3,"EXP and speed upper bounds");
 require(catalog::protection(1)==5&&catalog::protection(20)==100,"five-percent damage protection steps");
 require(catalog::step(0,0,-1)==1&&catalog::step(0,1,1)==0,"boolean adjustment works in either direction");
 bool roundTrips=true,walks=true;
 for(unsigned f: {4u,6u,8u,9u,10u,11u,12u,13u,14u,19u,21u,22u,23u,24u,26u,27u}){
  for(unsigned v=0;v<=catalog::maxima[f];++v){
   roundTrips&=catalog::numericSetting(f,catalog::numericValue(f,v))==v;
   for(unsigned c=0;c<3;++c)walks&=catalog::valid(f,catalog::numericChoice(f,v,c));
  }
  unsigned setting=catalog::numericSetting(f,0);
  for(unsigned i=0;i<catalog::maxima[f];++i)setting=catalog::numericChoice(f,setting,2);
  walks&=catalog::numericValue(f,setting)==catalog::maxima[f];
  for(unsigned i=0;i<catalog::maxima[f];++i)setting=catalog::numericChoice(f,setting,0);
  walks&=catalog::numericValue(f,setting)==0;
 }
 require(roundTrips,"displayed numeric values round-trip for every supported setting");
 require(walks,"repeated native selections traverse all fourteen adjustable ranges in both directions");
 require(catalog::numericChoice(9,0,0)==1&&catalog::numericChoice(9,20,2)==19,"damage directions follow the displayed percentage");
 require(catalog::numericChoice(4,3,1)==3,"confirming the current numeric value is inert");
 *value=100;apply<unsigned short>(4,a,1.25,30000,path,true,true);require(*value==125,"fractional attack multiplier");
 for(int i=0;i<20;++i)apply<unsigned short>(4,a,1.25,30000,path,true,true);
 require(*value==125,"fractional attack never compounds");restore(4);require(*value==100,"fractional attack restores baseline");
 *value=123;
 require(apply<unsigned short>(4,a,2,30000,path,true,true)&&*value==246,"attack multiplier");
 for(int i=0;i<40;++i)apply<unsigned short>(4,a,2,30000,path,true,true);
 require(*value==246,"repeated refresh never compounds attack");
 apply<unsigned short>(4,a,10,30000,path,true,true);require(*value==1230,"changing preset uses original attack");
 restore(4);require(*value==123,"Off restores attack baseline");
 apply<unsigned short>(4,a,2,30000,path,true,true);*value=200;apply<unsigned short>(4,a,2,30000,path,true,true);require(*value==400,"game changes refresh the baseline");
 *value=333;restore(4);require(*value==333,"restoration preserves newer game values");
 *value=9999;apply<unsigned short>(4,a,10,30000,path,true,true);require(*value==30000,"attack clamp");restore(4);
 auto exp=reinterpret_cast<int*>(a+16);*exp=250;apply<int>(6,a+16,5,999999,path,true,true);require(*exp==1250,"EXP multiplier");
 apply<int>(6,a+16,5,999999,path,true,true);require(*exp==1250,"EXP does not compound");restore(6);require(*exp==250,"EXP restoration");
 *value=123;apply<unsigned short>(4,a,2,30000,path,true,true);*reinterpret_cast<uintptr_t*>(guard)=0;
 restore(4);require(*value==246,"stale owner is never written during restoration");
 require(!apply<unsigned short>(0,a,9999,65535,path)&&*value==246,"stale owner blocks active writes");
 *reinterpret_cast<uintptr_t*>(guard)=owner;
 auto hp=reinterpret_cast<unsigned short*>(a+32);*hp=23;apply<unsigned short>(0,a+32,9999,65535,path);restore(0);require(*hp==9999,"disabling health never restores old damage");
 auto rate=reinterpret_cast<float*>(a+40);*rate=1.f;apply<float>(8,a+40,3.f,4.f,path,true,true);require(*rate==3.f,"fast-forward multiplier");restore(8);require(*rate==1.f,"fast-forward restoration");
 unsigned short before=*value;DWORD protection=0;VirtualProtect(reinterpret_cast<void*>(block),65536,PAGE_READONLY,&protection);
 require(!apply<unsigned short>(0,a,9999,65535,path)&&*value==before,"read-only memory cannot be changed");
 VirtualProtect(reinterpret_cast<void*>(block),65536,protection,&protection);
 require(!accessible(1,4)&&!copyRead(0,nullptr,4),"invalid memory rejected");
 for(auto& s:settings)s=1;stop();bool off=true;for(auto& s:settings)off&=s==0;require(off&&saved.empty(),"disable all resets every feature");
 movementChecks();explorationChecks();inventoryChecks();battleItemsChecks();economyChecks();eggChecks();
 VirtualFree(reinterpret_cast<void*>(block),0,MEM_RELEASE);puts("All native Battle engine checks passed.");
}
