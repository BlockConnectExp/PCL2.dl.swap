// PCL2.dl.swap - ExLoader injection shim (x64).
//
// Why this exists: ExLoader's managed-mod channel is Mono-only (it resolves mono_* exports
// from the target process), and PCL is a .NET Framework application with no Mono loaded, so a
// pure managed mod cannot be started by ExLoader. ExLoader's native channel can start us, but
// it only calls native entry points. This shim is that native entry point: it hosts the CLR
// that PCL already runs on, loads the managed payload into PCL's own default AppDomain, and
// lets the payload do the actual patching.
//
// Constraints kept in mind:
//   * manual mapping never runs the normal loader path, so this DLL avoids the C/C++ runtime
//     entirely (/NODEFAULTLIB) and uses Win32/COM APIs only;
//   * DllMain never does real work - a worker thread does everything;
//   * the managed payload plus its native dependencies travel as RCDATA resources so the mod
//     is a single file on ExLoader's side;
//   * every step is appended to %TEMP%\pcl2swap-native.log for post-mortem analysis.

#include <windows.h>
#include <metahost.h>

#define PAYLOAD_RES_ID    101
#define CLRWRAPPER_RES_ID 102
#define CAPSTONE_RES_ID   103
#define MONOMOD_RES_ID    104
#define CECIL_RES_ID      105
#define API_RES_ID        106
#define PAYLOAD_ASM_NAME  L"PclDlSwap.Payload.dll"
#define CLRWRAPPER_NAME   L"Gee.External.Capstone.dll"
#define CAPSTONE_NAME     L"capstone.dll"
#define MONOMOD_NAME      L"MonoMod.Utils.dll"
#define CECIL_NAME        L"Mono.Cecil.dll"
#define API_ASM_NAME      L"PCL2Swap.Api.dll"
#define ENTRY_TYPE        L"PclDlSwap.Payload.Host.ExLoaderEntry"
#define ENTRY_METHOD      L"Activate"
#define TOOL_VERSION      L"v26.0-Alpha4"

// GUIDs from metahost.h, spelled out so no import library is needed.
static const GUID kClsidClrMetaHost =
    { 0x9280188d, 0x0e8e, 0x4867, { 0xb3, 0x0c, 0x7f, 0xa8, 0x38, 0x84, 0xe8, 0xde } };
static const GUID kIidIclrMetaHost =
    { 0xd332db9e, 0xb9b3, 0x4125, { 0x82, 0x07, 0xa1, 0x48, 0x84, 0xf5, 0x32, 0x16 } };
static const GUID kIidIclrRuntimeInfo =
    { 0xbd39d1d2, 0xba2f, 0x486a, { 0x89, 0xb0, 0xb4, 0xb0, 0xcb, 0x46, 0x68, 0x91 } };
static const GUID kClsidClrRuntimeHost =
    { 0x90f1a06e, 0x7712, 0x4762, { 0x86, 0xb5, 0x7a, 0x5e, 0xba, 0x6b, 0xdb, 0x02 } };
static const GUID kIidIclrRuntimeHost =
    { 0x90f1a06c, 0x7712, 0x4762, { 0x86, 0xb5, 0x7a, 0x5e, 0xba, 0x6b, 0xdb, 0x02 } };

static HMODULE g_module;
static volatile LONG g_workerStarted;

// The worker runs exactly once per process; keeping the path buffers static keeps the thread's stack frame
// small enough that the compiler does not emit a __chkstk call (this DLL links without the CRT).
static wchar_t g_payloadPath[MAX_PATH];
static wchar_t g_clrWrapperPath[MAX_PATH];
static wchar_t g_capstonePath[MAX_PATH];
static wchar_t g_monoModPath[MAX_PATH];
static wchar_t g_cecilPath[MAX_PATH];
static wchar_t g_apiPath[MAX_PATH];

static void AppendLog(const wchar_t* text)
{
    wchar_t path[MAX_PATH];
    DWORD len = GetTempPathW(MAX_PATH, path);
    if (len == 0 || len + 32 >= MAX_PATH)
        return;
    lstrcatW(path, L"pcl2swap-native.log");

    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                              NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE)
        return;
    SetFilePointer(file, 0, NULL, FILE_END);
    DWORD written = 0;
    WriteFile(file, text, lstrlenW(text) * sizeof(wchar_t), &written, NULL);
    WriteFile(file, L"\r\n", 2 * sizeof(wchar_t), &written, NULL);
    CloseHandle(file);
}

