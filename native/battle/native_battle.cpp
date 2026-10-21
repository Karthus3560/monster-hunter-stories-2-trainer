#include <Windows.h>
#include <bcrypt.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include <atomic>
#include "sample_config.h"
#include "battle_engine.h"
#include "battle_proofs.h"
#include "ui_texture.h"
#include "runtime_guard.h"
#include "studio.h"
extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace {
uintptr_t base;
std::wstring logPath,statePath,stopPath;
std::atomic<bool> modMode=true, installed=false, battlePageReady=false;
int legacyRow[]{71,0,0};
unsigned activeTab=0;
std::atomic<unsigned> uiFrames=0,uiErrors=0;
bool buildingRows=false;
std::atomic<unsigned> setters=0,reads=0,cardBuilds=0;
struct Hook {void** slot;void* original;void* replacement;};
std::vector<Hook> hooks;
// Native Camp Menu row: the label is a game-owned reference-counted UTF-8 string.
struct CampRow {int icon,route;void* label;unsigned char enabled;unsigned char padding[7];};
static_assert(sizeof(CampRow)==24);
struct CampVector {CampRow* begin;CampRow* end;CampRow* capacity;};

struct Option { const char* title; const char* help; };
const Option options[battle::Count]={
 {"Rider health","Keep Rider and battle buddy at 9999 HP during battle."},
 {"Monstie health","Keep allied Monsties at 9999 HP during battle."},
 {"Full Kinship","Refill normal and mounted Kinship for both allied parties."},
 {"Party hearts","Keep both allied parties at three hearts during battle."},
 {"Attack multiplier","Adjust attack from 1x to 10x in 0.25x steps. Final damage depends on defense."},
 {"Turn counter","Normal progression or hold turn 1. Disabling resumes the counter."},
 {"Battle EXP","Adjust EXP from 1x to 20x in 0.25x steps. Set before defeating enemies."},
 {"Kinship attack level","Maintain mounted Level 3. Full Kinship is a separate control."},
 {"Battle speed","Adjust battle speed from 1x to 3x in 0.25x steps. Normal speed outside battle."},
 {"Incoming damage","Adjust damage taken from 100% to 0% in 5% steps. Applies NEXT battle; disable before saving."},
 {"Walking & running","Adjust on-foot speed from 1x to 3x in 0.25x steps. Set 1x to restore normal speed."},
 {"Mounted speed","Adjust ground riding and Dash speed from 1x to 3x. Does not change battle speed."},
 {"Flying speed","Adjust flight speed from 1x to 3x. Flight must be unlocked; take off to apply."},
 {"Swimming speed","Adjust water riding speed from 1x to 3x. Swimming must already be unlocked."},
 {"Egg carrying speed","Adjust egg carrying speed from 1x to 3x. Set 1x to restore normal speed."},
 {"Map radar","Reveal loaded material, egg and monster map markers in the current field."},
 {"4x gathering range","Reach ordinary plants, ore and insects from four times farther away. Eggs and chests stay normal."},
 {"Faster gathering","Play manual plant, ore and insect gathering animations at 4x speed. Press the normal gather button."},
 {"100% retreat","Defeated monsters retreat whenever allowed. Boss and story restrictions still apply. Enable before defeat."},
 {"Prayer Pot effect","Choose a permanent prayer; pot level sets its strength. Off restores the previous prayer. Separate from damage protection."},
 {"Preserve battle items","Keep ordinary healing and support items after battle use, using the desktop trainer method. Enable before using an item. Field and Growth use stay normal."},
 {"Gathering multiplier","Choose normal, 2x, 5x or 10x ordinary gathered materials. Enable before collecting. Battle and chest loot have separate controls."},
 {"Battle loot multiplier","Choose normal, 2x, 5x or 10x ordinary monster item rewards. Enable before defeating enemies."},
 {"Chest item multiplier","Multiply ordinary item rewards from supported chests. Enable before approaching or opening. Equipment, eggs, Bottle Caps and quest keys stay normal."},
 {"Zenny reward multiplier","Multiply earned Zenny when closing battle, quest or expedition results. Supported Zenny chests also scale. Purchases, sales and costs stay normal."},
 {"Preserve crafting materials","Restore ordinary ingredients consumed when closing Combine or committing equipment crafting/upgrades. Own the required materials first. Zenny costs remain normal."},
 {"Egg quality","Choose normal odds, boosted odds, highest available quality or Rainbow. Applies when eggs are generated in solo nests; Rainbow does not guarantee a Rainbow Gene."},
 {"Den rarity","Change newly generated field dens. Boosts respect unlocked den types; All rare selects rare dens, Best unlocked selects the highest unlocked tier. Travel to regenerate dens."},
 {"Prevent empty pulls","Disable the solo nest empty-pull chance. Off restores stock chances. Does not add egg slots or change monster species."},
};
int battleRows[studio::Capacity][3]{{1001,0,0},{1002,0,0},{1003,0,0},{1004,0,0},{1005,0,0},{1006,0,0}};
using RowFn=void(__fastcall*)(void*,unsigned,unsigned);
using PageFn=void(__fastcall*)(void*);
using ChoiceFn=int(__fastcall*)(void*,unsigned);
using GUIDestroyFn=void*(__fastcall*)(void*,unsigned);
GUIDestroyFn originalGUIDestroy=nullptr;
PageFn originalLabels,originalFrame;
RowFn originalChoices;
ChoiceFn originalChoiceCount;
using IntFn=int(__fastcall*)(void*);
using DescFn=void*(__fastcall*)(void*,unsigned);
using ValueFn=int(__fastcall*)(void*,unsigned);
using SetterFn=void(__fastcall*)(void*,unsigned,unsigned);
using VoidFn=void(__fastcall*)(void*);
using RowsFn=void*(__fastcall*)(void*);
using CountFn=int(__fastcall*)(void*,unsigned);
IntFn originalRoute,originalCampRoute,originalCampCount,originalPage;
CountFn originalOtherCount;
PageFn originalBuildRows;
using TabFn=void(__fastcall*)(void*,unsigned);
TabFn originalTabMove,originalTabSelect;
IntFn originalTabCount;
VoidFn originalCampBuild;

RowsFn originalRows;
ValueFn originalValue;
SetterFn originalSetter;
VoidFn originalReset;

void log(const char* message){FILE* f=nullptr;_wfopen_s(&f,logPath.c_str(),L"ab");if(f){fprintf(f,"%llu %s\n",GetTickCount64(),message);fclose(f);}}
void report(){FILE* f=nullptr;_wfopen_s(&f,statePath.c_str(),L"wb");if(f){
 unsigned active=0;for(auto& v:battle::settings)if(v)++active;
 fprintf(f,"{\"installed\":%s,\"nativeMenu\":true,\"campCard\":true,\"sessionOnly\":true,\"battleControls\":9,\"movementControls\":5,\"explorationControls\":5,\"rewardControls\":5,\"inventoryControls\":2,\"uiVersion\":37,\"convenienceControls\":3,\"eggHuntControls\":3,\"numericStyle\":\"text\",\"decorativeButtonObjects\":0,\"tabs\":8,\"modPageSelected\":%s,\"compatible\":%s,\"inBattle\":%s,\"active\":%u,\"setEvents\":%u,\"gameplayValuesWritten\":%llu,\"settings\":[",installed?"true":"false",modMode?"true":"false",battle::compatible?"true":"false",battle::inBattle?"true":"false",active,setters.load(),battle::writes.load());
 for(int i=0;i<battle::Count;++i)fprintf(f,"%s%u",i?",":"",battle::settings[i].load());fprintf(f,"],\"eggTableStatus\":%u,\"denTableStatus\":%u,\"eggTableChanges\":%u,\"tickMs\":%u,\"longestTickMs\":%u,\"fieldActors\":%u,\"fieldScanPasses\":%u,\"fieldScanBatchMs\":%u,\"radarTouchedAt\":%llu,\"craftCommits\":%u,\"craftRestoredSlots\":%u,\"zennyApplications\":%u,\"activeTab\":%u,\"uiFrames\":%u,\"uiErrors\":%u,\"iconApplications\":%u,\"textureLoaded\":%s}\n",battle::eggTableStatus.load(),battle::denTableStatus.load(),battle::eggTableChanges.load(),battle::tickDurationMs.load(),battle::longestTickMs.load(),battle::knownFieldCount.load(),battle::fieldScanPasses.load(),battle::fieldScanBatchMs.load(),battle::radarTouchAt.load(),battle::craftCommits.load(),battle::craftRestoredSlots.load(),battle::zennyApplications.load(),activeTab,uiFrames.load(),uiErrors.load(),ui::iconApplications,ui::loaded?"true":"false");fclose(f);}}

std::string sha256(const void* data,ULONG length){
 BCRYPT_ALG_HANDLE algorithm=nullptr;BCRYPT_HASH_HANDLE hash=nullptr;DWORD size=0,read=0;UCHAR digest[32]{};
 if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)return {};
 BCryptGetProperty(algorithm,BCRYPT_OBJECT_LENGTH,reinterpret_cast<PUCHAR>(&size),sizeof(size),&read,0);
 std::vector<UCHAR> object(size);
 bool ok=BCryptCreateHash(algorithm,&hash,object.data(),size,nullptr,0,0)>=0;
 if(ok)ok=BCryptHashData(hash,static_cast<PUCHAR>(const_cast<void*>(data)),length,0)>=0&&BCryptFinishHash(hash,digest,32,0)>=0;
 if(hash)BCryptDestroyHash(hash);BCryptCloseAlgorithmProvider(algorithm,0);
 if(!ok)return {};char output[65]{};for(int i=0;i<32;i++)sprintf_s(output+i*2,3,"%02X",digest[i]);return output;
}
bool writable(const void* pointer,size_t size){MEMORY_BASIC_INFORMATION info{};return VirtualQuery(pointer,&info,sizeof(info))&&info.State==MEM_COMMIT&&(info.Protect&0xff)==PAGE_READWRITE&&reinterpret_cast<uintptr_t>(pointer)+size<=reinterpret_cast<uintptr_t>(info.BaseAddress)+info.RegionSize;}
void setTexts(bool active,bool restoreAll=false){
 for(const auto& patch:kTexts){
  auto target=reinterpret_cast<char*>(kTextBase+patch.offset);size_t length=strlen(patch.original);
  if(!writable(target,length+1))continue;
  const char* text=restoreAll||patch.conditional&&!active?patch.original:patch.modified;
  memset(target,0,length+1);memcpy(target,text,strlen(text));
 }
}
bool soloCamp(void* self){return *reinterpret_cast<int*>(reinterpret_cast<char*>(self)+0x2b0)==0;}
CampVector* campVector(void* self){return reinterpret_cast<CampVector*>(reinterpret_cast<char*>(self)+0x298);}
bool hasCard(void* self){
 auto v=campVector(self);
 return soloCamp(self)&&v->begin&&v->end-v->begin==16&&v->begin[15].route==92;
}
void selectMode(bool active){setTexts(active);if(modMode.exchange(active)!=active){log(active?"Kinship Studio Camp card opened":"Original game menu selected");report();}}
void __fastcall buildCamp(void* self){
 originalCampBuild(self);
 if(!soloCamp(self))return;
 auto v=campVector(self);
 if(!v->begin||v->end-v->begin!=15){log("Camp row count did not match the expected solo layout; no card added");return;}
 using Resize=void(__fastcall*)(CampVector*,size_t);
 reinterpret_cast<Resize>(base+0x731ba0)(v,16);
 if(!v->begin||v->end-v->begin!=16){log("Native Camp vector did not resize; no card added");return;}
 auto& row=v->begin[15];
 // Icon 26 is the Camp Menu's own gear, including its native selected-state tint.
 row.icon=26;row.route=92;row.enabled=1;
 using MakeString=void(__fastcall*)(void**,const char*);
 reinterpret_cast<MakeString>(base+0x1f0940)(&row.label,"Kinship Studio");
 ++cardBuilds;log("Added Kinship Studio as Camp card 16, page 2 bottom-right");report();
}
int __fastcall campCount(void* self){return hasCard(self)?16:originalCampCount(self);}
int __fastcall campRoute(void* self){
 int result=originalCampRoute(self);
 if(result){
  auto list=reinterpret_cast<char*>(self)+0x538;
  int selected=reinterpret_cast<IntFn>(base+0x743790)(list);
  bool custom=result==92&&selected==15&&hasCard(list);
  if(custom)battlePageReady=true;
  selectMode(custom);
 }
 return result;
}
int __fastcall route(void* self){selectMode(false);return originalRoute(self);}
template<class T>T& field(void* p,size_t offset){return *reinterpret_cast<T*>(static_cast<char*>(p)+offset);}
void* currentController=nullptr;
void* resetRequest=nullptr;
PageFn originalGuiTick=nullptr;
std::map<void*,std::string> textCache,helpCache;
bool readable(const void* p,size_t n){return battle::accessible(reinterpret_cast<uintptr_t>(p),n);}
void* gui(void* self){return field<void*>(self,0x370);}
int feature(unsigned row){return row<studio::count(activeTab)?studio::rows(activeTab)[row].feature:-1;}
void* element(void* root,unsigned id){
 if(!readable(root,0x118)||id>=64)return nullptr;
 unsigned count=reinterpret_cast<unsigned(__fastcall*)(void*)>(base+0x10b7720)(root);
 if(id>=count)return nullptr;
 auto table=field<void**>(root,0x108);
 if(!readable(table,(id+1)*8))return nullptr;
 auto node=table[id];return readable(node,0xf0)?node:nullptr;
}
void* path(void* root,std::initializer_list<int> ids){
 if(!root)return nullptr;
 struct Path{const int* begin;const int* end;const int* capacity;};
 Path p{ids.begin(),ids.end(),ids.end()};
 return reinterpret_cast<void*(__fastcall*)(void*,Path*)>(base+0x4bc360)(root,&p);
}
void rawText(void* node,const char* text){
 if(!readable(node,0x140)||field<uintptr_t>(node,0)!=base+0x17be850)return;
 if(textCache[node]==text)return;
 textCache[node]=text;
 reinterpret_cast<void(__fastcall*)(void*,const char*,size_t)>(base+0x10ae5c0)(node,text,strlen(text));
}
void instanceText(void* root,std::initializer_list<int> ids,const char* text){
 if(textCache[root]==text)return;
 textCache[root]=text;
 struct Path{const int* begin;const int* end;const int* capacity;} p{ids.begin(),ids.end(),ids.end()};
 reinterpret_cast<void(__fastcall*)(void*,Path*,const char*)>(base+0x4e0900)(root,&p,text);
}
void* rowInstance(void* view,unsigned row){
 auto begin=field<void**>(view,0x11e0),end=field<void**>(view,0x11e8);
 if(!begin||end-begin<=row||end-begin>16)return nullptr;
 auto wrapper=begin[row];if(!readable(wrapper,0x68))return nullptr;
 auto instances=field<void**>(wrapper,0x20);
 if(!readable(instances,8))return nullptr;
 auto instance=instances[0];
 if(!readable(instance,0x118))return nullptr;
 auto vt=field<uintptr_t>(instance,0);
 return vt==base+0x17bf1e8||vt==base+0x17beee8?instance:nullptr;
}
void xy(void* node,float x,float y){
 if(!readable(node,0xd8))return;
 if(field<float>(node,0xc0)==x&&field<float>(node,0xc4)==y)return;
 field<float>(node,0xc0)=x;field<float>(node,0xc4)=y;field<unsigned>(node,0x64)|=0x10000;
}
void objectXY(void* node,float x,float y){
 if(!readable(node,0xb0))return;
 if(field<float>(node,0xa0)==x&&field<float>(node,0xa4)==y)return;
 field<float>(node,0xa0)=x;field<float>(node,0xa4)=y;field<unsigned>(node,0x54)|=0x20000;
}
void scale(void* node,float x,float y){
 if(!readable(node,0xd8))return;
 if(field<float>(node,0xd0)==x&&field<float>(node,0xd4)==y)return;
 field<float>(node,0xd0)=x;field<float>(node,0xd4)=y;field<unsigned>(node,0x64)|=0x10000;
}
struct IconTile{void* bitmap=nullptr;alignas(16) unsigned char tile[48]{};};
// Nodes and renderer commands can retain these pointers. Keep storage and the
// DLL alive until process exit, just like the approved Camp-card icon addon.
std::vector<IconTile*> iconTiles;
void icon(void* bitmap,unsigned index,float size){
 if(!readable(bitmap,0x154))return;
 auto vt=field<uintptr_t>(bitmap,0);
 if(vt!=base+0x17be560||!readable(field<void*>(bitmap,0x138),48)||!ui::loadTexture())return;
 IconTile* found=nullptr;
 for(auto* p:iconTiles)if(p->bitmap==bitmap){found=p;break;}
 if(!found){
  auto old=field<void*>(bitmap,0x138);if(!readable(old,48))return;
  found=new IconTile;found->bitmap=bitmap;memcpy(found->tile,old,48);iconTiles.push_back(found);
 }
 short rectangle[]{static_cast<short>(index%4*256),static_cast<short>(index/4*256),static_cast<short>(index%4*256+256),static_cast<short>(index/4*256+256)};
 memcpy(found->tile+8,rectangle,8);float inverse[]{1.f/1024,1.f/1024};memcpy(found->tile+0x20,inverse,8);memcpy(found->tile+0x28,&ui::texture,8);
 field<void*>(bitmap,0x138)=found->tile;
 float uv[]{static_cast<float>(rectangle[0]),static_cast<float>(rectangle[1]),static_cast<float>(rectangle[2]),static_cast<float>(rectangle[3])};
 reinterpret_cast<void(__fastcall*)(void*,const float*)>(base+0x10afbb0)(bitmap,uv);
 float dimensions[]{size,size};
 reinterpret_cast<void(__fastcall*)(void*,const float*)>(base+0x10af660)(bitmap,dimensions);
 ++ui::iconApplications;
}
// The engine separates Visible (bit 0) from Active (bit 3). Options' unused
// tab branch starts inactive. Activate only the owned branch, not the GUI root.
void activateBranch(void* node,unsigned depth=0){
 if(depth>8||!readable(node,0x98))return;
 reinterpret_cast<void(__fastcall*)(void*,bool)>(base+0x10b0d30)(node,true);
 for(auto child=field<void*>(node,0x78);readable(child,0x98);child=field<void*>(child,0x80))activateBranch(child,depth+1);
}
void* rowRoot(void* view,unsigned row){
 if(!readable(view,0x11f8))return nullptr;
 auto begin=field<void**>(view,0x11e0),end=field<void**>(view,0x11e8);
 if(!begin||end-begin<=row||end-begin>16||!readable(begin,(row+1)*8))return nullptr;
 auto wrapper=begin[row];return readable(wrapper,0x68)?field<void*>(wrapper,0x60):nullptr;
}
int __fastcall nativePage(void* self){
 if(!modMode)return originalPage(self);
 // Native click handling compares the clicked tab with the selected tab. All
 // row-building and value paths use bank zero, within the original allocation.
 auto caller=reinterpret_cast<uintptr_t>(_ReturnAddress())-base;
 return caller>=0x6d7af0&&caller<0x6d7b6c?static_cast<int>(activeTab):0;
}
int __fastcall otherCount(void* self,unsigned page){return modMode?(battlePageReady?(buildingRows?studio::Capacity:studio::count(activeTab)):1):originalOtherCount(self,page);}
void* __fastcall rows(void* self){return modMode?(battlePageReady?static_cast<void*>(battleRows):static_cast<void*>(legacyRow)):originalRows(self);}
int __fastcall value(void* self,unsigned id){
 if(!modMode)return originalValue(self,id);++reads;
 unsigned row=id-1001;int f=feature(row);
 return f<0?-1:f>=29?static_cast<int>(studio::selection(f)):studio::selected(activeTab,row,battle::settings[f]);
}
void refresh(void* self){reinterpret_cast<void(__fastcall*)(void*,bool,unsigned)>(base+0x6d7310)(self,true,0);}
int __fastcall choiceCount(void* self,unsigned id){
 if(!modMode)return originalChoiceCount(self,id);
 unsigned row=id-1001;return buildingRows?3:row<studio::count(activeTab)?studio::rows(activeTab)[row].count:0;
}
void choiceLabel(unsigned row,unsigned choice,char* out,size_t n){
 int f=feature(row);if(f<0||choice>=studio::rows(activeTab)[row].count){out[0]=0;return;}
 if(f>=29){studio::label(f,choice,out,n);return;}
 if(catalog::adjustable(f)){
  if(choice!=1){snprintf(out,n,"%s",choice==0?"[ - ]":"[ + ]");return;}
  catalog::label(f,battle::settings[f],out,n);return;
 }
 catalog::label(f,studio::rows(activeTab)[row].values[choice],out,n);
}
void __fastcall choices(void* self,unsigned id,unsigned page){
 if(!modMode){originalChoices(self,id,page);return;}
 unsigned row=id-1001;int f=feature(row);if(f<0)return;
 auto view=gui(self);if(!view)return;
 using Label=void(__fastcall*)(void*,unsigned,unsigned,unsigned,const char*);
 for(unsigned c=0;c<3;++c){char text[40]{};
  if(c<studio::rows(activeTab)[row].count)choiceLabel(row,c,text,sizeof(text));
  reinterpret_cast<Label>(base+0x6d4480)(view,0,row,c,text);
 }
}
void __fastcall labels(void* self){
 if(!modMode){originalLabels(self);return;}
 auto view=gui(self);if(!view)return;
 for(unsigned row=0;row<studio::Capacity;++row)
  reinterpret_cast<void(__fastcall*)(void*,unsigned,unsigned,const char*)>(base+0x6d4a40)(view,0,row,feature(row)>=0?studio::rows(activeTab)[row].title:"");
}
void tint(void* image,unsigned color){if(readable(image,0x154)&&field<uintptr_t>(image,0)==base+0x17be560)reinterpret_cast<void(__fastcall*)(void*,const unsigned*)>(base+0x10aff80)(image,&color);}
void sizeBitmap(void* image,float width,float height){if(readable(image,0x154)&&field<uintptr_t>(image,0)==base+0x17be560){float dimensions[]{width,height};reinterpret_cast<void(__fastcall*)(void*,const float*)>(base+0x10af660)(image,dimensions);}}
struct VertexInk{unsigned colors[4];};
std::map<void*,VertexInk> checkboxColors;
void* inkView=nullptr;
void* groupImage(void* group,unsigned index){
 if(!readable(group,0xf0)||field<uintptr_t>(group,0)!=base+0x17be3a8)return nullptr;
 auto objects=field<void**>(group,0xe8);if(!readable(objects,(index+1)*8))return nullptr;
 auto image=objects[index];return readable(image,0x154)&&field<uintptr_t>(image,0)==base+0x17be560?image:nullptr;
}
void checkboxInk(void* widget,bool numeric){
 if(!readable(widget,0x148))return;
 auto paint=[&](void* image){
  if(!image)return;
  if(numeric){
   if(!checkboxColors.count(image)){VertexInk original;memcpy(original.colors,static_cast<char*>(image)+0xd0,16);checkboxColors[image]=original;}
   tint(image,0);
  }else{auto found=checkboxColors.find(image);if(found!=checkboxColors.end())tint(image,found->second.colors[0]);}
 };
 auto group=field<void*>(widget,0x68);
 for(unsigned i=2;i<=4;++i)paint(groupImage(group,i));
 paint(groupImage(field<void*>(widget,0x138),2));
}
// Reposition the existing native help message, without cloning visual/input
// objects. Its original authored coordinates are captured per GUI lifetime.
void* helpPlacementView=nullptr;
void* helpPlacementNode=nullptr;
float helpOriginalX=0,helpOriginalY=0;
void positionPrayerDescription(void* view){
 auto holder=field<void*>(view,0x130);if(!readable(holder,8))return;
 auto root=field<void*>(holder,0);auto message=path(root,{6});
 if(!readable(message,0xb0)||field<uintptr_t>(message,0)!=base+0x17be850)return;
 if(helpPlacementView!=view||helpPlacementNode!=message){
  helpPlacementView=view;helpPlacementNode=message;
  helpOriginalX=field<float>(message,0xa0);helpOriginalY=field<float>(message,0xa4);
 }
 auto selected=field<unsigned>(currentController,0x5a0);
 bool prayer=feature(selected)==19;
 objectXY(message,helpOriginalX,helpOriginalY-(prayer?175.f:0.f));
}
void* __fastcall guiDestroy(void* self,unsigned flags){
 resetRequest=nullptr;
 if(self==inkView){checkboxColors.clear();inkView=nullptr;}
 if(self==helpPlacementView){helpPlacementView=nullptr;helpPlacementNode=nullptr;}
 return originalGUIDestroy(self,flags);
}
void render(void* self){
 auto view=gui(self);if(!view||!battlePageReady)return;
 auto wrappers=field<void**>(view,0x11e0);
 for(unsigned row=0;row<studio::Capacity;++row){
  auto root=rowRoot(view,row);if(!root)continue;int f=feature(row);
  auto objects=field<void**>(root,0xe8);
  // Native choice widgets remain at their authored positions. Moving only the
  // painted row in v6 broke hit testing; active rows now use their original Y.
  if(readable(objects,16))objectXY(objects[1],0,f<0?1800.f:0.f);
  auto wrapper=wrappers[row];
  if(readable(wrapper,0x450))for(unsigned c=0;c<3;++c){
   auto widget=static_cast<char*>(wrapper)+0x78+c*0x148;
   reinterpret_cast<void(__fastcall*)(void*,bool)>(base+0x6fdbf0)(widget,f>=0&&c<studio::rows(activeTab)[row].count);
   checkboxInk(widget,f>=0&&(catalog::adjustable(f)||studio::textStyle(f)));
  }
  for(unsigned c=0;c<3;++c){
   auto message=readable(objects,12*8)?objects[9+c]:nullptr;char text[40]{};
   if(f>=0&&c<studio::rows(activeTab)[row].count)choiceLabel(row,c,text,sizeof(text));
   rawText(message,text);if(message)objectXY(message,(f>=0&&(catalog::adjustable(f)||studio::textStyle(f))?-90.f:-30.f)+c*330.f,0);
  }
 }
 auto records=field<unsigned char*>(view,0x5d8),end=field<unsigned char*>(view,0x5e0);
 if(records&&end-records==studio::Tabs*0x58&&readable(records,studio::Tabs*0x58)){
  for(unsigned tab=0;tab<studio::Tabs;++tab){
   auto root=field<void*>(records+tab*0x58,0x50);if(!readable(root,0x110))continue;
   activateBranch(field<void*>(root,0x88));
   xy(root,studio::tabX(tab),140.f);scale(root,studio::tabScale,1.f);
   reinterpret_cast<void(__fastcall*)(void*,bool)>(base+0x10b99b0)(root,true);
   instanceText(root,{3,7,2},"");
   // One panel bitmap contains both plate and emblem. Native tab root
   // still owns hover/click/controller selection; no extra input hook.
   tint(path(root,{3,4}),0);tint(path(root,{3,5}),0);
   auto image=path(root,{3,6});if(image){
    icon(image,studio::tabIcon(tab)+(tab==activeTab?8:0),64);
    sizeBitmap(image,400,64);objectXY(image,-200,-32);tint(image,0xffffffffu);
   }
  }
  xy(field<void*>(view,0x388),330,110);xy(field<void*>(view,0x3e0),1670,110);
 }
 positionPrayerDescription(view);
 ++uiFrames;
}
void safeRender(void* self){__try{render(self);}__except(EXCEPTION_EXECUTE_HANDLER){++uiErrors;}}
void updateCategory(void* self,unsigned tab){
 if(tab>=studio::Tabs)return;if(tab!=studio::InventoryTab)studio::page=0;activeTab=tab;field<unsigned>(self,0x5a0)=0;textCache.clear();helpCache.clear();
 labels(self);for(unsigned row=0;row<studio::count(activeTab);++row)choices(self,1001+row,0);
 reinterpret_cast<void(__fastcall*)(void*)>(base+0x6d7900)(self);refresh(self);safeRender(self);report();
}
int __fastcall tabCount(void* self){return modMode?studio::Tabs:originalTabCount(self);}
void __fastcall tabMove(void* self,unsigned input){
 if(!modMode){originalTabMove(self,input);return;}
 originalTabMove(self,input);auto view=gui(self);if(!view)return;
 unsigned tab=field<unsigned>(view,0x608);if(tab<studio::Tabs&&tab!=activeTab)updateCategory(self,tab);
}
void __fastcall tabSelect(void* self,unsigned tab){
 if(!modMode){originalTabSelect(self,tab);return;}
 originalTabSelect(self,tab);updateCategory(self,tab);
}
void __fastcall buildRows(void* self){
 if(!modMode){originalBuildRows(self);return;}
 resetRequest=nullptr;currentController=self;activeTab=0;studio::page=0;textCache.clear();helpCache.clear();buildingRows=true;originalBuildRows(self);buildingRows=false;
 auto view=gui(self);if(!view)return;if(inkView!=view){checkboxColors.clear();inkView=view;}
 int ids[studio::Tabs]{};struct Vector{int* begin;int* end;int* capacity;} names{ids,ids+studio::Tabs,ids+studio::Tabs};
 reinterpret_cast<void(__fastcall*)(void*,Vector*,void*)>(base+0x6d58d0)(view,&names,static_cast<char*>(self)+0x80);
 reinterpret_cast<void(__fastcall*)(void*,unsigned)>(base+0x703a70)(static_cast<char*>(view)+0x328,0);
 log("Eight categories and six shared native control rows created");
}
void openResetConfirmation(void* self){
 // Called after the GUI tick has finished native input; the game owns the modal.
 if(resetRequest!=self)return;
 resetRequest=nullptr;
 if(!modMode||!battlePageReady||!battle::compatible||currentController!=self||!readable(self,0x738)||field<uintptr_t>(self,0)!=base+0x1662398||field<unsigned>(self,0x6f8)!=0)return;
 bool handled=false;
 reinterpret_cast<void(__fastcall*)(void*,unsigned,bool*)>(base+0x6d8190)(self,9,&handled);
 log(handled?"Native reset confirmation opened; waiting for Yes or No":"Native reset confirmation unavailable; settings retained");
}
void __fastcall guiTick(void* self){
 originalGuiTick(self);auto controller=currentController;
 if(!modMode||!battlePageReady||!readable(controller,0x378)||field<uintptr_t>(controller,0)!=base+0x1662398||gui(controller)!=self)return;
 unsigned tab=field<unsigned>(self,0x608);if(tab<studio::Tabs&&tab!=activeTab)updateCategory(controller,tab);
 studio::poll();
 unsigned selected=field<unsigned>(controller,0x5a0);int f=feature(selected);
 char help[640]{};
 const char* description=f>=29?studio::help(f):f==19?catalog::prayerHelp(battle::settings[19]):f>=0?options[f].help:"";
 if(f==19)snprintf(help,sizeof(help),"%s%s",description,battle::settings[19]?"\nEffect strength depends on your Prayer Pot level.":"");
 else snprintf(help,sizeof(help),"%s   |   Q / E: category   |   Minus / Plus: adjust; middle: current value\n%s",studio::tabName(activeTab),description);
 if(f>=26&&f<=28){unsigned state=f==27?battle::denTableStatus.load():battle::eggTableStatus.load();if(state==1||state==3){size_t used=strlen(help);snprintf(help+used,sizeof(help)-used,"\n%s",state==1?"Waiting for the game tables.":"Conflicting table values: this control has not been applied.");}}
 if(helpCache[self]!=help){helpCache[self]=help;reinterpret_cast<void(__fastcall*)(void*,const char*)>(base+0x6d4980)(self,help);}safeRender(controller);openResetConfirmation(controller);
}
void __fastcall frame(void* self){originalFrame(self);}
void __fastcall set(void* self,unsigned id,unsigned choice){
 if(!modMode){originalSetter(self,id,choice);return;}
 unsigned row=id-1001;int f=feature(row);
 if(!battlePageReady||!battle::compatible||f<0||choice>=studio::rows(activeTab)[row].count)return;
 // Feature40 only requests a dialog. Native result0 alone invokes reset slot79.
 if(f==40){if(choice==0&&currentController==self){resetRequest=self;++setters;log("Reset action received; confirmation queued");}return;}
 if(f>=29){auto previousPage=studio::page;studio::activate(f,choice);++setters;textCache.clear();helpCache.clear();updateCategory(self,activeTab);if(previousPage==studio::page&&row<studio::count(activeTab)){field<unsigned>(self,0x5a0)=row;refresh(self);safeRender(self);}return;}
 battle::settings[f]=studio::applyChoice(activeTab,row,battle::settings[f],choice);++setters;studio::persist();
 textCache.clear();labels(self);choices(self,id,0);
 refresh(self);safeRender(self);report();
}
void __fastcall reset(void* self){
 if(!modMode){originalReset(self);return;}
 // The native modal invokes this callback only after an affirmative result.
 resetRequest=nullptr;studio::resetAll();log("Native reset confirmed; all gameplay settings reset");textCache.clear();helpCache.clear();
 for(unsigned row=0;row<studio::count(activeTab);++row)choices(self,1001+row,0);
 refresh(self);safeRender(self);report();
}

