std::atomic<bool> economyHooksEnabled=false;
PageFn originalCraftCommit=nullptr,originalForgeCommit=nullptr,originalBattleRewardCommit=nullptr,originalQuestRewardCommit=nullptr,originalExpeditionRewardCommit=nullptr;
void __fastcall craftCommit(void* self){
 if(economyHooksEnabled)battle::craftingCommit(reinterpret_cast<uintptr_t>(self),0x1625e50,originalCraftCommit);else originalCraftCommit(self);
}
void __fastcall forgeCommit(void* self){
 if(economyHooksEnabled)battle::craftingCommit(reinterpret_cast<uintptr_t>(self),0x167bf30,originalForgeCommit);else originalForgeCommit(self);
}
void __fastcall battleRewardCommit(void* self){
 if(economyHooksEnabled&&battle::compatible)battle::stageZennyReward(reinterpret_cast<uintptr_t>(self));originalBattleRewardCommit(self);
}
void __fastcall questRewardCommit(void* self){
 if(economyHooksEnabled&&battle::compatible)battle::stageZennyReward(reinterpret_cast<uintptr_t>(self));originalQuestRewardCommit(self);
}
void __fastcall expeditionRewardCommit(void* self){
 if(economyHooksEnabled&&battle::compatible)battle::stageZennyReward(reinterpret_cast<uintptr_t>(self));originalExpeditionRewardCommit(self);
}
