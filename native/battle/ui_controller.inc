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
