#include "Profiler.h"

#include <cstdio>
#include <cstdarg>

// metahost.h supplies the ICLRRuntimeHost declaration (and its IID via mscoree.lib);
// the meta-host *activation* path is not used because CorBindToRuntimeEx is simpler here.
#include <metahost.h>

#pragma comment(lib, "mscoree.lib")

// The C# profiler COM interface IDs are declared but not defined by corprof.h.
// Values are the ones shipped in the .NET Framework SDK headers.
const IID IID_ICorProfilerCallback =
{ 0x176fbed1, 0xa55c, 0x4796, { 0x98, 0xca, 0xa9, 0x5a, 0x0f, 0xd9, 0xd8, 0xc2 } };
const IID IID_ICorProfilerCallback2 =
{ 0x8a8cc829, 0xccf2, 0x49fe, { 0xbb, 0xae, 0x0f, 0x02, 0x22, 0x28, 0x07, 0x1a } };
const IID IID_ICorProfilerInfo =
{ 0x28b5557d, 0x3f3f, 0x48b4, { 0x90, 0xb2, 0x5f, 0x9e, 0xea, 0x2f, 0x6c, 0x48 } };
const IID IID_ICorProfilerInfo2 =
{ 0xcc0935cd, 0xa518, 0x487d, { 0xb0, 0xbb, 0xa9, 0x32, 0x14, 0xe6, 0x54, 0x78 } };

// CorBindToRuntimeEx lives in mscoree; declared here to avoid pulling in a guarded header.
typedef HRESULT(__stdcall* CorBindToRuntimeExFn)(
    LPCWSTR pwszVersion,
    LPCWSTR pwszBuildFlavor,
    DWORD startupFlags,
    REFCLSID rclsid,
    REFIID riid,
    LPVOID* ppv);

namespace
{
    const wchar_t* kPayloadAssembly = L"PclDlSwap.Payload.dll";
    const wchar_t* kPayloadType = L"PclDlSwap.Payload.Host.CoreInstaller";
    const wchar_t* kTargetAssemblyName = L"Plain Craft Launcher 2";

    std::wstring g_logPath;
    CRITICAL_SECTION g_logLock;
    bool g_logReady = false;

    void EnsureLog()
    {
        if (g_logReady) return;
        InitializeCriticalSection(&g_logLock);
        g_logPath = CombinePath(ModuleDirectory(), L"logs\\pcl2swap-profiler.log");
        CreateDirectoryW(CombinePath(ModuleDirectory(), L"logs").c_str(), nullptr);
        g_logReady = true;
    }
}

std::wstring ModuleDirectory()
{
    static std::wstring cached;
    if (!cached.empty()) return cached;

    HMODULE self = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       reinterpret_cast<LPCWSTR>(&ModuleDirectory), &self);
    wchar_t buffer[MAX_PATH] = {};
    GetModuleFileNameW(self, buffer, MAX_PATH);
    cached = buffer;
    size_t slash = cached.find_last_of(L"\\/");
    if (slash != std::wstring::npos) cached.resize(slash);
    return cached;
}

std::wstring CombinePath(const std::wstring& a, const std::wstring& b)
{
    if (a.empty()) return b;
    std::wstring result = a;
    if (result.back() != L'\\' && result.back() != L'/') result += L'\\';
    result += b;
    return result;
}

bool FileExists(const std::wstring& path)
{
    DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && !(attributes & FILE_ATTRIBUTE_DIRECTORY);
}

std::string ReadAllBytes(const std::wstring& path)
{
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return std::string();
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(file, &size) || size.QuadPart <= 0)
    {
        CloseHandle(file);
        return std::string();
    }
    std::string data(static_cast<size_t>(size.QuadPart), '\0');
    DWORD read = 0;
    BOOL ok = ReadFile(file, &data[0], static_cast<DWORD>(data.size()), &read, nullptr);
    CloseHandle(file);
    if (!ok) return std::string();
    data.resize(read);
    return data;
}

std::wstring ToLower(const std::wstring& value)
{
    std::wstring result = value;
    for (auto& ch : result) ch = static_cast<wchar_t>(towlower(ch));
    return result;
}

std::string GuidToString(REFGUID guid)
{
    char buffer[64] = {};
    sprintf_s(buffer, "%08x-%04x-%04x-%02x%02x-%02x%02x%02x%02x%02x%02x",
              guid.Data1, guid.Data2, guid.Data3,
              guid.Data4[0], guid.Data4[1], guid.Data4[2], guid.Data4[3],
              guid.Data4[4], guid.Data4[5], guid.Data4[6], guid.Data4[7]);
    return buffer;
}

