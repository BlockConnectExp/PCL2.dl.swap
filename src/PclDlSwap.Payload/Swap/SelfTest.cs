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
            Probe("Host.Shape", () => api.Host.Shape);
            Probe("Host.ProcessId", () => api.Host.ProcessId);
            Probe("Host.AddonDirectory", () => api.Host.AddonDirectory);
            Probe("Host.LogDirectory", () => api.Host.LogDirectory);

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
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
