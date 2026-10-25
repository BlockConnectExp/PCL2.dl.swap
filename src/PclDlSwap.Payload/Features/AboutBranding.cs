using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using PclDlSwap.Payload.Hook;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Features
{
    /// <summary>
    /// Adds a "PCL2 Swap By BlockConnect" row right below the "Plain Craft Launcher 2" entry on
    /// 更多 → 关于与鸣谢 → 关于, carrying the swap's own version and the BlockConnect organisation
    /// icon (the high-resolution GitHub avatar shipped inside this payload).
    ///
    /// The row is injected after the page's constructor has built its visual tree, so it lands in
    /// the same stack panel as PCL's own entries and inherits PCL's list styling.
    /// </summary>
    internal static class AboutBranding
    {
        private const string IconResourceName = "PclDlSwap.Payload.Resources.pcl2swap-org-icon.png";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CtorDelegate(IntPtr self);

        private static CtorDelegate _hook;
        private static CtorDelegate _original;
        private static string _iconPath;
        private static bool _inserted;

        public static bool Installed { get; private set; }

        public static void Install(Assembly host)
        {
            try
            {
                var type = host.GetType("PCL.PageOtherAbout", throwOnError: false);
                var ctor = type?.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(c => c.GetParameters().Length == 0);
                if (ctor == null)
                {
                    SwapLog.Warn("AboutBranding：未找到 PageOtherAbout 构造函数。");
                    return;
                }

                _hook = OnCtor;
                var detour = DetourInstaller.Install(ctor, _hook);
                _original = detour.Original<CtorDelegate>();
                Installed = true;
                SwapLog.Info("关于页品牌项已安装：" + detour.Verify());
            }
            catch (Exception ex)
            {
                SwapLog.Error("AboutBranding 安装失败：" + ex);
            }
        }

        private static void OnCtor(IntPtr selfPtr)
        {
            if (_original != null)
                _original(selfPtr);

            try
            {
                if (_inserted) return;

                var self = ObjectPointer.ToObject(selfPtr);
                if (self == null) return;

                var host = Assembly.GetEntryAssembly();
                var listItemType = host?.GetType("PCL.MyListItem", throwOnError: false);
                if (listItemType == null)
                {
                    SwapLog.Warn("AboutBranding：宿主没有 MyListItem，跳过。");
                    return;
                }

                var panMain = PclUi.GetField(self, "PanMain");
                var itemAboutPcl = PclUi.GetField(self, "ItemAboutPcl");
                if (panMain == null || itemAboutPcl == null) return;

                var children = panMain.GetType().GetProperty("Children", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(panMain, null);
                if (children == null) return;

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
                if (indexOf == null || insert == null) return;

                var index = (int)indexOf.Invoke(children, new[] { itemAboutPcl });
                insert.Invoke(children, new object[] { index + 1, item });
                _inserted = true;
                SwapLog.Info("关于页已插入 “PCL2 Swap By BlockConnect” 条目（索引 " + (index + 1) + "）。");
            }
            catch (Exception ex)
            {
                SwapLog.Error("AboutBranding 插入条目失败：" + ex);
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

                    var directory = Path.Combine(Path.GetTempPath(), "PCL2Swap", SwapLog.ToolVersion);
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