void LogLine(const char* format, ...)
{
    EnsureLog();
    EnterCriticalSection(&g_logLock);

    // Raw Win32 I/O: this routine is also called from DllMain, where the CRT is not
    // guaranteed to be initialised yet, so stdio must not be used here.
    char body[2048] = {};
    va_list args;
    va_start(args, format);
    _vsnprintf_s(body, sizeof(body) - 1, _TRUNCATE, format, args);
    va_end(args);

    SYSTEMTIME now{};
    GetLocalTime(&now);
    char line[2200] = {};
    _snprintf_s(line, sizeof(line) - 1, _TRUNCATE, "%04d-%02d-%02d %02d:%02d:%02d.%03d [profiler] %s\r\n",
                now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond, now.wMilliseconds, body);

    HANDLE file = CreateFileW(g_logPath.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                              nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file != INVALID_HANDLE_VALUE)
    {
        DWORD written = 0;
        WriteFile(file, line, static_cast<DWORD>(strlen(line)), &written, nullptr);
        CloseHandle(file);
    }
    LeaveCriticalSection(&g_logLock);
}

// ---------------------------------------------------------------------------
// Managed payload bridge (hosts the CLR inside the target process and calls
// into PclDlSwap.Payload, which performs all managed-side logic).
// ---------------------------------------------------------------------------
bool PayloadBridge::Start()
{
    if (_started) return true;
    _started = true;

    HMODULE mscoree = LoadLibraryW(L"mscoree.dll");
    if (!mscoree)
    {
        LogLine("LoadLibrary(mscoree) failed: %u", GetLastError());
        return false;
    }

    auto bind = reinterpret_cast<CorBindToRuntimeExFn>(GetProcAddress(mscoree, "CorBindToRuntimeEx"));
    if (!bind)
    {
        LogLine("GetProcAddress(CorBindToRuntimeEx) failed");
        return false;
    }

    // The host is already running .NET Framework in-process; CorBindToRuntimeEx hands back the
    // existing runtime's ICLRRuntimeHost, which is what we need to reach the default AppDomain.
    ICLRRuntimeHost* host = nullptr;
    HRESULT hr = bind(L"v4.0.30319", nullptr, 0,
                      CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, reinterpret_cast<LPVOID*>(&host));
    if (FAILED(hr) || !host)
    {
        LogLine("CorBindToRuntimeEx failed: 0x%08x", hr);
        return false;
    }

    const std::wstring assemblyPath = CombinePath(ModuleDirectory(), kPayloadAssembly);
    if (!FileExists(assemblyPath))
    {
        LogLine("payload assembly not found at the profiler directory");
        host->Release();
        return false;
    }

    DWORD returnValue = 0;
    hr = host->ExecuteInDefaultAppDomain(
        assemblyPath.c_str(),
        kPayloadType,
        L"Bootstrap",
        L"profiler",
        &returnValue);

    if (FAILED(hr))
    {
        LogLine("ExecuteInDefaultAppDomain(Bootstrap) failed: 0x%08x (payload exit=%u)", hr, returnValue);
        host->Release();
        return false;
    }

    LogLine("payload bootstrapped (exit=%u)", returnValue);
    _host = host;                       // keep the runtime host alive for the process lifetime

    // The payload writes its three hook addresses next to itself so the profiler can pick
    // them up without extra marshalling (ExecuteInDefaultAppDomain can only return a DWORD).
    const std::wstring pointerFile = CombinePath(ModuleDirectory(), L"data\\hook-pointers.bin");
    if (!FileExists(pointerFile))
    {
        LogLine("hook pointer file missing; no rewrites will be installed");
        return false;
    }

    const std::string raw = ReadAllBytes(pointerFile);
    if (raw.size() < sizeof(HookTable))
    {
        LogLine("hook pointer file truncated (%zu bytes)", raw.size());
        return false;
    }

    memcpy(&_hooks, raw.data(), sizeof(HookTable));
    LogLine("hook table: download=0x%llx ota=0x%llx permission=0x%llx",
            _hooks.downloadHook, _hooks.otaHook, _hooks.permissionHook);
    return _hooks.otaHook != 0;
}



<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->