static void LogStage(const wchar_t* stage)
{
    wchar_t line[512];
    wsprintfW(line, L"[%u] %s", (unsigned)GetCurrentProcessId(), stage);
    AppendLog(line);
}

static void LogStageCode(const wchar_t* stage, long code)
{
    wchar_t line[512];
    wsprintfW(line, L"[%u] %s (0x%08X)", (unsigned)GetCurrentProcessId(), stage, (unsigned)code);
    AppendLog(line);
}

static bool BuildWorkDirectory(wchar_t* buffer, DWORD capacity)
{
    wchar_t temp[MAX_PATH];
    DWORD len = GetTempPathW(MAX_PATH, temp);
    if (len == 0 || len + 64 >= MAX_PATH)
        return false;
    lstrcatW(temp, L"PCL2Swap\\");
    CreateDirectoryW(temp, NULL);
    lstrcatW(temp, TOOL_VERSION);
    CreateDirectoryW(temp, NULL);
    if (capacity < (DWORD)lstrlenW(temp) + 4)
        return false;
    lstrcpyW(buffer, temp);
    lstrcatW(buffer, L"\\");
    return true;
}

static bool ExtractResource(HMODULE module, int resId, const wchar_t* targetPath)
{
    HRSRC found = FindResourceW(module, MAKEINTRESOURCEW(resId), RT_RCDATA);
    if (found == NULL)
        return false;
    DWORD size = SizeofResource(module, found);
    HGLOBAL loaded = LoadResource(module, found);
    if (loaded == NULL || size == 0)
        return false;
    const void* data = LockResource(loaded);
    if (data == NULL)
        return false;

    // Rewriting is preferred so an updated payload always wins. A file locked by a previous
    // injection of this very process is tolerated when its size matches the resource.
    HANDLE file = CreateFileW(targetPath, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS,
                              FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE)
    {
        HANDLE existing = CreateFileW(targetPath, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING,
                                      FILE_ATTRIBUTE_NORMAL, NULL);
        if (existing != INVALID_HANDLE_VALUE)
        {
            LARGE_INTEGER existingSize;
            BOOL sizeOk = GetFileSizeEx(existing, &existingSize);
            CloseHandle(existing);
            if (sizeOk && existingSize.QuadPart == (LONGLONG)size)
                return true;
        }
        return false;
    }
    DWORD written = 0;
    BOOL ok = WriteFile(file, data, size, &written, NULL);
    CloseHandle(file);
    return ok && written == size;
}

typedef HRESULT (STDAPICALLTYPE *ClrCreateInstanceFn)(REFCLSID clsid, REFIID riid, LPVOID* out);
typedef HRESULT (STDAPICALLTYPE *ExecuteInDefaultAppDomainFn)(
    LPCWSTR assemblyPath, LPCWSTR typeName, LPCWSTR methodName, LPCWSTR argument, DWORD* result);

static bool HostManagedPayload(const wchar_t* payloadPath)
{
    HMODULE mscoree = LoadLibraryW(L"mscoree.dll");
    if (mscoree == NULL)
    {
        LogStageCode(L"LoadLibraryW(mscoree) failed", (long)GetLastError());
        return false;
    }

    ClrCreateInstanceFn createInstance =
        (ClrCreateInstanceFn)GetProcAddress(mscoree, "CLRCreateInstance");
    if (createInstance == NULL)
    {
        LogStage(L"CLRCreateInstance not exported by mscoree.dll");
        return false;
    }

    ICLRMetaHost* metaHost = NULL;
    HRESULT hr = createInstance(kClsidClrMetaHost, kIidIclrMetaHost, (LPVOID*)&metaHost);
    if (FAILED(hr) || metaHost == NULL)
    {
        LogStageCode(L"CLRCreateInstance failed", hr);
        return false;
    }

    ICLRRuntimeInfo* runtimeInfo = NULL;
    hr = metaHost->GetRuntime(L"v4.0.30319", kIidIclrRuntimeInfo, (LPVOID*)&runtimeInfo);
    if (FAILED(hr) || runtimeInfo == NULL)
    {
        LogStageCode(L"ICLRMetaHost::GetRuntime(v4.0.30319) failed", hr);
        metaHost->Release();
        return false;
    }

    ICLRRuntimeHost* runtimeHost = NULL;
    hr = runtimeInfo->GetInterface(kClsidClrRuntimeHost, kIidIclrRuntimeHost, (LPVOID*)&runtimeHost);
    if (FAILED(hr) || runtimeHost == NULL)
    {
        LogStageCode(L"ICLRRuntimeInfo::GetInterface(CLRRuntimeHost) failed", hr);
        runtimeInfo->Release();
        metaHost->Release();
        return false;
    }

    LogStage(L"CLR v4.0.30319 located, executing managed payload");
    DWORD result = 0;
    hr = runtimeHost->ExecuteInDefaultAppDomain(payloadPath, ENTRY_TYPE, ENTRY_METHOD,
                                               TOOL_VERSION, &result);
    if (FAILED(hr))
    {
        LogStageCode(L"ExecuteInDefaultAppDomain failed", hr);
    }
    else
    {
        wchar_t line[256];
        wsprintfW(line, L"managed payload returned %u", (unsigned)result);
        AppendLog(line);
    }

    runtimeHost->Release();
    runtimeInfo->Release();
    metaHost->Release();
    return SUCCEEDED(hr);
}

