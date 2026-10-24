#pragma once

#include <windows.h>
#include <cor.h>
#include <corprof.h>
#include <string>
#include <vector>

// {9C1E1D6A-6C6C-4C53-9A0B-5B7A9C3F0D12}
static const GUID CLSID_PclDlSwapProfiler =
{ 0x9c1e1d6a, 0x6c6c, 0x4c53, { 0x9a, 0x0b, 0x5b, 0x7a, 0x9c, 0x3f, 0x0d, 0x12 } };

// One rewrite job: a target method (identified by metadata token inside the PCL module),
// the payload factory method that yields the hook code address, and the calli signature.
struct RewriteJob
{
    mdMethodDef targetToken = 0;
    std::wstring factoryMethod;         // e.g. L"DownloadHookPtr"
    std::vector<BYTE> signature;        // calli StandAloneSig blob
    bool hasThis = false;
    int argCount = 0;
    bool returnsValue = false;
};

std::wstring ModuleDirectory();
std::wstring CombinePath(const std::wstring& a, const std::wstring& b);
bool FileExists(const std::wstring& path);
std::string ReadAllBytes(const std::wstring& path);
std::wstring ToLower(const std::wstring& value);
std::string GuidToString(REFGUID guid);

void LogLine(const char* format, ...);

// Hook entry points exported to the profiler by the managed payload.
struct HookTable
{
    long long downloadHook = 0;         // void (IEnumerable<string>, string, object, object, bool)
    long long otaHook = 0;              // void (bool, string[])
    long long permissionHook = 0;       // bool ()
};

// Payload (managed) contract: PclDlSwap.Payload.Host.CoreInstaller exposes
//   int Bootstrap(string argument)
// which installs the managed logic and writes the three hook addresses to
// <payload dir>\data\hook-pointers.bin as a little-endian int64[3]:
//   [0] download hook, [1] OTA hook, [2] permission hook.
class PayloadBridge
{
public:
    bool Start();                       // loads the CLR host + payload, then reads the hook table
    const HookTable& Hooks() const { return _hooks; }

private:
    void* _host = nullptr;              // ICLRRuntimeHost*
    bool _started = false;
    HookTable _hooks{};
};


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover