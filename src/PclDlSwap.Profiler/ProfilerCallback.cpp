#include "ProfilerCallback.h"
#include "RewritePlan.h"

#include <sstream>
#include <cstring>

namespace
{
    const DWORD kEventMask = COR_PRF_MONITOR_MODULE_LOADS | COR_PRF_MONITOR_ASSEMBLY_LOADS;
    std::vector<PlanEntry> g_plan;
    bool g_planLoaded = false;

    // Emits a StandAloneSig for the calli site and returns its token.
    bool CreateCalliSignature(IMetaDataEmit* emit, const PlanEntry& entry, mdSignature* outToken)
    {
        // Locals signature brand: 0x07; calling convention: DEFAULT | VARARG (0x05) = calli
        BYTE blob[5];
        int length = 0;
        blob[length++] = 0x07;
        blob[length++] = 0x05;
        blob[length++] = entry.argCount;
        blob[length++] = entry.returnsValue ? 0x02 : 0x01;      // ELEMENT_TYPE_BOOLEAN / VOID

        HRESULT hr = emit->GetTokenFromSig(blob, static_cast<ULONG>(length), outToken);
        if (FAILED(hr))
        {
            LogLine("GetTokenFromSig failed: 0x%08x", hr);
            return false;
        }
        return true;
    }

    std::vector<BYTE> BuildReplacement(const PlanEntry& entry, mdSignature signatureToken, long long hookAddress)
    {
        std::vector<BYTE> il;
        il.push_back(0x20);                                          // ldc.i4 hookAddressLow
        const DWORD low = static_cast<DWORD>(hookAddress & 0xFFFFFFFFu);
        il.insert(il.end(), reinterpret_cast<const BYTE*>(&low), reinterpret_cast<const BYTE*>(&low) + 4);
        il.push_back(0x49);                                          // ldind.i4
        int argumentBase = entry.hasThis ? 1 : 0;
        for (int i = 0; i < entry.argCount; ++i)
        {
            int index = argumentBase + i;
            if (index <= 3) il.push_back(static_cast<BYTE>(0x02 + index));
            else { il.push_back(0xFE); il.push_back(static_cast<BYTE>(0x09 + index)); }
        }
        il.push_back(0x29);                                          // calli
        il.insert(il.end(), reinterpret_cast<const BYTE*>(&signatureToken), reinterpret_cast<const BYTE*>(&signatureToken) + 4);
        il.push_back(0x2A);                                          // ret
        return il;
    }

    bool RewriteMethod(ICorProfilerInfo2* info, ModuleID moduleId, const PlanEntry& entry, long long hookAddress)
    {
        IMetaDataImport* import = nullptr;
        IMetaDataEmit* emit = nullptr;
        DWORD flags = ofRead | ofWrite;
        HRESULT hr = info->GetModuleMetaData(moduleId, flags, IID_IMetaDataImport, reinterpret_cast<IUnknown**>(&import));
        if (FAILED(hr) || !import)
        {
            LogLine("GetModuleMetaData(import) failed: 0x%08x", hr);
            return false;
        }
        hr = import->QueryInterface(IID_IMetaDataEmit, reinterpret_cast<void**>(&emit));
        if (FAILED(hr) || !emit)
        {
            LogLine("QueryInterface(IMetaDataEmit) failed: 0x%08x", hr);
            import->Release();
            return false;
        }

        mdSignature signatureToken = mdSignatureNil;
        bool ok = CreateCalliSignature(emit, entry, &signatureToken);
        if (ok)
        {
            LPCBYTE oldBody = nullptr;
            ULONG oldSize = 0;
            hr = info->GetILFunctionBody(moduleId, entry.targetToken, &oldBody, &oldSize);
            if (FAILED(hr))
            {
                LogLine("GetILFunctionBody(0x%08x) failed: 0x%08x", entry.targetToken, hr);
                ok = false;
            }
            else
            {
                const std::vector<BYTE> replacement = BuildReplacement(entry, signatureToken, hookAddress);
                // SetILFunctionBody copies the body into CLR-owned memory, so a plain buffer is fine.
                hr = info->SetILFunctionBody(moduleId, entry.targetToken, replacement.data());
                if (FAILED(hr))
                {
                    LogLine("SetILFunctionBody(0x%08x) failed: 0x%08x", entry.targetToken, hr);
                    ok = false;
                }
                else
                {
                    LogLine("rewrote token=0x%08x hook=%d argCount=%d il=%zu bytes",
                            entry.targetToken, entry.hookIndex, entry.argCount, replacement.size());
                }
            }
        }

        emit->Release();
        import->Release();
        return ok;
    }

