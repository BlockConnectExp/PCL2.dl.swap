using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PclDlSwap.Payload.Sandbox;

namespace PclDlSwap.Payload.Host
{
    /// <summary>
    /// Watches where PCL stages an OTA payload and refuses to let an unverified file reach the
    /// launcher executable.
    ///
    /// PCL's update flow writes the download to %TEMP%\PCL\&lt;version&gt;.&lt;code&gt;.&lt;ext&gt; first and only later
    /// unpacks/moves it into &lt;launcher&gt;\PCL\Plain Craft Launcher 2.exe. This watchdog intercepts at
    /// the staging step:
    ///
    ///   1. hold the staged file with FILE_SHARE_NONE so the host cannot read or move it,
    ///   2. scan it with every configured engine,
    ///   3. clean -> release the handle and let the original flow continue,
    ///      dirty / unverified -> quarantine it, surface a dialog, and stop the update.
    ///
    /// The exclusive hold is what makes this reliable: the host's own error handling then reports
    /// "更新失败" rather than silently installing an unverified binary.
    /// </summary>
    public sealed class UpdateLandingWatchdog : IDisposable
    {
        private readonly SwapConfig _config;
        private readonly ScannerSet _scanners;
        private readonly List<string> _roots;
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new object();
        private bool _disposed;

        public UpdateLandingWatchdog(SwapConfig config)
        {
            _config = config;
            _scanners = new ScannerSet(config);
            _roots = new List<string> { Path.Combine(Path.GetTempPath(), "PCL") };
            if (!string.IsNullOrWhiteSpace(config.ResolvedSandboxDirectory))
                _roots.Add(config.ResolvedSandboxDirectory);
        }

        public void Start()
        {
            foreach (var root in _roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    var watcher = new FileSystemWatcher(root)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };
                    watcher.Created += OnChanged;
                    watcher.Changed += OnChanged;
                    watcher.Renamed += (sender, args) => Handle(args.FullPath);
                    _watchers.Add(watcher);
                    SwapLog.Info("更新落地看门狗已挂载：" + root);
                }
                catch (Exception ex)
                {
                    SwapLog.Warn("看门狗挂载失败（" + root + "）：" + ex.Message);
                }
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs e) => Handle(e.FullPath);

        private void Handle(string path)
        {
            if (_disposed || string.IsNullOrEmpty(path)) return;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".zip" && extension != ".exe" && extension != ".7z" && extension != ".rar") return;

            lock (_gate)
            {
                if (!_seen.Add(path)) return;
            }

            var worker = new Thread(() => Verify(path)) { IsBackground = true, Name = "PclDlSwap.Watchdog" };
            worker.Start();
        }

        private void Verify(string path)
        {
            FileStream hold = null;
            try
            {
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    try
                    {
                        hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(150);
                    }
                }
                if (hold == null)
                {
                    SwapLog.Warn("看门狗无法锁定更新文件（可能仍在写入）：" + path);
                    return;
                }

                SwapLog.Info("看门狗锁定更新包，开始查毒：" + path);
                var verdicts = _scanners.Scan(path);
                bool clean = _scanners.AllClean(verdicts, out var summary);

                if (clean)
                {
                    SwapLog.Info("更新包通过检测，放行落盘：" + path);
                    return;
                }

                hold.Dispose();
                hold = null;

                var quarantineDirectory = _config.ResolvedSandboxDirectory;
                Directory.CreateDirectory(quarantineDirectory);
                var quarantine = Path.Combine(quarantineDirectory,
                    "quarantine-" + Guid.NewGuid().ToString("N") + Path.GetExtension(path));
                try { File.Move(path, quarantine); }
                catch (Exception moveError) { SwapLog.Warn("隔离移动失败：" + moveError.Message); }

                SwapLog.Error("更新包未通过检测，已阻止更新。报告：" + summary.Replace(Environment.NewLine, " | "));
                Dialogs.OnStaThread(() =>
                {
                    Dialogs.BlockedDownload(path + Environment.NewLine + "（已隔离到：" + quarantine + "）", summary);
                    return 0;
                }, 0);
            }
            catch (Exception ex)
            {
                SwapLog.Error("看门狗处理异常：" + ex);
            }
            finally
            {
                hold?.Dispose();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var watcher in _watchers)
            {
                try { watcher.Dispose(); } catch { }
            }
            _watchers.Clear();
        }
    }
}



// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover