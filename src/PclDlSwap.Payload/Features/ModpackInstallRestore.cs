using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using PclDlSwap.Payload.Hook;
using PclDlSwap.Payload.Host;
using PclDlSwap.Payload.Swap;

namespace PclDlSwap.Payload.Features
{
    /// <summary>
    /// Restores modpack installation on PCL 2.99.x. The vanilla-download restore only replaces
    /// PageDownloadInstall.LoaderInit; modpack entry points use a second gate in ModModpack:
    ///
    ///  * ModpackInstall() no longer opens the file picker.
    ///  * ModpackInstall(string, string, string) still detects CurseForge / Modrinth / HMCL / MMC /
    ///    MCBBS / launcher-pack / plain archives, but throws before dispatching to the unchanged
    ///    InstallPack* implementations.
    ///
    /// Both overloads are detoured behind the existing Download MC module. The string overload
    /// repeats only the small format detector and delegates actual installation back to PCL, so
    /// loader UI, taskbar registration, cancellation, cleanup and pack-specific behaviour remain
    /// native.
    /// </summary>
    internal static class ModpackInstallRestore
    {
        private const string DownloadModuleId = "swap.download-mc";
        private const string GateText = "不再提供 MC 下载功能";

        private static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                                     BindingFlags.Static | BindingFlags.Instance;

        private static DetourInstaller _pickerDetour;
        private static DetourInstaller _fileDetour;
        private static Type _hostType;

        public static bool PickerInstalled { get; private set; }
        public static bool FileInstalled { get; private set; }

        public static void Install(Assembly host)
        {
            try
            {
                _hostType = host?.GetType("PCL.ModModpack", throwOnError: false);
                if (_hostType == null)
                {
                    SwapLog.Warn("整合包恢复：宿主没有 PCL.ModModpack，跳过。");
                    return;
                }

                var methods = _hostType.GetMethods(Flags)
                    .Where(method => method.Name == "ModpackInstall")
                    .ToList();
                var picker = methods.FirstOrDefault(method => method.GetParameters().Length == 0);
                var file = methods.FirstOrDefault(method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length == 3 &&
                           parameters.All(parameter => parameter.ParameterType == typeof(string));
                });

                if (picker == null || file == null)
                {
                    SwapLog.Warn("整合包恢复：未找到两个 ModpackInstall 重载。");
                    return;
                }

                if (!LooksLikeGatedMethod(picker) || !LooksLikeGatedMethod(file))
                {
                    SwapLog.Info("整合包恢复：当前宿主未检测到 ModpackInstall 法规门，保持宿主原行为。");
                    return;
                }

                var missing = RequiredInstallers()
                    .Where(name => ResolveInstaller(name.Item1, name.Item2) == null)
                    .Select(name => name.Item1)
                    .ToList();
                if (missing.Count > 0)
                {
                    SwapLog.Warn("整合包恢复：宿主缺少原生安装器，跳过以避免半恢复：" +
                                 string.Join(", ", missing));
                    return;
                }

                var fileHook = typeof(ModpackInstallRestore).GetMethod(nameof(HookFile),
                    BindingFlags.NonPublic | BindingFlags.Static);
                _fileDetour = DetourInstaller.Install(file, fileHook);
                FileInstalled = true;
                SwapLog.Info("整合包格式分发恢复已安装：" + _fileDetour.Verify());

                var pickerHook = typeof(ModpackInstallRestore).GetMethod(nameof(HookPicker),
                    BindingFlags.NonPublic | BindingFlags.Static);
                _pickerDetour = DetourInstaller.Install(picker, pickerHook);
                PickerInstalled = true;
                SwapLog.Info("整合包文件选择恢复已安装：" + _pickerDetour.Verify());
            }
            catch (Exception ex)
            {
                SwapLog.Error("整合包安装恢复安装失败：" + ex);
            }
        }

        private static IEnumerable<Tuple<string, int>> RequiredInstallers()
        {
            yield return Tuple.Create("InstallPackCurseForge", 5);
            yield return Tuple.Create("InstallPackModrinth", 5);
            yield return Tuple.Create("InstallPackHMCL", 3);
            yield return Tuple.Create("InstallPackMMC", 3);
            yield return Tuple.Create("InstallPackMCBBS", 4);
            yield return Tuple.Create("InstallPackLauncherPack", 3);
            yield return Tuple.Create("InstallPackCompress", 2);
        }