    // Measures the method body so we can verify the target before rewriting it.
    bool ReadMethodBodySize(ICorProfilerInfo2* info, ModuleID moduleId, mdToken token, ULONG* size)
    {
        LPCBYTE body = nullptr;
        ULONG bodySize = 0;
        if (FAILED(info->GetILFunctionBody(moduleId, token, &body, &bodySize))) return false;
        *size = bodySize;
        return true;
    }
}

std::vector<PlanEntry> LoadRewritePlan()
{
    std::vector<PlanEntry> plan;
    const std::wstring path = CombinePath(CombinePath(ModuleDirectory(), L"data"), L"rewrite-plan.tsv");
    if (!FileExists(path))
    {
        LogLine("rewrite plan not found: data\\rewrite-plan.tsv");
        return plan;
    }

    const std::string text = ReadAllBytes(path);
    std::istringstream stream(text);
    std::string line;
    while (std::getline(stream, line))
    {
        if (line.empty() || line[0] == '#') continue;

        std::istringstream parts(line);
        PlanEntry entry;
        std::string mvid, token, hook, hasThis, argCount, returnsValue;
        if (!std::getline(parts, mvid, '\t')) continue;
        if (!std::getline(parts, token, '\t')) continue;
        if (!std::getline(parts, hook, '\t')) continue;
        if (!std::getline(parts, hasThis, '\t')) continue;
        if (!std::getline(parts, argCount, '\t')) continue;
        if (!std::getline(parts, returnsValue, '\t')) continue;

        std::wstring mvidWide(mvid.begin(), mvid.end());
        entry.moduleMvid = ToLower(mvidWide);
        entry.targetToken = strtoul(token.c_str(), nullptr, 16);
        entry.hookIndex = atoi(hook.c_str());
        entry.hasThis = hasThis == "1";
        entry.argCount = atoi(argCount.c_str());
        entry.returnsValue = returnsValue == "1";
        plan.push_back(entry);
    }

    LogLine("rewrite plan loaded: %zu entries", plan.size());
    return plan;
}

ProfilerCallback::ProfilerCallback() = default;

ProfilerCallback::~ProfilerCallback()
{
    if (_info) _info->Release();
}

HRESULT STDMETHODCALLTYPE ProfilerCallback::QueryInterface(REFIID riid, void** ppvObject)
{
    if (!ppvObject) return E_POINTER;
    LogLine("callback QueryInterface: %s", GuidToString(riid).c_str());
    if (riid == IID_IUnknown || riid == IID_ICorProfilerCallback || riid == IID_ICorProfilerCallback2)
    {
        *ppvObject = static_cast<ICorProfilerCallback2*>(this);
        AddRef();
        return S_OK;
    }

    // Everything else (notably the ICorProfilerInfo family, which the CLR itself implements)
    // must be declined: answering with this object would hand the CLR the wrong vtable and it
    // would then abandon profiler initialisation.
    *ppvObject = nullptr;
    return E_NOINTERFACE;
}

ULONG STDMETHODCALLTYPE ProfilerCallback::AddRef() { return InterlockedIncrement(&_refCount); }

ULONG STDMETHODCALLTYPE ProfilerCallback::Release()
{
    const ULONG count = InterlockedDecrement(&_refCount);
    if (count == 0) delete this;
    return count;
}

HRESULT ProfilerCallback::InitializeFromEnvironment(IUnknown* profilerInfoUnknown)
{
    HRESULT hr = profilerInfoUnknown->QueryInterface(IID_ICorProfilerInfo2, reinterpret_cast<void**>(&_info));
    if (FAILED(hr) || !_info)
    {
        LogLine("QueryInterface(ICorProfilerInfo2) failed: 0x%08x", hr);
        return hr;
    }

    DWORD mask = kEventMask;
    hr = _info->SetEventMask(mask);
    if (FAILED(hr))
    {
        LogLine("SetEventMask(0x%x) failed: 0x%08x", mask, hr);
        return hr;
    }

    LogLine("profiler initialised (event mask=0x%x)", mask);
    if (!_payload.Start())
    {
        LogLine("payload bridge failed; profiler will stay passive");
        return S_OK;
    }
    return S_OK;
}