static DWORD WINAPI WorkerThread(LPVOID parameter)
{
    (void)parameter;
    LogStage(L"injection shim attached");

    wchar_t directory[MAX_PATH];
    if (!BuildWorkDirectory(directory, MAX_PATH))
    {
        LogStage(L"could not build the work directory");
        return 1;
    }

    lstrcpyW(g_payloadPath, directory);   lstrcatW(g_payloadPath, PAYLOAD_ASM_NAME);
    lstrcpyW(g_clrWrapperPath, directory); lstrcatW(g_clrWrapperPath, CLRWRAPPER_NAME);
    lstrcpyW(g_capstonePath, directory);   lstrcatW(g_capstonePath, CAPSTONE_NAME);
    lstrcpyW(g_monoModPath, directory);    lstrcatW(g_monoModPath, MONOMOD_NAME);
    lstrcpyW(g_cecilPath, directory);      lstrcatW(g_cecilPath, CECIL_NAME);
    lstrcpyW(g_apiPath, directory);        lstrcatW(g_apiPath, API_ASM_NAME);

    if (!ExtractResource(g_module, PAYLOAD_RES_ID, g_payloadPath) ||
        !ExtractResource(g_module, CLRWRAPPER_RES_ID, g_clrWrapperPath) ||
        !ExtractResource(g_module, CAPSTONE_RES_ID, g_capstonePath) ||
        !ExtractResource(g_module, MONOMOD_RES_ID, g_monoModPath) ||
        !ExtractResource(g_module, CECIL_RES_ID, g_cecilPath) ||
        !ExtractResource(g_module, API_RES_ID, g_apiPath))
    {
        LogStageCode(L"payload extraction failed", (long)GetLastError());
        return 1;
    }
    LogStage(L"payload extracted");

    // Preload the native disassembler so the managed wrapper's DllImport("capstone") resolves to
    // this extracted copy instead of searching the launcher directory.
    HMODULE capstone = LoadLibraryW(g_capstonePath);
    if (capstone == NULL)
        LogStageCode(L"LoadLibraryW(capstone) failed", (long)GetLastError());
    else
        LogStage(L"capstone preloaded");

    // PCL's own CLR needs a moment to come up when ExLoader injects during process start.
    for (int attempt = 0; attempt < 60; attempt++)
    {
        if (GetModuleHandleW(L"clr.dll") != NULL)
            break;
        Sleep(250);
    }
    if (GetModuleHandleW(L"clr.dll") == NULL)
        LogStage(L"warning: clr.dll still not loaded, trying anyway");

    HostManagedPayload(g_payloadPath);
    LogStage(L"shim finished");
    return 0;
}

static void EnsureWorker()
{
    if (InterlockedCompareExchange(&g_workerStarted, 1, 0) == 0)
        CreateThread(NULL, 0, WorkerThread, NULL, 0, NULL);
}

BOOL WINAPI DllMain(HMODULE module, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
        EnsureWorker();
    }
    return TRUE;
}

// ExLoader may call an exported start function instead of / in addition to the PE entry point;
// both shapes are provided so the mod works either way.
extern "C" __declspec(dllexport) int Pcl2SwapStart(void)
{
    EnsureWorker();
    return 0;
}

extern "C" __declspec(dllexport) int Pcl2SwapInit(void* argument)
{
    (void)argument;
    EnsureWorker();
    return 1;
}
