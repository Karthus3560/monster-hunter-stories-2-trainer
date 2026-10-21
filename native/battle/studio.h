#pragma once
#include <sstream>
namespace studio {
// Main is a presentation-only shortcut page. Gameplay IDs and persisted files
// remain unchanged; all quick controls share their original settings.
inline constexpr unsigned Tabs=catalog::Tabs+1,Capacity=6,InventoryTab=6;
inline constexpr catalog::Row mainRows[]{
 {0,2,{0,1},"Rider health"},{1,2,{0,1},"Monstie health"},
 {2,2,{0,1},"Full Kinship"},{8,3,{},"Battle speed"},
 {15,2,{0,1},"Map radar"},{40,1,{},"Reset all options"}
};
inline const char* tabName(unsigned tab){return tab==0?"Main":tab<Tabs?catalog::tabNames[tab-1]:"";}
inline unsigned tabIcon(unsigned tab){return tab==0?7:tab<Tabs?catalog::tabIcons[tab-1]:0;}
inline constexpr float tabX(unsigned tab){return 458.f+148.f*tab;}
inline constexpr float tabScale=.35f;
inline std::wstring data,directory;
inline unsigned page=0,profile=0,backup=0;
inline bool autoload=true;
inline std::string notice="Settings are saved automatically.";
struct Backup{std::string id,date,summary;};
inline std::vector<Backup> backups;
inline unsigned long long requestId=0,lastPoll=0,armedUntil=0;
inline std::string armedId;
inline bool prefsRecovered=false;
inline bool atomic(const std::wstring& path,const void* bytes,unsigned size){
 auto temp=path+L".native-tmp";HANDLE file=CreateFileW(temp.c_str(),GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);if(file==INVALID_HANDLE_VALUE)return false;
 DWORD written=0;bool ok=WriteFile(file,bytes,size,&written,nullptr)&&written==size&&FlushFileBuffers(file);CloseHandle(file);
 return ok&&MoveFileExW(temp.c_str(),path.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH);
}
inline bool prefsRead(const std::wstring& path,std::array<unsigned,29>& values){
 FILE* file=nullptr;_wfopen_s(&file,path.c_str(),L"rb");if(!file)return false;unsigned char bytes[161]{};auto count=fread(bytes,1,sizeof(bytes),file);fclose(file);unsigned header[3]{};memcpy(header,bytes,12);unsigned char hash[32]{};
 if(count!=160||header[0]!=0x314e534b||header[1]!=1||header[2]!=29||!runtimeguard::digest(bytes,128,hash)||memcmp(hash,bytes+128,32))return false;
 memcpy(values.data(),bytes+12,116);for(unsigned f=0;f<29;++f)if(!catalog::valid(f,values[f]))return false;return true;
}
inline bool prefsWrite(const std::wstring& path){
 unsigned char bytes[160]{};unsigned header[]{0x314e534b,1,29};memcpy(bytes,header,12);
 for(unsigned f=0;f<29;++f){unsigned value=battle::settings[f];if(!catalog::valid(f,value))return false;memcpy(bytes+12+f*4,&value,4);}
 if(!runtimeguard::digest(bytes,128,bytes+128))return false;
 std::array<unsigned,29> previous{};if(prefsRead(path,previous))CopyFileW(path.c_str(),(path+L".previous").c_str(),FALSE);
 return atomic(path,bytes,160);
}
inline bool persist(){bool ok=prefsWrite(data+L"\\last.bin");if(!ok)notice="Settings could not be saved. Check the native data folder.";return ok;}
inline void resetAll(){
 for(auto& value:battle::settings)value=0;
 if(persist())notice="All 29 gameplay options reset to Off / Normal. Profiles and backups kept.";
}
inline std::wstring profilePath(){return data+L"\\profiles\\slot"+std::to_wstring(profile+1)+L".bin";}
inline void restorePreferences(){
 if(!autoload){notice="Saved settings are kept. Options start at Off / Normal.";return;}
 std::array<unsigned,29> values{};bool ok=prefsRead(data+L"\\last.bin",values);if(!ok){ok=prefsRead(data+L"\\last.bin.previous",values);prefsRecovered=ok;}
 if(ok){for(unsigned f=0;f<29;++f)battle::settings[f]=values[f];if(prefsRecovered)notice="Recovered the previous valid settings file.";}
 else notice="No valid saved settings were found. Current defaults are active.";
}
inline void initialize(const std::wstring& session){
 directory=session;wchar_t appdata[MAX_PATH]{};GetEnvironmentVariableW(L"LOCALAPPDATA",appdata,MAX_PATH);data=std::wstring(appdata)+L"\\KinshipStudioNative\\data";
 CreateDirectoryW(data.c_str(),nullptr);CreateDirectoryW((data+L"\\profiles").c_str(),nullptr);
 FILE* file=nullptr;_wfopen_s(&file,(data+L"\\autoload.enabled").c_str(),L"r");if(file){autoload=fgetc(file)=='1';fclose(file);}
 restorePreferences();
}
inline const catalog::Row home[]{ {20,2,{0,1},"Preserve battle items"},{25,2,{0,1},"Preserve crafting materials"},{29,2,{0,1},"Restore last settings"},{30,1,{},"Settings profiles"},{31,1,{},"Save backups"} };
inline const catalog::Row profiles[]{ {32,3,{},"Selected profile"},{33,3,{},"Profile actions"} };
inline const catalog::Row backupRows[]{ {35,3,{},"Selected backup"},{36,1,{},"Create a save backup"},{37,1,{},"Verify selected backup"},{38,3,{},"Restore after exiting game"},{39,1,{},"Return to Inventory"} };
inline const catalog::Row* rows(unsigned tab){return tab==0?mainRows:tab==InventoryTab?(page?(page==1?profiles:backupRows):home):tab<Tabs?catalog::rows[tab-1]:nullptr;}
inline unsigned count(unsigned tab){return tab==0?6u:tab==InventoryTab?(page==1?2u:5u):tab<Tabs?catalog::counts[tab-1]:0;}
inline bool textStyle(int f){return f>=30;}
inline bool action(int f){return f==30||f==31||f==33||f==36||f==37||f==38||f==39||f==40;}
inline int selection(int f){return action(f)?-1:f==29?(autoload?1:0):1;}
inline int selected(unsigned tab,unsigned row,unsigned current){
 if(row>=count(tab))return -1;const auto& r=rows(tab)[row];
 if(r.feature>=29)return static_cast<int>(selection(r.feature));
 if(catalog::adjustable(r.feature))return 1;
 for(unsigned c=0;c<r.count;++c)if(r.values[c]==current)return static_cast<int>(c);return -1;
}
inline unsigned applyChoice(unsigned tab,unsigned row,unsigned current,unsigned choice){
 if(row>=count(tab))return current;const auto& r=rows(tab)[row];
 if(r.feature<0||r.feature>=29||choice>=r.count)return current;
 return catalog::adjustable(r.feature)?catalog::numericChoice(r.feature,current,choice):r.values[choice];
}
inline void poll(){
 auto now=GetTickCount64();if(now-lastPoll<500)return;lastPoll=now;
 std::string selected=backup<backups.size()?backups[backup].id:"";std::vector<Backup> next;
 FILE* file=nullptr;_wfopen_s(&file,(data+L"\\backup-index.tsv").c_str(),L"rb");if(file){char line[512];while(fgets(line,sizeof(line),file)&&next.size()<100){std::string s=line;auto first=s.find('\t'),second=s.find('\t',first==std::string::npos?0:first+1);if(first==std::string::npos||second==std::string::npos)continue;Backup b{s.substr(0,first),s.substr(first+1,second-first-1),s.substr(second+1)};if(b.id.size()!=24||b.id.find_first_not_of("0123456789Tabcdef-")!=std::string::npos)continue;while(!b.summary.empty()&&(b.summary.back()=='\n'||b.summary.back()=='\r'))b.summary.pop_back();next.push_back(b);}fclose(file);}
 if(!selected.empty())for(unsigned i=0;i<next.size();++i)if(next[i].id==selected){backup=i;break;}backups=std::move(next);if(backup>=backups.size())backup=0;
 _wfopen_s(&file,(directory+L"\\response.tsv").c_str(),L"rb");if(file){char buffer[512]{};fread(buffer,1,511,file);fclose(file);std::string response=buffer;auto first=response.find('\t'),second=response.find('\t',first==std::string::npos?0:first+1);if(first!=std::string::npos&&second!=std::string::npos&&strtoull(response.substr(0,first).c_str(),nullptr,10)==requestId)notice=response.substr(second+1);}
}
inline void label(int f,unsigned choice,char* out,size_t size){
 if(f==29){snprintf(out,size,"%s",choice?"On":"Off");return;}
 if(f==32||f==35){if(choice!=1){snprintf(out,size,"%s",choice?"[ + ]":"[ - ]");return;}
  if(f==32){std::array<unsigned,29> values{};snprintf(out,size,"Slot %u%s",profile+1,prefsRead(profilePath(),values)?"":" (empty)");}
  else snprintf(out,size,"%s",backups.empty()?"No backups":backups[backup].date.c_str());return;
 }
 const char* text=f==40?"Reset all":f==30?"Open profiles":f==31?"Open backups":f==33?(choice==0?"Load":choice==1?"Save":"Back"):f==36?"Create":f==37?"Verify":f==38?(choice==0?"Arm":choice==1?"Confirm":"Cancel"):"Back";
 snprintf(out,size,"%s",text);
}
inline void request(unsigned action){
 if(GetFileAttributesW((directory+L"\\request.txt").c_str())!=INVALID_FILE_ATTRIBUTES){notice="Please wait for the previous backup action.";return;}
 if(action!=1&&action!=4&&backups.empty()){notice="There is no backup selected.";return;}
 requestId=GetTickCount64();char message[256]{};snprintf(message,sizeof(message),"KSNREQUEST1 %u %llu %llu %u %s -",GetCurrentProcessId(),runtimeguard::creation,requestId,action,(action==1||action==4)?"-":backups[backup].id.c_str());
 if(atomic(directory+L"\\request.txt",message,static_cast<unsigned>(strlen(message))))notice="Working... please wait.";else notice="Could not send the backup request.";
}
inline void activate(int f,unsigned choice){
 if(f==40)return; // UI requests the native modal; only its confirmed callback calls resetAll().
 if(f==29){autoload=choice!=0;char value=autoload?'1':'0';if(!atomic(data+L"\\autoload.enabled",&value,1))notice="Startup preference could not be saved.";else notice=autoload?"Your saved options will be restored next time.":"Next launch starts at Off / Normal. Saved profiles are kept.";}
 else if(f==30||f==31){page=f==30?1:2;lastPoll=0;poll();}
 else if(f==32){if(choice==0&&profile)--profile;if(choice==2&&profile<4)++profile;}
 else if(f==33){if(choice==2){page=0;return;}if(choice==1){notice=prefsWrite(profilePath())?"Profile saved.":"Profile could not be saved.";}else{std::array<unsigned,29> values{};if(!prefsRead(profilePath(),values)){notice="Profile is empty or failed verification. Current settings kept.";return;}for(unsigned i=0;i<29;++i)battle::settings[i]=values[i];if(persist())notice="Profile loaded and saved as your current settings.";}}
 else if(f==35){if(choice==0&&backup)--backup;if(choice==2&&backup+1<backups.size())++backup;armedUntil=0;}
 else if(f==36)request(1);
 else if(f==37)request(2);
 else if(f==38){if(choice==2){armedUntil=0;request(4);}else if(backups.empty())notice="Create a backup first.";else if(choice==0){armedId=backups[backup].id;armedUntil=GetTickCount64()+15000;notice="Armed for 15 seconds. Confirm to restore this backup AFTER exiting the game.";}else if(GetTickCount64()<armedUntil&&armedId==backups[backup].id){armedUntil=0;request(3);}else notice="Select Arm first, then Confirm within 15 seconds.";}
 else if(f==39){page=0;armedUntil=0;}
}
inline const char* help(int f){
 if(f==40){static std::string resetHelp;resetHelp="Open confirmation to reset all 29 gameplay controls to Off / Normal. Choose No to cancel. Profiles and backups are kept.\n"+notice;return resetHelp.c_str();}
 static std::string description;description=f==29?"Restore your saved options when the game starts. Off starts at Off / Normal; Studio stays available in Camp.":f==30?"Save or load five profiles. Your last-used settings are saved automatically.":f==31?"Browse verified save backups, create one, or queue a restore for after the game exits.":f==32?"Choose one of five profile slots. Profiles contain the 29 existing gameplay controls.":f==33?"Load the selected profile, save your current settings into it, or return.":f==35?(backups.empty()?"No save backups yet.":backups[backup].date+" | "+backups[backup].summary):f==36?"Copy and verify current save files. Wait for the game to finish saving first.":f==37?"Check every selected backup file against its recorded hash.":f==38?"Arm, then Confirm: restore the selected backup only AFTER the game closes. Current saves get a safety backup. Cancel removes a queued restore.":"Return to the Inventory controls.";
 description+="\n"+notice;return description.c_str();
}
}
