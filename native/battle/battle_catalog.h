#pragma once
#include <cstdio>
#include <algorithm>
namespace catalog {
constexpr unsigned Tabs=7, Capacity=5;
constexpr const char* tabNames[]{"Survival","Power","Movement","Exploration","Rewards","Inventory","Egg Hunt"};
constexpr unsigned tabIcons[]{0,1,3,4,6,5,2};
struct Row {int feature;unsigned count;unsigned values[3];const char* title;};
// Category order is independent from stable gameplay setting IDs.
constexpr Row rows[Tabs][Capacity]={
 {{0,2,{0,1},"Rider health"},{1,2,{0,1},"Monstie health"},{3,2,{0,1},"Party hearts"},{9,3,{},"Damage taken"},{-1,0,{},""}},
 {{4,3,{},"Attack multiplier"},{2,2,{0,1},"Full Kinship"},{7,2,{0,1},"Kinship attack level"},{8,3,{},"Battle speed"},{5,2,{0,1},"Turn counter"}},
 {{10,3,{},"Walking & running"},{11,3,{},"Mounted speed"},{12,3,{},"Flying speed"},{13,3,{},"Swimming speed"},{14,3,{},"Egg carrying speed"}},
 {{15,2,{0,1},"Map radar"},{16,2,{0,1},"4x gathering range"},{17,2,{0,1},"Faster gathering"},{18,2,{0,1},"100% retreat"},{19,3,{},"Prayer Pot effect"}},
 {{6,3,{},"Battle EXP"},{21,3,{},"Gathering multiplier"},{22,3,{},"Battle loot multiplier"},{23,3,{},"Chest item multiplier"},{24,3,{},"Zenny reward multiplier"}},
 {{20,2,{0,1},"Preserve battle items"},{25,2,{0,1},"Preserve crafting materials"},{-1,0,{},""},{-1,0,{},""},{-1,0,{},""}},
 {{26,3,{},"Egg quality"},{27,3,{},"Den rarity"},{28,2,{0,1},"Prevent empty pulls"},{-1,0,{},""},{-1,0,{},""}}
};
constexpr unsigned counts[]{4,5,5,5,5,2,3};
inline bool adjustable(int f){return f==26||f==27||f==4||f==6||f==8||f==9||f==19||(f>=21&&f<=24)||(f>=10&&f<15);}
inline int selection(unsigned tab,unsigned row,unsigned value){
 if(tab>=Tabs||row>=counts[tab])return -1;
 const auto& r=rows[tab][row];
 if(adjustable(r.feature))return 1;
 for(unsigned c=0;c<r.count;++c)if(r.values[c]==value)return static_cast<int>(c);
 return -1;
}
// Setting zero always means the unmodified game. Values are quarter steps for
// multipliers and five-percent steps for damage protection.
constexpr unsigned maxima[]{1,1,1,1,36,1,76,1,8,20,8,8,8,8,8,1,1,1,1,8,1,3,3,3,3,1,4,5,1};
inline bool valid(unsigned f,unsigned v){return f<29&&v<=maxima[f];}
inline unsigned step(unsigned f,unsigned v,int direction){
 if(f>=29)return 0;
 if(maxima[f]==1)return v?0:1;
 return static_cast<unsigned>(std::clamp(static_cast<int>(v)+direction,0,static_cast<int>(maxima[f])));
}
inline unsigned numericValue(unsigned f,unsigned setting){return f==9?20-std::min(setting,20u):std::min(setting,maxima[f]);}
inline unsigned numericSetting(unsigned f,unsigned value){value=std::min(value,maxima[f]);return f==9?20-value:value;}
inline unsigned numericChoice(unsigned f,unsigned setting,unsigned choice){
 if(!adjustable(f)||choice>2)return setting;
 unsigned displayed=numericValue(f,setting);
 if(choice==0&&displayed>0)--displayed;
 if(choice==2&&displayed<maxima[f])++displayed;
 return numericSetting(f,displayed);
}
inline unsigned applyChoice(unsigned tab,unsigned row,unsigned current,unsigned choice){
 if(tab>=Tabs||row>=counts[tab])return current;
 const auto& r=rows[tab][row];if(choice>=r.count)return current;
 if(adjustable(r.feature))return numericChoice(r.feature,current,choice);
 return r.values[choice];
}
inline double attack(unsigned v){return 1.0+std::min(v,36u)*.25;}
inline double exp(unsigned v){return 1.0+std::min(v,76u)*.25;}
inline float speed(unsigned v){return 1.f+std::min(v,8u)*.25f;}
inline float protection(unsigned v){return std::min(v,20u)*5.f;}
inline const char* prayerHelp(unsigned effect){
 constexpr const char* descriptions[]{
  "Off: stop maintaining a prayer and restore your previous Prayer Pot effect.",
  "Training: increases EXP earned from battles, helping your party level up faster.",
  "Gathering: improves your chance of finding rare items at gathering points.",
  "Finding: improves your chance of finding eggs with rare genes. Does not guarantee a Rainbow Gene.",
  "Lucky: adds battle-result bonus points, helping you earn better battle rewards.",
  "Kinship: increases how quickly the Kinship Gauge fills during battle.",
  "Adamant: reduces damage received from enemy attacks in battle.",
  "Might: increases the damage your party deals to enemies in battle.",
  "Recovery: restores some HP each turn during battle."
 };
 return descriptions[std::min(effect,8u)];
}
inline void label(unsigned f,unsigned v,char* out,size_t length){
 if(f==4||f==6||f==8||(f>=10&&f<15)){double n=f==4?attack(v):f==6?exp(v):speed(v);snprintf(out,length,"%.2fx",n);}
 else if(f>=21&&f<=24){constexpr unsigned m[]{1,2,5,10};snprintf(out,length,"%ux",m[std::min(v,3u)]);}
 else if(f==26){constexpr const char* names[]{"Normal","Lucky","Very lucky","Best available","Rainbow"};snprintf(out,length,"%s",names[std::min(v,4u)]);}
 else if(f==27){constexpr const char* names[]{"Normal","Rare boost","Strong boost","Super-rare boost","All rare","Best unlocked"};snprintf(out,length,"%s",names[std::min(v,5u)]);}
 else if(f==19){constexpr const char* effects[]{"Off","Training","Gathering","Finding","Lucky","Kinship","Adamant","Might","Recovery"};snprintf(out,length,"%s",effects[std::min(v,8u)]);}
 else if(f==9)snprintf(out,length,"%u%%",100-std::min(v,20u)*5);
 else if(f==5)snprintf(out,length,"%s",v?"Turn 1":"Normal");
 else if(f==7)snprintf(out,length,"%s",v?"Level 3":"Normal");
 else snprintf(out,length,"%s",v?"On":"Off");
}
}