HRESULT STDMETHODCALLTYPE ProfilerCallback::Initialize(IUnknown* profilerInfoUnknown)
{
    return InitializeFromEnvironment(profilerInfoUnknown);
}

HRESULT STDMETHODCALLTYPE ProfilerCallback::HandleModuleLoadFinishedInternal(ModuleID moduleId, HRESULT hrStatus)
{
    if (FAILED(hrStatus) || _moduleHandled) return S_OK;
    if (!_payload.Hooks().otaHook) return S_OK;
    if (!g_planLoaded)
    {
        g_plan = LoadRewritePlan();
        g_planLoaded = true;
    }
    if (g_plan.empty()) return S_OK;

    // Identify the module by its MVID: the plan is keyed on the exact build we validated.
    // Both the managed module name and the MVID come from the metadata scope properties.
    IMetaDataImport* scopeImport = nullptr;
    if (FAILED(_info->GetModuleMetaData(moduleId, ofRead, IID_IMetaDataImport,
                                        reinterpret_cast<IUnknown**>(&scopeImport))) || !scopeImport)
        return S_OK;

    WCHAR scopeName[512] = {};
    ULONG scopeNameLength = 0;
    GUID mvid{};
    const HRESULT scopeResult = scopeImport->GetScopeProps(scopeName, _countof(scopeName), &scopeNameLength, &mvid);
    scopeImport->Release();
    if (FAILED(scopeResult)) return S_OK;

    const std::wstring scopeNameLower = ToLower(scopeName);
    if (scopeNameLower.find(L"plain craft launcher") == std::wstring::npos) return S_OK;

    const std::string mvidText = GuidToString(mvid);
    std::wstring mvidWide(mvidText.begin(), mvidText.end());
    const std::wstring mvidLower = ToLower(mvidWide);

    std::vector<const PlanEntry*> entries;
    for (const auto& entry : g_plan)
    {
        if (entry.moduleMvid == mvidLower || entry.moduleMvid.empty()) entries.push_back(&entry);
    }
    if (entries.empty()) return S_OK;

    const HookTable& hooks = _payload.Hooks();
    const long long addresses[3] = { hooks.downloadHook, hooks.otaHook, hooks.permissionHook };

    LogLine("target module detected: mvid=%s entries=%zu", mvidText.c_str(), entries.size());
    int applied = 0;
    for (const auto* entry : entries)
    {
        long long address = 0;
        if (entry->hookIndex >= 0 && entry->hookIndex < 3) address = addresses[entry->hookIndex];
        if (address == 0)
        {
            LogLine("hook index %d has no address; skipping token 0x%08x", entry->hookIndex, entry->targetToken);
            continue;
        }

        ULONG bodySize = 0;
        if (!ReadMethodBodySize(_info, moduleId, entry->targetToken, &bodySize))
        {
            LogLine("token 0x%08x has no IL body on this build; skipping", entry->targetToken);
            continue;
        }

        if (RewriteMethod(_info, moduleId, *entry, address)) applied++;
    }

    LogLine("rewrites applied: %d/%zu", applied, entries.size());
    _moduleHandled = applied > 0;
    return S_OK;
}

HRESULT STDMETHODCALLTYPE ProfilerCallback::ModuleLoadFinished(ModuleID moduleId, HRESULT hrStatus)
{
    return HandleModuleLoadFinishedInternal(moduleId, hrStatus);
}