        private static bool LooksLikeGatedMethod(MethodInfo method)
        {
            try
            {
                var il = method.GetMethodBody()?.GetILAsByteArray();
                if (il == null) return false;
                for (var index = 0; index + 4 < il.Length; index++)
                {
                    if (il[index] != 0x72) continue; // ldstr
                    var token = BitConverter.ToInt32(il, index + 1);
                    try
                    {
                        var text = method.Module.ResolveString(token);
                        if (!string.IsNullOrEmpty(text) &&
                            text.IndexOf(GateText, StringComparison.Ordinal) >= 0)
                            return true;
                    }
                    catch
                    {
                        // A non-string metadata token in this position is not a gate marker.
                    }
                }
            }
            catch (Exception ex)
            {
                SwapLog.Warn("整合包恢复：法规门探测失败：" + ex.Message);
            }
            return false;
        }

        private static bool DownloadModuleEnabled()
        {
            if (Dialogs.TestMode &&
                Environment.GetEnvironmentVariable("PCL2SWAP_TEST_MODPACK_DISABLED") == "1")
                return false;
            return SwapHost.AddonEnabled(DownloadModuleId);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HookPicker()
        {
            if (!DownloadModuleEnabled())
            {
                _pickerDetour.InvokeOriginal(null);
                return;
            }

            try
            {
                var selected = Dialogs.TestMode
                    ? Environment.GetEnvironmentVariable("PCL2SWAP_TEST_MODPACK_FILE")
                    : PickFile();
                if (string.IsNullOrWhiteSpace(selected)) return;

                Action install = () =>
                {
                    try
                    {
                        DispatchFile(selected, null, null);
                    }
                    catch (OperationCanceledException)
                    {
                        // PCL treats cancellation as a normal user choice.
                    }
                    catch (Exception ex)
                    {
                        SwapLog.Error("手动安装整合包失败：" + ex);
                    }
                };

                if (Dialogs.TestMode &&
                    Environment.GetEnvironmentVariable("PCL2SWAP_TEST_MODPACK_SYNC") == "1")
                {
                    install();
                    return;
                }

                var worker = new Thread(() => install())
                {
                    IsBackground = true,
                    Name = "PCL2Swap.ModpackInstall"
                };
                worker.Start();
            }
            catch (Exception ex)
            {
                SwapLog.Error("整合包文件选择恢复失败：" + ex);
            }
        }

        private static string PickFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择整合包压缩文件",
                Filter = "整合包文件|*.zip;*.mrpack;*.rar|所有文件|*.*",
                CheckFileExists = true,
                Multiselect = false,
                RestoreDirectory = true
            };
            var modMain = _hostType?.Assembly.GetType("PCL.ModMain", throwOnError: false);
            var owner = PclUi.GetMember(modMain, "FrmMain") as System.Windows.Window;
            var accepted = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            return accepted == true ? dialog.FileName : null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object HookFile(object fileValue, object instanceNameValue, object logoValue)
        {
            if (!DownloadModuleEnabled())
                return _fileDetour.InvokeOriginal(null, fileValue, instanceNameValue, logoValue);

            return DispatchFile(Convert.ToString(fileValue), Convert.ToString(instanceNameValue),
                Convert.ToString(logoValue));
        }

