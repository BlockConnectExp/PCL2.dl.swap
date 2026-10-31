using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PCL2Swap.Api;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>
    /// Read-only compatibility probe for the launcher the payload just bound to. With
    /// PCL2SWAP_SELFTEST=1 every typed access family is walked once and the outcome lands in the
    /// swap log, so a new PCL build can be judged from its log instead of from a pile of manual
    /// clicks - the PCL2-Swap equivalent of a module compatibility self-check.
    /// Only side-effect-free members are touched: no downloads, no launcher update, no game launch.
    /// </summary>
    internal static class SelfTest
    {
        /// <summary>Keeps the self-test's loader subscriptions alive so late callbacks still log.</summary>
        private static readonly List<IDisposable> HeldWatches = new List<IDisposable>();

        public static void RunIfRequested(ISwapApi api)
        {
            if (api == null) return;
            if (!string.Equals(Environment.GetEnvironmentVariable("PCL2SWAP_SELFTEST"), "1",
                    StringComparison.Ordinal))
                return;

            // The launcher is still building its windows while the payload boots: give it a moment
            // and then probe from its own dispatcher, so members that touch UI objects
            // (McLoginAble and friends) behave exactly like they do for a real click.
            var worker = new Thread(() =>
            {
                try { Thread.Sleep(5000); } catch { }
                PclUi.OnUiThread(() => { Run(api); return true; }, false);
            })
            {
                IsBackground = true,
                Name = "PCL2Swap.SelfTest"
            };
            worker.Start();
        }

        private static void Run(ISwapApi api)
        {
            SwapLog.Info("[selftest] 开始：宿主 " + (SwapHost.HostAssembly?.Location ?? "(unknown)"));

            var failures = new List<string>();
            var succeeded = 0;

            void Probe(string label, Func<object> probe)
            {
                try
                {
                    var value = probe();
                    succeeded++;
                    SwapLog.Info($"[selftest] ok   {label} = {Describe(value)}");
                }
                catch (Exception ex)
                {
                    failures.Add(label);
                    var inner = ex.InnerException;
                    SwapLog.Warn($"[selftest] FAIL {label}：{ex.GetType().Name}: {ex.Message}" +
                                 (inner == null ? string.Empty :
                                     $" ← {inner.GetType().Name}: {inner.Message}"));
                }
            }

            Probe("Host.SwapVersion", () => api.Host.SwapVersion);
            Probe("Host.ApiVersion", () => api.Host.ApiVersion);
            Probe("Host.PclVersion", () => api.Host.PclVersion);
            Probe("模块作用域判定", () => RunScopeProbe(api));
            Probe("Host.Shape", () => api.Host.Shape);
            Probe("Host.ProcessId", () => api.Host.ProcessId);
            Probe("Host.AddonDirectory", () => api.Host.AddonDirectory);
            Probe("Host.LogDirectory", () => api.Host.LogDirectory);
            Probe("Host.SafeMode", () => api.Host.SafeMode);
            Probe("Host.SafeModeReason", () => api.Host.SafeModeReason);
            Probe("Host.RecoveryDirectory", () => api.Host.RecoveryDirectory);

            Probe("I18n.Explicit", () => api.I18n.T("启动", "Launch"));
            Probe("I18n.Count", () => api.I18n.Count >= 450 ? api.I18n.Count : 0);
            Probe("I18n.Has(multiplayer)", () => api.I18n.Has("联机模块"));
            Probe("I18n.Roundtrip", () => RunI18nProbe(api));
            Probe("I18n.Format", () => api.I18n.Format("已处理 {0} 项", 3));
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                Probe("Ui.API1.1.Slider", () => api.Ui.Slider("v", 0, 10, 1, 5, _ => { }) != null);
                Probe("Ui.API1.1.TextInput", () => api.Ui.TextInput("t", "x", _ => { }) != null);
                Probe("Ui.API1.1.Link", () => api.Ui.Link("link", "https://example.com") != null);
                Probe("Ui.API1.1.Separator", () => api.Ui.Separator() != null);
                Probe("Ui.API1.3.RadioGroup",
                    () => api.Ui.RadioGroup("choice", new[] { "one", "two" }, 0, _ => { }) != null);
                Probe("Ui.API1.3.NumberInput",
                    () => api.Ui.NumberInput("number", 1, 0, 10, 1, _ => { }) != null);
                Probe("Ui.API1.3.PasswordInput",
                    () => api.Ui.PasswordInput("secret", string.Empty, _ => { }) != null);
                Probe("Ui.API1.3.MultilineTextInput",
                    () => api.Ui.MultilineTextInput("notes", string.Empty, 70, _ => { }) != null);
                Probe("Ui.API1.3.Progress",
                    () => api.Ui.Progress("progress", 0.25, false)?.Element != null);
                Probe("Ui.API1.7.Timeline",
                    () => api.Ui.Timeline(api.Diagnostics.Recent(5), 160) != null);
                Probe("Ui.API1.8.Table",
                    () => api.Ui.Table(new[] { "Key", "Value" },
                        new IReadOnlyList<string>[] { new[] { "api", api.Host.ApiVersion } },
                        new[] { 1.0, 2.0 }) != null);
                Probe("Ui.API1.8.EmptyState",
                    () => api.Ui.EmptyState("No items", "Add one to continue.") != null);
                Probe("Ui.API1.8.CodeBlock",
                    () => api.Ui.CodeBlock("{\"ok\":true}", 120) != null);
            }
            Probe("Files.API1.8.RoundTrip", () =>
            {
                const string relative = "selftest/api18.txt";
                api.Files.WriteText(relative, "ok", false);
                var value = api.Files.ReadText(relative);
                api.Files.Delete(relative);
                return value;
            });
            Probe("Messaging.API1.8.RoundTrip", () =>
            {
                var received = string.Empty;
                using (api.Messaging.Subscribe("selftest.api18", message => received = message.Payload as string))
                    api.Messaging.Publish("selftest.api18", "ok");
                return received;
            });
            Probe("Pcl.Logs.LogPath", () => api.Pcl.Logs.LogPath);
            Probe("Pcl.Ui.CurrentPage", () => api.Pcl.Ui.CurrentPage);
            Probe("Pcl.Navigation", () => api.Pcl.Navigation != null);
            Probe("Pcl.Threading", () =>
            {
                var wasUi = api.Pcl.Threading.IsUiThread;
                var value = api.Pcl.Threading.Invoke(() => 7);
                return $"wasUi={wasUi}, value={value}";
            });
            Probe("Pcl.Environment", () =>
                api.Pcl.Environment.HostPath + " | " + api.Pcl.Environment.DataDirectory);
            Probe("Pcl.Profiles", () => api.Pcl.Profiles != null);
            Probe("Pcl.Net.Request", () => typeof(IPclNetAccess).GetMethod("Request") != null);
            Probe("Pcl.Shell.Family", () => api.Pcl.Shell != null);
            Probe("Pcl.Clipboard.Family", () => api.Pcl.Clipboard != null);
            Probe("Pcl.Clipboard.GetText", () => api.Pcl.Clipboard.GetText() ?? string.Empty);
            Probe("Ui.API1.2.RegisterMorePage",
                () => typeof(IUi).GetMethod("RegisterMorePage") != null);
            Probe("仓库.索引解析", () =>
            {
                const string json =
                    "{\"schema\":1,\"modules\":[{\"id\":\"test.module\",\"name\":\"Test\"," +
                    "\"version\":\"1.0.0\",\"download\":\"https://example.com/x.swapaddon\"," +
                    "\"sha256\":\"abc\",\"size\":123}]}";
                var snapshot = AddonRepository.Parse(json, "selftest");
                return snapshot.Modules.Count == 1 && snapshot.Modules[0].Id == "test.module"
                    ? "ok"
                    : "modules=" + snapshot.Modules.Count + " error=" + (snapshot.Error ?? "(none)");
            });
            Probe("仓库.内置索引", () =>
            {
                var snapshot = AddonRepository.LoadBundled();
                return snapshot.Modules.Count >= 1 &&
                       !string.IsNullOrEmpty(snapshot.Modules[0].Download)
                    ? "ok"
                    : "modules=" + snapshot.Modules.Count + " error=" + (snapshot.Error ?? "(none)");
            });
            Probe("仓库.版本比较", () =>
                AddonRepository.CompareVersion("v26.0-Alpha 4", "v26.0-Alpha 3") > 0 &&
                AddonRepository.CompareVersion("v1.0.0", "v1.0.0") == 0 &&
                AddonRepository.CompareVersion("v1.0.0", "v1.1.0") < 0);
            Probe("仓库.自定义源", () =>
            {
                var before = AddonRepository.CurrentIndexUrl;
                var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "pcl2swap-index-" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    System.IO.File.WriteAllText(temp,
                        "{\"schema\":1,\"modules\":[{\"id\":\"local.module\",\"name\":\"Local\"," +
                        "\"version\":\"1.0.0\",\"download\":\"https://example.com/x.swapaddon\"}]}");
                    AddonRepository.SetSource(temp);
                    var snapshot = AddonRepository.Load(AddonRepository.CurrentIndexUrl);
                    return AddonRepository.CurrentIndexUrl == temp && snapshot.Modules.Count == 1;
                }
                finally
                {
                    AddonRepository.SetSource(
                        string.Equals(before, AddonRepository.DefaultIndexUrl, StringComparison.Ordinal)
                            ? string.Empty : before);
                    try { System.IO.File.Delete(temp); } catch { }
                }
            });
            Probe("仓库.签名校验", () =>
            {
                using (var rsa = System.Security.Cryptography.RSA.Create())
                {
                    rsa.KeySize = 2048;
                    var trustedDir = PackageTrust.TrustedKeyDirectory;
                    System.IO.Directory.CreateDirectory(trustedDir);
                    var keyFile = System.IO.Path.Combine(trustedDir, "selftest-index.xml");
                    System.IO.File.WriteAllText(keyFile, rsa.ToXmlString(false));
                    try
                    {
                        var json = System.Text.Encoding.UTF8.GetBytes(
                            "{\"schema\":1,\"modules\":[]}");
                        var signature = rsa.SignData(json,
                            System.Security.Cryptography.HashAlgorithmName.SHA256,
                            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
                        var keyId = PackageTrust.KeyIdFor(rsa);
                        var sigJson = System.Text.Encoding.UTF8.GetBytes(
                            "{\"algorithm\":\"RSA-SHA256\",\"keyId\":\"" + keyId +
                            "\",\"signature\":\"" + Convert.ToBase64String(signature) + "\"}");
                        var good = IndexTrust.Verify(json, sigJson);
                        var tampered = System.Text.Encoding.UTF8.GetBytes(
                            "{\"schema\":1,\"modules\":[{\"id\":\"evil\"}]}");
                        var bad = IndexTrust.Verify(tampered, sigJson);
                        return good.Ok && !bad.Ok;
                    }
                    finally
                    {
                        try { System.IO.File.Delete(keyFile); } catch { }
                    }
                }
            });
            Probe("权限.宽松(内置)", () =>
                SwapHost.Addons.HasPermission("swap.download-mc", "hooks"));
            Probe("权限.严格判定", () =>
            {
                var record = new AddonManager.AddonRecord
                {
                    Id = "selftest.perms",
                    Manifest = new AddonManifest { Permissions = new List<string> { "hooks" } }
                };
                return SwapHost.Addons.IsStrict(record) &&
                       SwapHost.Addons.HasPermission(record, "hooks") &&
                       !SwapHost.Addons.HasPermission(record, "pages");
            });
            Probe("权限.Scoped 拦截", () =>
            {
                var record = new AddonManager.AddonRecord
                {
                    Id = "selftest.perms.scoped",
                    Manifest = new AddonManifest { Permissions = new List<string> { "pages" } }
                };
                var scoped = SwapHost.Addons.CreateScopedApi(record);
                try
                {
                    scoped.Hooks.Attach("PCL.ModBase", "ToUnixTimestampUtc", "selftest.perm",
                        (Func<DateTime, long>)(_ => 1L), HookMode.Replace);
                    return "NOT BLOCKED";
                }
                catch (UnauthorizedAccessException)
                {
                    return "blocked";
                }
                finally
                {
                    try
                    {
                        var probeDir = System.IO.Path.Combine(
                            SwapPaths.AddonsDirectory, "selftest.perms.scoped");
                        if (System.IO.Directory.Exists(probeDir))
                            System.IO.Directory.Delete(probeDir, true);
                    }
                    catch { }
                }
            });
            Probe("权限.别名归一化", () =>
            {
                var record = new AddonManager.AddonRecord
                {
                    Id = "selftest.perms.alias",
                    Manifest = new AddonManifest
                    {
                        Permissions = new List<string> { "host.hooks.attach", "host.ui.pages" }
                    }
                };
                return SwapHost.Addons.HasPermission(record, "hooks") &&
                       SwapHost.Addons.HasPermission(record, "pages") &&
                       !SwapHost.Addons.HasPermission(record, "secret");
            });
            if (Environment.GetEnvironmentVariable("PCL2SWAP_SELFTEST_MORE") == "1")
            {
                Probe("Ui.RegisterMorePage(selftest)", () =>
                {
                    api.Ui.RegisterMorePage("selftest.more", "Swap Selftest",
                        () => api.Ui.Card("Swap Selftest",
                            api.Ui.Text("Registered through IUi.RegisterMorePage (API 1.2).")));
                    return "registered";
                });
            }

            Probe("Surface.HostMvid", () => api.Surface.HostMvid);
            Probe("Surface.TypeCount", () => api.Surface.TypeCount);
            Probe("Surface.FindMethod(ModSecret.UpdateStart)",
                () => api.Surface.FindMethod("PCL.ModSecret", "UpdateStart", 2)?.Token);

            Probe("Reflect.HostType(PCL.ModMain)", () => api.Reflect.HostType("PCL.ModMain") != null);
            Probe("Reflect.Call(ModBase.GetUuid)",
                () => api.Reflect.Call(api.Reflect.HostType("PCL.ModBase"), "GetUuid"));

            Probe("Pcl.Secret.HasUpdatePermission", () => api.Pcl.Secret.HasUpdatePermission);
            Probe("Pcl.Animation.ControlEnabled", () => api.Pcl.Animation.ControlEnabled);
            // A string-typed key: the host's generic Get<string> casts the stored value, so a
            // numeric setting (UiTheme) would throw InvalidCastException inside the trampoline.
            Probe("Pcl.Settings.Get(LaunchArgumentTitle)", () => api.Pcl.Settings.Get("LaunchArgumentTitle"));
            Probe("Pcl.Settings.GetDefault(LaunchArgumentTitle)",
                () => api.Pcl.Settings.GetDefault("LaunchArgumentTitle"));
            Probe("Pcl.Base.GetUuid", () => api.Pcl.Base.GetUuid());

            Probe("Pcl.Instance.FolderSelected", () => api.Pcl.Instance.FolderSelected);
            Probe("Pcl.Instance.GetFriendlyName",
                () => api.Pcl.Instance.GetFriendlyName(api.Pcl.Instance.FolderSelected));
            Probe("Pcl.Instance.List", () =>
            {
                var list = api.Pcl.Instance.List();
                if (list.Count > 0)
                {
                    SwapLog.Info($"              ↳ 首个实例：{api.Pcl.Instance.GetName(list[0])} " +
                                 $"{api.Pcl.Instance.GetVersion(list[0])} · " +
                                 $"可装 Mod={(api.Pcl.Instance.IsModable(list[0]) ? "是" : "否")}");
                }
                return list;
            });

            Probe("Pcl.Download.PreferMojang", () => api.Pcl.Download.PreferMojang);
            Probe("Pcl.Download.SourceModGet",
                () => api.Pcl.Download.SourceModGet("sodium"));
            Probe("Pcl.Loader.TaskbarProgress", () => api.Pcl.Loader.TaskbarProgress);
            Probe("Pcl.Loader.ListAll", () => api.Pcl.Loader.ListAll(false));
            Probe("Pcl.Loader.Watch(real loader)", () => RunLoaderWatchProbe(api));
            Probe("Pcl.Loader.Combo(empty)", () => RunLoaderComboProbe(api));
            Probe("Pcl.Launch.LoginName", () => api.Pcl.Launch.LoginName());
            Probe("Pcl.Launch.LoginAble", () => api.Pcl.Launch.LoginAble());

            Probe("Data.Get(selftest)", () => api.Data?.Get("selftest"));

            // Drive the real update pipeline once so the event bus is proven on the host itself:
            // the hook publishes UpdateRequested before any gate, and test mode cancels the silent
            // update without touching the network.
            if (Dialogs.TestMode)
            {
                Probe("EventBus.UpdateRequested（驱动一次静默 UpdateStart）", () =>
                {
                    api.Pcl.Secret.UpdateStart(true, null);
                    return "raised";
                });

                if (SwapHost.Addons?.Find("test.sample") != null)
                    Probe("ScopedHook（模块钩子跟随开关）", () => RunScopedHookProbe(api));
            }

            try
            {
                foreach (var record in SwapHost.Addons?.Records ?? new List<AddonManager.AddonRecord>())
                    SwapLog.Info($"[selftest] 模块 {record.Id}：{(record.Enabled ? "启用" : "禁用")}（{record.Status}）");
            }
            catch (Exception ex)
            {
                SwapLog.Warn("[selftest] 模块清单读取失败：" + ex.Message);
            }

            SwapLog.Info($"[selftest] 完成：ok={succeeded} fail={failures.Count}" +
                         (failures.Count == 0 ? string.Empty : "（" + string.Join(", ", failures) + "）"));
        }

        private static string Describe(object value)
        {
            if (value == null) return "(null)";
            if (value is string text) return text.Length > 120 ? text.Substring(0, 120) + "…" : text;
            if (value is IEnumerable items)
            {
                var count = items.Cast<object>().Count();
                return "count=" + count;
            }
            return Convert.ToString(value);
        }

        /// <summary>
        /// Real-host proof of the loader event bridge: subscribe to a loader the launcher currently
        /// shows on its taskbar and log whatever the host's own events deliver. The probe returns
        /// immediately (the launcher dispatches state changes through its UI thread, which is busy
        /// running this self-test), so the callbacks land in the log right afterwards.
        /// </summary>
        private static string RunLoaderWatchProbe(ISwapApi api)
        {
            var loaders = api.Pcl.Loader.ListAll(false);
            if (loaders.Count == 0) return "宿主任务栏为空（跳过）";

            var loader = loaders[0];
            var name = api.Pcl.Loader.GetName(loader);
            var weight = api.Pcl.Loader.GetProgressWeight(loader);

            HeldWatches.Add(api.Pcl.Loader.Watch(loader,
                value => SwapLog.Info($"[selftest] loader 进度事件：{name} = {value:0.###}"),
                state => SwapLog.Info($"[selftest] loader 状态事件：{name} → {state}")));
            HeldWatches.Add(api.Pcl.Loader.WatchPreviewFinish(loader,
                _ => SwapLog.Info($"[selftest] loader PreviewFinish 事件：{name}")));

            return $"{name}（权重 {weight:0.##}）：已订阅状态/进度/PreviewFinish（事件异步写入日志）";
        }

        /// <summary>
        /// Deterministic real-host proof of the loader bridge: an empty LoaderCombo is the platform's
        /// own object (nothing to reparent, no host side effects) and starting it drives the real
        /// Waiting → Loading → Finished sequence plus PreviewFinish through the host's own events.
        /// </summary>
        private static string RunLoaderComboProbe(ISwapApi api)
        {
            var combo = api.Pcl.Loader.CreateCombo("PCL2Swap 自检组合", new List<object>(), typeof(object));
            var name = api.Pcl.Loader.GetName(combo);
            HeldWatches.Add(api.Pcl.Loader.Watch(combo,
                value => SwapLog.Info($"[selftest] loader 进度事件：{name} = {value:0.###}"),
                state => SwapLog.Info($"[selftest] loader 状态事件：{name} → {state}")));
            HeldWatches.Add(api.Pcl.Loader.WatchPreviewFinish(combo,
                _ => SwapLog.Info($"[selftest] loader PreviewFinish 事件：{name}")));

            api.Pcl.Loader.Start(combo);
            var tasks = api.Pcl.Loader.ListTasks(combo, false);
            return $"{combo.GetType().Name} 已启动（任务 {tasks.Count} 个），状态/进度/PreviewFinish 事件异步写入日志";
        }

        /// <summary>
        /// Read-only check of the module scope evaluator against the real host version: no
        /// constraints and "*" must accept everything, an impossible range must reject.
        /// </summary>
        private static string RunScopeProbe(ISwapApi api)
        {
            var version = api.Host.PclVersion;
            var none = AddonManager.HostVersionMatches(new List<string>(), version);
            var wildcard = AddonManager.HostVersionMatches(new List<string> { "*" }, version);
            var impossible = AddonManager.HostVersionMatches(new List<string> { ">=99.0" }, version);
            var exact = AddonManager.HostVersionMatches(new List<string> { version }, version);
            return $"宿主 {version}：空约束={none}，*= {wildcard}，精确匹配={exact}，\">=99.0\"={impossible}";
        }

        /// <summary>
        /// End-to-end proof of module-scoped hooks on the real host: attach to a pure ModBase helper
        /// (value-type parameter, the ABI case the gate has to keep), watch the handler run, disable
        /// the module and watch the gate forward to the original, re-enable, then dispose the token.
        /// Only runs in test mode, and only when the sample addon is installed.
        /// </summary>
        private static string RunScopedHookProbe(ISwapApi api)
        {
            var host = api as HostApi;
            if (host == null) return "API 不是 HostApi（跳过）";

            var target = api.Reflect.HostType("PCL.ModBase");
            var method = target?.GetMethod("ToUnixTimestampUtc",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null) return "宿主缺少 ModBase.ToUnixTimestampUtc（跳过）";

            var hits = 0;
            Func<DateTime, long> handler = stamp =>
            {
                hits++;
                return 42L;
            };

            using (host.AttachAddonHook("test.sample", "PCL.ModBase", "ToUnixTimestampUtc",
                       "selftest.scoped", HookMode.Replace, handler))
            {
                var enabled = Convert.ToInt64(method.Invoke(null, new object[] { DateTime.UtcNow }));
                SwapHost.Addons.SetEnabled("test.sample", false);
                var disabled = Convert.ToInt64(method.Invoke(null, new object[] { DateTime.UtcNow }));
                SwapHost.Addons.SetEnabled("test.sample", true);
                var reEnabled = Convert.ToInt64(method.Invoke(null, new object[] { DateTime.UtcNow }));

                return $"enabled={(enabled == 42 ? "handler" : "original")}，" +
                       $"disabled={(disabled == 42 ? "handler" : "original")}，" +
                       $"reEnabled={(reEnabled == 42 ? "handler" : "original")}，handler 命中 {hits} 次";
            }
        }

        /// <summary>Switches to English and back, proving the dictionary and switch are wired.</summary>
        private static object RunI18nProbe(ISwapApi api)
        {
            var original = api.I18n.Language;
            try
            {
                api.I18n.SetLanguage(SwapLanguage.English);
                var english = api.I18n.T("宿主信息");
                api.I18n.SetLanguage(SwapLanguage.Chinese);
                var chinese = api.I18n.T("宿主信息");
                if (english != "Host information")
                    throw new InvalidOperationException("EN = " + english);
                if (chinese != "宿主信息")
                    throw new InvalidOperationException("ZH = " + chinese);
                return $"EN→{english}；ZH→{chinese}";
            }
            finally
            {
                api.I18n.SetLanguage(original);
            }
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
