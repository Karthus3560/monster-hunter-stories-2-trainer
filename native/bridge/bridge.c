#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
// Bounded one-shot Windows library-load result reporting. No game callbacks,
// watches, code patches, inventory edits or background work in DllMain.
typedef struct Request {
 DWORD size,magic;
 WCHAR path[1024];
 HMODULE module;
 DWORD error,completed;
} Request;
__declspec(dllexport) DWORD WINAPI LoadRequestedModule(Request* request){
 {
  if(!request||request->size!=sizeof(Request)||request->magic!=0x4b534c31||request->path[1023])return ERROR_INVALID_PARAMETER;
  SetLastError(ERROR_SUCCESS);
  request->module=LoadLibraryW(request->path);
  request->error=request->module?ERROR_SUCCESS:GetLastError();
  InterlockedExchange((volatile LONG*)&request->completed,1);
  return request->error;
 }
}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID reserved){
 (void)reserved;if(reason==DLL_PROCESS_ATTACH)DisableThreadLibraryCalls(module);return TRUE;
}