// --- remaining callbacks: no-op ---
#define STUB(name, signature) HRESULT STDMETHODCALLTYPE ProfilerCallback::name signature { return S_OK; }
STUB(Shutdown, ())
STUB(AppDomainCreationStarted, (AppDomainID))
STUB(AppDomainCreationFinished, (AppDomainID, HRESULT))
STUB(AppDomainShutdownStarted, (AppDomainID))
STUB(AppDomainShutdownFinished, (AppDomainID, HRESULT))
STUB(AssemblyLoadStarted, (AssemblyID))
STUB(AssemblyLoadFinished, (AssemblyID, HRESULT))
STUB(AssemblyUnloadStarted, (AssemblyID))
STUB(AssemblyUnloadFinished, (AssemblyID, HRESULT))
STUB(ModuleLoadStarted, (ModuleID))
STUB(ModuleUnloadStarted, (ModuleID))
STUB(ModuleUnloadFinished, (ModuleID, HRESULT))
STUB(ModuleAttachedToAssembly, (ModuleID, AssemblyID))
STUB(ClassLoadStarted, (ClassID))
STUB(ClassLoadFinished, (ClassID, HRESULT))
STUB(ClassUnloadStarted, (ClassID))
STUB(ClassUnloadFinished, (ClassID, HRESULT))
STUB(FunctionUnloadStarted, (FunctionID))
STUB(JITCompilationStarted, (FunctionID, BOOL))
STUB(JITCompilationFinished, (FunctionID, HRESULT, BOOL))
STUB(JITCachedFunctionSearchStarted, (FunctionID, BOOL*))
STUB(JITCachedFunctionSearchFinished, (FunctionID, COR_PRF_JIT_CACHE))
STUB(JITFunctionPitched, (FunctionID))
STUB(JITInlining, (FunctionID, FunctionID, BOOL*))
STUB(ThreadCreated, (ThreadID))
STUB(ThreadDestroyed, (ThreadID))
STUB(ThreadAssignedToOSThread, (ThreadID, DWORD))
STUB(RemotingClientInvocationStarted, ())
STUB(RemotingClientSendingMessage, (GUID*, BOOL))
STUB(RemotingClientReceivingReply, (GUID*, BOOL))
STUB(RemotingClientInvocationFinished, ())
STUB(RemotingServerReceivingMessage, (GUID*, BOOL))
STUB(RemotingServerInvocationStarted, ())
STUB(RemotingServerInvocationReturned, ())
STUB(RemotingServerSendingReply, (GUID*, BOOL))
STUB(UnmanagedToManagedTransition, (FunctionID, COR_PRF_TRANSITION_REASON))
STUB(ManagedToUnmanagedTransition, (FunctionID, COR_PRF_TRANSITION_REASON))
STUB(RuntimeSuspendStarted, (COR_PRF_SUSPEND_REASON))
STUB(RuntimeSuspendFinished, ())
STUB(RuntimeSuspendAborted, ())
STUB(RuntimeResumeStarted, ())
STUB(RuntimeResumeFinished, ())
STUB(RuntimeThreadSuspended, (ThreadID))
STUB(RuntimeThreadResumed, (ThreadID))
STUB(MovedReferences, (ULONG, ObjectID[], ObjectID[], ULONG[]))
STUB(ObjectAllocated, (ObjectID, ClassID))
STUB(ObjectsAllocatedByClass, (ULONG, ClassID[], ULONG[]))
STUB(ObjectReferences, (ObjectID, ClassID, ULONG, ObjectID[]))
STUB(RootReferences, (ULONG, ObjectID[]))
STUB(ExceptionThrown, (ObjectID))
STUB(ExceptionSearchFunctionEnter, (FunctionID))
STUB(ExceptionSearchFunctionLeave, ())
STUB(ExceptionSearchFilterEnter, (FunctionID))
STUB(ExceptionSearchFilterLeave, ())
STUB(ExceptionSearchCatcherFound, (FunctionID))
STUB(ExceptionOSHandlerEnter, (UINT_PTR))
STUB(ExceptionOSHandlerLeave, (UINT_PTR))
STUB(ExceptionUnwindFunctionEnter, (FunctionID))
STUB(ExceptionUnwindFunctionLeave, ())
STUB(ExceptionUnwindFinallyEnter, (FunctionID))
STUB(ExceptionUnwindFinallyLeave, ())
STUB(ExceptionCatcherEnter, (FunctionID, ObjectID))
STUB(ExceptionCatcherLeave, ())
STUB(COMClassicVTableCreated, (ClassID, REFGUID, void*, ULONG))
STUB(COMClassicVTableDestroyed, (ClassID, REFGUID, void*))
STUB(ExceptionCLRCatcherFound, ())
STUB(ExceptionCLRCatcherExecute, ())
STUB(ThreadNameChanged, (ThreadID, ULONG, WCHAR[]))
STUB(GarbageCollectionStarted, (int, BOOL[], COR_PRF_GC_REASON))
STUB(SurvivingReferences, (ULONG, ObjectID[], ULONG[]))
STUB(GarbageCollectionFinished, ())
STUB(FinalizeableObjectQueued, (DWORD, ObjectID))
STUB(RootReferences2, (ULONG, ObjectID[], COR_PRF_GC_ROOT_KIND[], COR_PRF_GC_ROOT_FLAGS[], UINT_PTR[]))
STUB(HandleCreated, (GCHandleID, ObjectID))
STUB(HandleDestroyed, (GCHandleID))
#undef STUB



<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->