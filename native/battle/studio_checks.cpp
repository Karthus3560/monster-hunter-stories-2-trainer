#include <Windows.h>
#include "battle_engine.h"
#include "runtime_guard.h"
#include "studio.h"
#include <stdexcept>
int main(int argc,char** argv){
 if(argc==2){std::string input=argv[1];std::wstring file(input.begin(),input.end());std::array<unsigned,29> values{};bool valid=studio::prefsRead(file,values);printf("Preference verification: %s\n",valid?"valid":"invalid");return valid?0:2;}
 unsigned checks=0;auto check=[&](bool good,const char* name){if(!good)throw std::runtime_error(name);++checks;printf("PASS: %s\n",name);};
 wchar_t temp[MAX_PATH]{};GetTempPathW(MAX_PATH,temp);auto root=std::wstring(temp)+L"KinshipStudioChecks-"+std::to_wstring(GetTickCount64());CreateDirectoryW(root.c_str(),nullptr);studio::data=root;studio::directory=root;CreateDirectoryW((root+L"\\profiles").c_str(),nullptr);
 for(unsigned f=0;f<29;++f)battle::settings[f]=catalog::maxima[f];check(studio::persist(),"native settings persist atomically");
 std::array<unsigned,29> values{};check(studio::prefsRead(root+L"\\last.bin",values)&&values[4]==36&&values[27]==5,"native saved settings verify and round-trip");
 battle::settings[4]=0;studio::persist();check(studio::prefsRead(root+L"\\last.bin.previous",values)&&values[4]==36,"previous valid native settings retained");
 studio::activate(30,0);check(studio::page==1&&studio::count(studio::InventoryTab)==2,"profile screen opens within existing tab");
 studio::activate(32,2);check(studio::profile==1,"native profile stepper selects second slot");studio::activate(33,1);battle::settings[4]=9;studio::activate(33,0);check(battle::settings[4]==0,"profile load restores saved choices instead of current values");
 studio::profile=4;battle::settings[4]=7;studio::activate(33,0);check(battle::settings[4]==7,"empty profile leaves current settings unchanged");
 studio::activate(33,2);check(studio::page==0&&studio::count(studio::InventoryTab)==5,"back returns five inventory and convenience rows");
 studio::activate(29,0);check(!studio::autoload,"automatic loading can be disabled without stopping current controls");studio::activate(29,1);check(studio::autoload,"automatic loading preference can be enabled");
 studio::activate(31,0);check(studio::page==2&&studio::count(studio::InventoryTab)==5,"backup screen opens five native rows");
 studio::backups={{"20261021T010000-1234abcd","21 Oct 04:00","5 saves"}};studio::backup=0;studio::activate(38,1);check(GetFileAttributesW((root+L"\\request.txt").c_str())==INVALID_FILE_ATTRIBUTES,"restore confirmation alone cannot queue a restore");
 studio::activate(38,0);studio::activate(38,1);check(GetFileAttributesW((root+L"\\request.txt").c_str())!=INVALID_FILE_ATTRIBUTES,"armed explicit confirmation creates restore-after-exit request");
 DeleteFileW((root+L"\\request.txt").c_str());studio::activate(38,0);studio::armedUntil=0;studio::activate(38,1);check(GetFileAttributesW((root+L"\\request.txt").c_str())==INVALID_FILE_ATTRIBUTES,"expired restore confirmation is rejected");
 studio::activate(38,0);studio::backups[0].id="20261021T020000-1234abcd";studio::activate(38,1);check(GetFileAttributesW((root+L"\\request.txt").c_str())==INVALID_FILE_ATTRIBUTES,"changing selected backup invalidates armed restore");
 studio::activate(39,0);check(studio::page==0,"backup Back returns to Inventory");
 bool labels=true;for(unsigned page=0;page<3;++page){studio::page=page;for(unsigned row=0;row<studio::count(studio::InventoryTab);++row){auto r=studio::rows(studio::InventoryTab)[row];if(r.feature<29)continue;labels&=studio::action(r.feature)?studio::selection(r.feature)==-1:studio::selection(r.feature)>=0&&static_cast<unsigned>(studio::selection(r.feature))<r.count;for(unsigned c=0;c<r.count;++c){char label[40]{};studio::label(r.feature,c,label,40);labels&=label[0]!=0;}}}check(labels,"all auxiliary native rows have valid selections and button labels");
 studio::page=0;check(studio::Tabs==8&&studio::count(0)==6&&studio::Capacity==6&&!strcmp(studio::tabName(0),"Main"),"Main opens first with five quick controls and reset row");
 const unsigned quick[]{0,1,2,8,15};bool mapping=true,sync=true;
 for(unsigned row=0;row<5;++row){auto f=quick[row];mapping&=studio::rows(0)[row].feature==static_cast<int>(f);unsigned originalTab=0,originalRow=0;
  for(unsigned tab=1;tab<studio::Tabs;++tab)for(unsigned r=0;r<studio::count(tab);++r)if(studio::rows(tab)[r].feature==static_cast<int>(f)){originalTab=tab;originalRow=r;}
  sync&=originalTab!=0;
  for(unsigned value=0;value<=catalog::maxima[f];++value){battle::settings[f]=value;sync&=studio::selected(0,row,value)==studio::selected(originalTab,originalRow,value);
   for(unsigned c=0;c<studio::rows(0)[row].count;++c)sync&=studio::applyChoice(0,row,value,c)==catalog::applyChoice(originalTab-1,originalRow,value,c);
  }
 }
 check(mapping,"Main selects only the five intended existing gameplay IDs");check(sync,"quick controls share original tab choice mapping for every value");
 bool rowsValid=true;for(unsigned tab=0;tab<studio::Tabs;++tab){rowsValid&=studio::count(tab)<=studio::Capacity;for(unsigned r=0;r<studio::count(tab);++r){auto row=studio::rows(tab)[r];rowsValid&=row.count>0&&row.count<=3;if(row.feature<29)rowsValid&=catalog::valid(row.feature,0);else rowsValid&=studio::action(row.feature)?studio::selection(row.feature)==-1:studio::selection(row.feature)>=0&&static_cast<unsigned>(studio::selection(row.feature))<row.count;}}
 check(rowsValid&&studio::count(99)==0&&studio::rows(99)==nullptr,"all eight tabs remain within six-row and three-choice bounds");
 check(studio::tabX(0)-200*studio::tabScale>330&&studio::tabX(7)+200*studio::tabScale<1670&&(studio::tabX(0)+studio::tabX(7))*.5f==976,"eight compact tabs stay centered between native Q and E prompts");
 for(unsigned f=0;f<29;++f)battle::settings[f]=catalog::maxima[f];studio::profile=0;studio::prefsWrite(studio::profilePath());
 auto backupMarker=root+L"\\keep-backup.bin";const unsigned marker=0x12345678;studio::atomic(backupMarker,&marker,4);
 studio::persist();studio::activate(40,0);
 check(battle::settings[4]==catalog::maxima[4]&&studio::prefsRead(root+L"\\last.bin",values)&&values[4]==catalog::maxima[4],"requesting Reset All cannot clear settings or saved preferences");
 studio::resetAll();bool cleared=true;for(auto& value:battle::settings)cleared&=value==0;
 check(cleared,"Main Reset All clears all29 controls including non-Main options");
 check(studio::prefsRead(root+L"\\last.bin",values)&&std::all_of(values.begin(),values.end(),[](unsigned v){return v==0;}),"Reset All persists defaults for the next launch");
 check(studio::prefsRead(studio::profilePath(),values)&&values[4]==36&&values[27]==5&&GetFileAttributesW(backupMarker.c_str())!=INVALID_FILE_ATTRIBUTES&&studio::autoload,"Reset All preserves saved profiles backups and loading preference");
 check(studio::selected(0,6,0)==-1&&studio::applyChoice(0,6,1,0)==1&&studio::applyChoice(0,3,8,2)==8,"Main rejects extra rows and battle-speed maximum stays bounded");
 check(studio::rows(0)[5].feature==40&&studio::selection(40)==-1&&studio::textStyle(40),"Reset All stays unselected so repeated activation reaches the native setter");
 for(unsigned f=0;f<29;++f)battle::settings[f]=catalog::maxima[f];studio::persist();
 for(auto& value:battle::settings)value=0;studio::autoload=true;studio::restorePreferences();
 check(battle::settings[4]==36&&battle::settings[27]==5,"startup restores validated native settings");
 for(auto& value:battle::settings)value=0;studio::autoload=false;studio::restorePreferences();
 check(battle::settings[4]==0&&studio::prefsRead(root+L"\\last.bin",values)&&values[4]==36,"starting without restored settings preserves the saved file");
 studio::autoload=true;studio::atomic(root+L"\\last.bin","bad",3);studio::restorePreferences();
 check(studio::prefsRecovered&&battle::settings[4]==0,"startup recovers previous valid settings after corruption");
 printf("%u native Studio checks passed.\n",checks);return 0;
}
