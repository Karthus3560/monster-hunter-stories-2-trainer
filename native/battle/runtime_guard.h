#pragma once
#include <bcrypt.h>
#include <string>
#include <vector>
#include <cstdio>
#include <cstdint>
#include <cstring>
namespace runtimeguard {
inline unsigned long long creation=0;
inline bool digest(const void* bytes,unsigned size,unsigned char out[32]){
 BCRYPT_ALG_HANDLE algorithm=nullptr;BCRYPT_HASH_HANDLE hash=nullptr;DWORD length=0,got=0;
 if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)return false;
 BCryptGetProperty(algorithm,BCRYPT_OBJECT_LENGTH,reinterpret_cast<PUCHAR>(&length),sizeof(length),&got,0);std::vector<unsigned char> object(length);
 bool ok=BCryptCreateHash(algorithm,&hash,object.data(),length,nullptr,0,0)>=0;
 if(ok)ok=BCryptHashData(hash,static_cast<PUCHAR>(const_cast<void*>(bytes)),size,0)>=0&&BCryptFinishHash(hash,out,32,0)>=0;
 if(hash)BCryptDestroyHash(hash);BCryptCloseAlgorithmProvider(algorithm,0);return ok;
}
inline bool session(const std::wstring& directory,uintptr_t& text){
 FILE* file=nullptr;_wfopen_s(&file,(directory+L"\\session.guard").c_str(),L"r");if(!file)return false;
 unsigned pid=0;unsigned long long expectedCreation=0,address=0;char magic[32]{};
 bool ok=fscanf_s(file,"%31s %u %llu %llx",magic,static_cast<unsigned>(sizeof(magic)),&pid,&expectedCreation,&address)==4;fclose(file);
 FILETIME start{},exit{},kernel{},user{};if(!ok||strcmp(magic,"KSNSESSION1")||pid!=GetCurrentProcessId()||!GetProcessTimes(GetCurrentProcess(),&start,&exit,&kernel,&user))return false;
 creation=(static_cast<unsigned long long>(start.dwHighDateTime)<<32)|start.dwLowDateTime;if(creation!=expectedCreation)return false;
 auto image=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));auto dos=reinterpret_cast<IMAGE_DOS_HEADER*>(image);auto nt=reinterpret_cast<IMAGE_NT_HEADERS64*>(image+dos->e_lfanew);
 if(dos->e_magic!=IMAGE_DOS_SIGNATURE||nt->Signature!=IMAGE_NT_SIGNATURE||nt->OptionalHeader.SizeOfImage!=86138880)return false;
 wchar_t path[MAX_PATH];GetModuleFileNameW(nullptr,path,MAX_PATH);auto executable=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);if(executable==INVALID_HANDLE_VALUE)return false;
 LARGE_INTEGER size{};if(!GetFileSizeEx(executable,&size)||size.QuadPart<=0||size.QuadPart>200000000){CloseHandle(executable);return false;}
 std::vector<unsigned char> bytes(static_cast<size_t>(size.QuadPart));DWORD read=0;ok=ReadFile(executable,bytes.data(),static_cast<DWORD>(bytes.size()),&read,nullptr)&&read==bytes.size();CloseHandle(executable);
 unsigned char hash[32]{};constexpr unsigned char expected[]{0xac,0x73,0xbb,0xa1,0x01,0x9f,0x09,0xde,0x83,0xca,0x84,0x66,0xc4,0x85,0x6b,0x39,0x37,0xe9,0xd6,0x39,0x72,0xf5,0xfb,0xca,0x2b,0xcb,0xc6,0x70,0x43,0x7b,0x7d,0xa9};
 if(!ok||!digest(bytes.data(),static_cast<unsigned>(bytes.size()),hash)||memcmp(hash,expected,32))return false;
 text=static_cast<uintptr_t>(address);return text>=65536;
}
}
