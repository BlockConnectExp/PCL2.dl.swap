using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace PclDlSwap.Payload.Host
{
    /// <summary>
    /// Managed entry point for the ExLoader-injected build.
    ///
    /// ExLoader cannot start a .NET Framework mod by itself (its managed channel resolves Mono,
    /// which PCL does not load), so the native shim hosts the CLR PCL already runs on and calls this
    /// type through <c>ICLRRuntimeHost::ExecuteInDefaultAppDomain(..., "PclDlSwap.Payload.Host.ExLoaderEntry", "Activate", ...)</c>.
    ///
    /// The method must keep the <c>static int Activate(string)</c> shape - that is the only signature
    /// <c>ExecuteInDefaultAppDomain</c> accepts.
    /// </summary>
    public static class ExLoaderEntry
    {
        private static readonly object Gate = new object();
        private static bool _started;
        private static bool _resolverInstalled;

        /// <summary>
        /// Files the shim extracts next to the payload. They are preloaded so the *extracted* build
        /// wins: a same-named assembly sitting next to the host (typical leftover of an earlier
        /// disk-patch run) is found by the default probing path first, and the payload would then
        /// fail against the older interface with a MissingMethodException - the resolver below only
        /// runs after probing fails, which it does not in that case.
        /// </summary>
        private static readonly string[] PreloadedDependencies =
        {
            "PCL2Swap.Api.dll",
            "Gee.External.Capstone.dll",
            "MonoMod.Utils.dll",
            "Mono.Cecil.dll"
        };

        public static int Activate(string argument)
        {
            RegisterDependencyResolver();
            PreloadOwnDependencies();

            lock (Gate)
            {
                if (_started)
                {
                    SwapLog.Info("ExLoaderEntry.Activate 重复调用，已忽略。");
                    return 0;
                }
                _started = true;
            }

            try
            {
                SwapLog.Info("ExLoaderEntry.Activate 进入（参数：" + (argument ?? "(null)") +
                             "，CLR=" + Environment.Version + "，" +
                             (Environment.Is64BitProcess ? "x64" : "x86") + "）。");

                // PCL's own types are JIT'd lazily; wait until the main assembly is really there.
                var host = WaitForHostAssembly(TimeSpan.FromMinutes(3));
                if (host == null)
                {
                    SwapLog.Error("等待宿主程序集超时，注入式功能未装配。");
                    return 2;
                }

                var worker = new Thread(() => CoreInstaller.ActivateFromInjection(host, argument))
                {
                    IsBackground = true,
                    Name = "PCL2Swap.Install"
                };
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
                return 0;
            }
            catch (Exception ex)
            {
                SwapLog.Error("ExLoaderEntry.Activate 失败：" + ex);
                return -1;
            }
        }

        /// <summary>Pins the payload-adjacent copies of the runtime assemblies before first use.</summary>
        private static void PreloadOwnDependencies()
        {
            try
            {
                var directory = Path.GetDirectoryName(typeof(ExLoaderEntry).Assembly.Location);
                if (string.IsNullOrEmpty(directory)) return;

                foreach (var name in PreloadedDependencies)
                {
                    var path = Path.Combine(directory, name);
                    if (!File.Exists(path)) continue;
                    var loaded = Assembly.LoadFrom(path);
                    SwapLog.Info($"依赖预载：{loaded.GetName().Name} {loaded.GetName().Version} ← {path}");
                }
            }
            catch (Exception ex)
            {
                SwapLog.Warn("依赖预载失败（继续正常装配）：" + ex.Message);
            }
        }

        /// <summary>
        /// The native shim extracts the payload next to its managed dependencies in
        /// %TEMP%\PCL2Swap\&lt;version&gt;\, which is outside the host's probing path. Resolve
        /// Gee.External.Capstone from the payload's own directory before any detour is installed.
        /// </summary>
        private static void RegisterDependencyResolver()
        {
            if (_resolverInstalled) return;
            _resolverInstalled = true;

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    var requested = new AssemblyName(args.Name).Name;
                    if (string.IsNullOrEmpty(requested)) return null;
                    var directory = Path.GetDirectoryName(typeof(ExLoaderEntry).Assembly.Location);
                    if (string.IsNullOrEmpty(directory)) return null;
                    var candidate = Path.Combine(directory, requested + ".dll");
                    return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
                }
                catch
                {
                    return null;
                }
            };

            SwapLog.Info("依赖解析器已安装（PCL2Swap.Api / Gee.External.Capstone / MonoMod.Utils / Mono.Cecil → 解包目录）。");
        }

        private static Assembly WaitForHostAssembly(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            Assembly best = null;
            while (DateTime.UtcNow < deadline)
            {
                var entry = Assembly.GetEntryAssembly();
                if (entry != null && entry.GetType("PCL.ModSecret", throwOnError: false) != null)
                    return entry;
                best = best ?? entry;

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetType("PCL.ModMain", throwOnError: false) != null)
                        return assembly;
                }
                Thread.Sleep(250);
            }
            return best;
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