#include "economy_hooks.inc"

bool hook(size_t table,unsigned index,size_t expected,void* replacement,void** original){
 auto slot=reinterpret_cast<void**>(base+table)+index;
 if(*slot!=reinterpret_cast<void*>(base+expected))return false;
 DWORD previous;if(!VirtualProtect(slot,sizeof(void*),PAGE_READWRITE,&previous))return false;
 *original=*slot;InterlockedExchangePointer(reinterpret_cast<void* volatile*>(slot),replacement);
 DWORD ignored;VirtualProtect(slot,sizeof(void*),previous,&ignored);hooks.push_back({slot,*original,replacement});return true;
}
void restore(){
 economyHooksEnabled=false;
 for(auto i=hooks.rbegin();i!=hooks.rend();++i){DWORD previous;if(VirtualProtect(i->slot,sizeof(void*),PAGE_READWRITE,&previous)){InterlockedCompareExchangePointer(reinterpret_cast<void* volatile*>(i->slot),i->original,i->replacement);DWORD ignored;VirtualProtect(i->slot,sizeof(void*),previous,&ignored);}}
 battle::stop();setTexts(false,true);installed=false;report();log("All native callbacks and text restored; no disk game files modified");
}
DWORD WINAPI run(void*){
 wchar_t module[MAX_PATH];GetModuleFileNameW(reinterpret_cast<HMODULE>(&__ImageBase),module,MAX_PATH);
 std::wstring directory(module);directory.resize(directory.find_last_of(L"\\/"));logPath=directory+L"\\battle-runtime.log";statePath=directory+L"\\battle-state.json";stopPath=directory+L"\\battle.stop";
 log("Native Battle options starting (session-only test)");base=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
 if(!runtimeguard::session(directory,kTextBase)||!writable(reinterpret_cast<void*>(kTextBase),kTextBytes)||sha256(reinterpret_cast<void*>(kTextBase),kTextBytes)!=kTextHash){log("Process or message-data verification failed; no changes");return 1;}
 ui::base=base;ui::directory=directory;
 battle::image=base;
 if(!verifyBattleProofs(base)){log("Battle code verification failed; no changes");return 3;}
 battle::compatible=true;
 // The current build is verified against thirteen native callback addresses. These
 // are data-pointer replacements only; no executable instructions are patched.
 bool ok=hook(0x166b7e8,10,0x731b30,reinterpret_cast<void*>(buildCamp),reinterpret_cast<void**>(&originalCampBuild))
 &&hook(0x166b7e8,17,0x7319e0,reinterpret_cast<void*>(campCount),reinterpret_cast<void**>(&originalCampCount))
 &&hook(0x1627910,74,0x53d190,reinterpret_cast<void*>(campRoute),reinterpret_cast<void**>(&originalCampRoute))
 &&hook(0x1661fa8,74,0x698f50,reinterpret_cast<void*>(route),reinterpret_cast<void**>(&originalRoute))
 &&hook(0x1662398,100,0x2791d0,reinterpret_cast<void*>(otherCount),reinterpret_cast<void**>(&originalOtherCount))
 &&hook(0x1662398,99,0x6dba20,reinterpret_cast<void*>(rows),reinterpret_cast<void**>(&originalRows))
 &&hook(0x1662398,87,0x6dba30,reinterpret_cast<void*>(value),reinterpret_cast<void**>(&originalValue))
 &&hook(0x1662398,88,0x6dbc20,reinterpret_cast<void*>(set),reinterpret_cast<void**>(&originalSetter))
 &&hook(0x1662398,79,0x6db980,reinterpret_cast<void*>(reset),reinterpret_cast<void**>(&originalReset))
 &&hook(0x1662398,84,0x6db9c0,reinterpret_cast<void*>(choiceCount),reinterpret_cast<void**>(&originalChoiceCount))
 &&hook(0x1662398,102,0x6d90f0,reinterpret_cast<void*>(labels),reinterpret_cast<void**>(&originalLabels))
 &&hook(0x1662398,104,0x6dbe10,reinterpret_cast<void*>(choices),reinterpret_cast<void**>(&originalChoices))
 &&hook(0x1662398,80,0x1efaa0,reinterpret_cast<void*>(nativePage),reinterpret_cast<void**>(&originalPage))
 &&hook(0x1662398,101,0x6d8fa0,reinterpret_cast<void*>(buildRows),reinterpret_cast<void**>(&originalBuildRows))
 &&hook(0x1662398,108,0x6d86f0,reinterpret_cast<void*>(tabMove),reinterpret_cast<void**>(&originalTabMove))
 &&hook(0x1662398,109,0x6d5e40,reinterpret_cast<void*>(tabSelect),reinterpret_cast<void**>(&originalTabSelect))
 &&hook(0x1662398,110,0x2b0400,reinterpret_cast<void*>(tabCount),reinterpret_cast<void**>(&originalTabCount))
 &&hook(0x165f8f8,0,0x6d0160,reinterpret_cast<void*>(guiDestroy),reinterpret_cast<void**>(&originalGUIDestroy))
 &&hook(0x165f8f8,9,0x6cb420,reinterpret_cast<void*>(guiTick),reinterpret_cast<void**>(&originalGuiTick))
 &&hook(0x1662398,50,0x6d6410,reinterpret_cast<void*>(frame),reinterpret_cast<void**>(&originalFrame))
 &&hook(0x1625e50,51,0x52d7f0,reinterpret_cast<void*>(craftCommit),reinterpret_cast<void**>(&originalCraftCommit))
 &&hook(0x167bf30,79,0x7f2ed0,reinterpret_cast<void*>(forgeCommit),reinterpret_cast<void**>(&originalForgeCommit))
 &&hook(0x161bf10,51,0x7e23c0,reinterpret_cast<void*>(battleRewardCommit),reinterpret_cast<void**>(&originalBattleRewardCommit))
 &&hook(0x16528b0,51,0x7e23c0,reinterpret_cast<void*>(questRewardCommit),reinterpret_cast<void**>(&originalQuestRewardCommit))
 &&hook(0x167ef20,51,0x803a10,reinterpret_cast<void*>(expeditionRewardCommit),reinterpret_cast<void**>(&originalExpeditionRewardCommit));
 if(!ok){log("Callback verification failed; reverting partial installation");restore();return 2;}
 // A one-use, PID-bound handoff retains the user's values across this UI-only replacement.
 // First install starts Off / Normal; validated saved settings override these defaults.
 studio::initialize(directory);
 FILE* resume=nullptr;_wfopen_s(&resume,(directory+L"\\resume-settings.txt").c_str(),L"r");
 if(resume){unsigned pid=0,values[battle::Count]{};bool valid=fscanf_s(resume,"%u",&pid)==1&&pid==GetCurrentProcessId();
  for(unsigned f=0;f<battle::Count;++f)valid=(fscanf_s(resume,"%u",&values[f])==1&&catalog::valid(f,values[f]))&&valid;
  fclose(resume);if(valid){for(unsigned f=0;f<battle::Count;++f)battle::settings[f]=values[f];MoveFileW((directory+L"\\resume-settings.txt").c_str(),(directory+L"\\resume-settings.applied.txt").c_str());}
 }
 economyHooksEnabled=true;setTexts(true);installed=true;report();log("Camp callbacks installed; close and reopen the Camp Menu to create the new card");
 unsigned ticks=0;
 while(GetFileAttributesW(stopPath.c_str())==INVALID_FILE_ATTRIBUTES){
  if(++ticks%20==0&&!verifyBattleProofs(base)){battle::compatible=false;battle::stop();log("Game routines changed; Battle controls stopped");}
  auto tickStart=GetTickCount64();battle::tick();battle::tickDurationMs=static_cast<unsigned>(GetTickCount64()-tickStart);battle::longestTickMs=std::max(battle::longestTickMs.load(),battle::tickDurationMs.load());if(ticks%10==0)report();Sleep(100);
 }
 restore();return 0;
}
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH){DisableThreadLibraryCalls(instance);HANDLE worker=CreateThread(nullptr,0,run,nullptr,0,nullptr);if(worker)CloseHandle(worker);}return TRUE;}
