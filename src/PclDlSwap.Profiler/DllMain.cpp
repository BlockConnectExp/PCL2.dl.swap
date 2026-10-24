#include "Profiler.h"
#include "ProfilerCallback.h"

#include <new>

namespace
{
    HINSTANCE g_module = nullptr;

    class ProfilerClassFactory : public IClassFactory
    {
    public:
        // IUnknown
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppvObject) override
        {
            if (!ppvObject) return E_POINTER;
            LogLine("factory QueryInterface: %s", GuidToString(riid).c_str());
            if (riid == IID_IUnknown || riid == IID_IClassFactory)
            {
                *ppvObject = static_cast<IClassFactory*>(this);
                AddRef();
                return S_OK;
            }
            *ppvObject = nullptr;
            return E_NOINTERFACE;
        }

        ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&_refCount); }

        ULONG STDMETHODCALLTYPE Release() override
        {
            const ULONG count = InterlockedDecrement(&_refCount);
            if (count == 0) delete this;
            return count;
        }

        // IClassFactory
        HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID riid, void** ppvObject) override
        {
            LogLine("CreateInstance requested: riid=%s outer=%p", GuidToString(riid).c_str(), outer);
            if (outer != nullptr) return CLASS_E_NOAGGREGATION;
            if (!ppvObject) return E_POINTER;

            auto* callback = new (std::nothrow) ProfilerCallback();
            if (!callback) return E_OUTOFMEMORY;

            HRESULT hr = callback->QueryInterface(riid, ppvObject);
            callback->Release();
            return hr;
        }

        HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override
        {
            if (lock) InterlockedIncrement(&_lockCount);
            else InterlockedDecrement(&_lockCount);
            return S_OK;
        }

    private:
        LONG _refCount = 1;
        LONG _lockCount = 0;
    };
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = instance;
        DisableThreadLibraryCalls(instance);
        LogLine("profiler DLL attached");
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        LogLine("profiler DLL detached");
    }
    return TRUE;
}

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID riid, void** ppvObject)
{
    if (clsid != CLSID_PclDlSwapProfiler) return CLASS_E_CLASSNOTAVAILABLE;
    LogLine("DllGetClassObject called");
    auto* factory = new (std::nothrow) ProfilerClassFactory();
    if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(riid, ppvObject);
    factory->Release();
    return hr;
}

extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    return S_FALSE;
}

// --- forwarded exports: the CLR instantiates the profiler through the legacy
//     COM entry points that older profiling hosts still use ---
extern "C" HRESULT __stdcall DllGetClassObjectWrapper(REFCLSID clsid, REFIID riid, void** ppvObject)
{
    return DllGetClassObject(clsid, riid, ppvObject);
}


<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->