        private static object DispatchFile(string file, string instanceName, string logo)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                PclUi.Hint("请选择有效的整合包文件。", "Red");
                throw new ArgumentException("整合包路径不能为空。", nameof(file));
            }

            var mcFolder = Convert.ToString(PclUi.GetMember(_hostType, "McFolderSelected")) ??
                           string.Empty;
            var target = mcFolder + "versions\\" + instanceName + "\\";
            if (target.IndexOf('!') >= 0 || target.IndexOf(';') >= 0)
            {
                PclUi.Hint("游戏路径中不能含有感叹号或分号：" + target, "Red");
                throw new OperationCanceledException();
            }

            ZipArchive archive = null;
            try
            {
                archive = ZipFile.OpenRead(file);
                var archiveBaseFolder = string.Empty;
                int kind;
                try
                {
                    kind = DetectKind(archive, ref archiveBaseFolder);
                }
                catch (Exception ex)
                {
                    const string message = "打开整合包文件失败，文件可能损坏或为不支持的压缩包格式";
                    SwapLog.Error("整合包恢复：" + message + " | " + file + " | " + ex);
                    PclUi.Hint(message, "Red");
                    throw new InvalidOperationException(message, ex);
                }
                SwapLog.Info("整合包恢复：识别为 " + KindName(kind) + "，转交宿主原生安装器。");
                return InvokeInstaller(kind, file, archive, archiveBaseFolder, instanceName, logo);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException ||
                                       ex is UnauthorizedAccessException)
            {
                var message = string.Equals(Path.GetExtension(file), ".rar",
                    StringComparison.OrdinalIgnoreCase)
                    ? "PCL 无法处理 rar 格式的压缩包，请在解压后重新压缩为 zip 格式再试"
                    : "打开整合包文件失败，文件可能损坏或为不支持的压缩包格式";
                SwapLog.Error("整合包恢复：" + message + " | " + file + " | " + ex.Message);
                PclUi.Hint(message, "Red");
                throw new InvalidOperationException(message, ex);
            }
            finally
            {
                archive?.Dispose();
            }
        }

        private static int DetectKind(ZipArchive archive, ref string archiveBaseFolder)
        {
            if (archive.GetEntry("mcbbs.packmeta") != null) return 3;
            if (archive.GetEntry("mmc-pack.json") != null) return 2;
            if (archive.GetEntry("modrinth.index.json") != null) return 4;

            var manifest = archive.GetEntry("manifest.json");
            if (manifest != null)
                return ManifestHasAddons(manifest) ? 3 : 0;

            if (archive.GetEntry("modpack.json") != null) return 1;
            if (archive.GetEntry("modpack.zip") != null ||
                archive.GetEntry("modpack.mrpack") != null) return 9;

            foreach (var entry in archive.Entries)
            {
                var parts = entry.FullName.Split('/');
                archiveBaseFolder = (parts.Length > 0 ? parts[0] : string.Empty) + "/";
                if (parts.Length != 2) continue;

                switch (parts[1])
                {
                    case "mcbbs.packmeta":
                        return 3;
                    case "mmc-pack.json":
                        return 2;
                    case "modrinth.index.json":
                        return 4;
                    case "manifest.json":
                        if (!EntryHasAddons(entry))
                        {
                            return 0;
                        }
                        archiveBaseFolder = "overrides/";
                        return 3;
                    case "modpack.json":
                        return 1;
                    case "modpack.zip":
                    case "modpack.mrpack":
                        return 9;
                }
            }

            return -1;
        }

        private static bool ManifestHasAddons(ZipArchiveEntry entry)
        {
            return EntryHasAddons(entry);
        }

        private static bool EntryHasAddons(ZipArchiveEntry entry)
        {
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
            {
                var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                    .Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                return json != null && json.ContainsKey("addons");
            }
        }

        private static object InvokeInstaller(int kind, string file, ZipArchive archive,
            string archiveBaseFolder, string instanceName, string logo)
        {
            switch (kind)
            {
                case 0:
                    return InvokeInstaller("InstallPackCurseForge", 5, file, archive,
                        archiveBaseFolder, instanceName, logo);
                case 1:
                    return InvokeInstaller("InstallPackHMCL", 3, file, archive, archiveBaseFolder);
                case 2:
                    return InvokeInstaller("InstallPackMMC", 3, file, archive, archiveBaseFolder);
                case 3:
                    return InvokeInstaller("InstallPackMCBBS", 4, file, archive, archiveBaseFolder,
                        instanceName);
                case 4:
                    return InvokeInstaller("InstallPackModrinth", 5, file, archive,
                        archiveBaseFolder, instanceName, logo);
                case 9:
                    return InvokeInstaller("InstallPackLauncherPack", 3, file, archive,
                        archiveBaseFolder);
                default:
                    return InvokeInstaller("InstallPackCompress", 2, file, archive);
            }
        }

        private static object InvokeInstaller(string name, int parameterCount, params object[] args)
        {
            var method = ResolveInstaller(name, parameterCount);
            if (method == null)
                throw new MissingMethodException(_hostType?.FullName, name);
            try
            {
                return method.Invoke(null, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static MethodInfo ResolveInstaller(string name, int parameterCount)
        {
            return _hostType?.GetMethods(Flags)
                .FirstOrDefault(method => method.Name == name &&
                                          method.GetParameters().Length == parameterCount &&
                                          method.IsStatic);
        }

        private static string KindName(int kind)
        {
            switch (kind)
            {
                case 0: return "CurseForge";
                case 1: return "HMCL";
                case 2: return "MMC";
                case 3: return "MCBBS";
                case 4: return "Modrinth";
                case 9: return "launcher pack";
                default: return "plain archive";
            }
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
