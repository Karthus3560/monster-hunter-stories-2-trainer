#pragma once
inline uintptr_t kTextBase=0;
constexpr size_t kTextBytes=38087;
constexpr char kTextHash[]="BD02CD7AA46356642264F4650C64DC583A4371B27A607D77D8E66FAADE5AA351";
struct TextPatch {size_t offset;const char* original;const char* modified;bool conditional;};
constexpr TextPatch kTexts[]={
{22558,"Other Settings","Mods",true},
{22527,"Adjust various other settings.","Kinship Studio battle controls",true},
};
