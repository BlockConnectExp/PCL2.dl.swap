using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Features
{
    /// <summary>
    /// Adds a "PCL2 Swap By BlockConnect" row right below the "Plain Craft Launcher 2" entry on
    /// 更多 → 关于与鸣谢 → 关于, carrying the swap's own version and the BlockConnect organisation
    /// icon (the high-resolution GitHub avatar shipped inside this payload).
    ///
    /// The About page is created lazily (FormMain news up ModMain.FrmOtherAbout on the first visit),
    /// and the constructor's JIT prologue is not safe to relocate, so instead of detouring the
    /// constructor this watcher waits for ModMain.FrmOtherAbout to appear and then inserts the row
    /// on PCL's UI thread. The insertion is idempotent and lands within a few hundred milliseconds
    /// of the page first being shown.
    /// </summary>
    internal static class AboutBranding
    {
        private const string IconResourceName = "PclDlSwap.Payload.Resources.pcl2swap-org-icon.png";

        private static readonly object Gate = new object();
        private static string _iconPath;
        private static bool _inserted;
        private static volatile bool _stop;
        private static int _attempts;

        /// <summary>
        /// The page can appear before its visual tree is fully usable; a failed attempt is retried
        /// until this cap instead of giving up on the first try (the Alpha 4 log shows a single
        /// cross-thread failure silenced the row for the whole session).
        /// </summary>
        private const int MaxAttempts = 40;

        public static bool Installed { get; private set; }

        public static void Install(Assembly host)
        {
            if (host == null)
            {
                SwapLog.Warn("AboutBranding：宿主程序集为空，跳过。");
                return;
            }

            var watcher = new Thread(() => Watch(host))
            {
                IsBackground = true,
                Name = "PCL2Swap.AboutBranding"
            };
            watcher.Start();
            Installed = true;
            SwapLog.Info("关于页品牌项已启用（等待关于页首次创建）。");
        }

        private static void Watch(Assembly host)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(30);
            while (!_stop && !_inserted && DateTime.UtcNow < deadline)
            {
                try
                {
                    var modMain = host.GetType("PCL.ModMain", throwOnError: false);
                    var page = PclUi.GetMember(modMain, "FrmOtherAbout");
                    if (page != null) Insert(page);
                }
                catch (Exception ex)
                {
                    SwapLog.Error("AboutBranding 轮询异常：" + ex.Message);
                    _stop = true;
                }

                if (!_inserted && !_stop) Thread.Sleep(300);
            }

            if (_inserted) SwapLog.Info("AboutBranding：品牌项已就位，轮询结束。");
        }

        private static void Insert(object page)
        {
            PclUi.OnUiThread(() =>
            {
                lock (Gate)
                {
                    if (_inserted) return;
                    try
                    {
                        var host = Assembly.GetEntryAssembly();
                        var listItemType = host?.GetType("PCL.MyListItem", throwOnError: false);
                        if (listItemType == null)
                        {
                            NoteAttemptFailure("AboutBranding：宿主没有 MyListItem", new MissingMemberException("PCL.MyListItem"));
                            return;
                        }

                        var panMain = PclUi.GetMember(page, "PanMain");
                        var itemAboutPcl = PclUi.GetMember(page, "ItemAboutPcl");
                        if (panMain == null || itemAboutPcl == null)
                        {
                            NoteAttemptFailure("AboutBranding：关于页上没有 PanMain/ItemAboutPcl",
                                new MissingMemberException("PanMain/ItemAboutPcl"));
                            return;
                        }

                        var children = PclUi.GetMember(panMain, "Children");
                        if (children == null)
                        {
                            NoteAttemptFailure("AboutBranding：PanMain 没有 Children", new MissingMemberException("Children"));
                            return;
                        }

                        var item = Activator.CreateInstance(listItemType);
                        PclUi.SetProperty(item, "Title", "PCL2 Swap By BlockConnect");
                        PclUi.SetProperty(item, "Info", "版本 " + SwapLog.ToolVersion +
                                                        " · 下载恢复、静默更新拦截与云下载查毒沙箱");

                        var icon = EnsureIconFile();
                        if (!string.IsNullOrEmpty(icon))
                            PclUi.SetProperty(item, "Logo", icon);

                        var indexOf = children.GetType().GetMethods()
                            .FirstOrDefault(m => m.Name == "IndexOf" && m.GetParameters().Length == 1);
                        var insert = children.GetType().GetMethods()
                            .FirstOrDefault(m => m.Name == "Insert" && m.GetParameters().Length == 2);
                        if (indexOf == null || insert == null)
                        {
                            NoteAttemptFailure("AboutBranding：Children 集合缺少 IndexOf/Insert",
                                new MissingMemberException("IndexOf/Insert"));
                            return;
                        }

                        var index = (int)indexOf.Invoke(children, new[] { itemAboutPcl });
                        insert.Invoke(children, new object[] { index + 1, item });
                        _inserted = true;
                        SwapLog.Info("关于页已插入 “PCL2 Swap By BlockConnect” 条目（索引 " + (index + 1) + "）。");
                    }
                    catch (Exception ex)
                    {
                        NoteAttemptFailure("AboutBranding 插入条目失败", ex);
                    }
                }
            });
        }

        private static void NoteAttemptFailure(string label, Exception ex)
        {
            var attempts = Interlocked.Increment(ref _attempts);
            if (attempts >= MaxAttempts)
            {
                _stop = true;
                SwapLog.Error($"{label}（第 {attempts} 次仍失败，停止重试）：{ex}");
            }
            else if (attempts == 1)
            {
                SwapLog.Warn($"{label}（第 {attempts} 次，将继续重试）：{ex.Message}");
            }
            else if (attempts % 10 == 0)
            {
                SwapLog.Info($"{label}（第 {attempts} 次失败，继续重试）。");
            }
        }

        /// <summary>Writes the embedded organisation icon next to the payload (once) and returns its path.</summary>
        private static string EnsureIconFile()
        {
            if (!string.IsNullOrEmpty(_iconPath) && File.Exists(_iconPath))
                return _iconPath;

            try
            {
                var assembly = typeof(AboutBranding).Assembly;
                var name = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("pcl2swap-org-icon.png", StringComparison.OrdinalIgnoreCase))
                    ?? IconResourceName;
                using (var stream = assembly.GetManifestResourceStream(name))
                {
                    if (stream == null)
                    {
                        SwapLog.Warn("AboutBranding：内嵌组织图标缺失。");
                        return null;
                    }

                    var directory = Path.Combine(Path.GetTempPath(), "PCL2Swap", SwapLog.ToolVersionCompact);
                    Directory.CreateDirectory(directory);
                    _iconPath = Path.Combine(directory, "pcl2swap-org-icon.png");
                    using (var file = File.Create(_iconPath))
                        stream.CopyTo(file);
                }
                return _iconPath;
            }
            catch (Exception ex)
            {
                SwapLog.Warn("AboutBranding：写出组织图标失败：" + ex.Message);
                return null;
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
