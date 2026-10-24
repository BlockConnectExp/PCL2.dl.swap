using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace PclDlSwap.Payload.Sandbox
{
    /// <summary>
    /// Stages a PCL-cloud payload, scans it with every configured engine, and only then publishes
    /// it to the real destination. The staged file always carries the Mark-of-the-Web so the shell
    /// keeps treating it as downloaded content.
    /// </summary>
    public sealed class DownloadSandbox
    {
        private readonly SwapConfig _config;
        private readonly ScannerSet _scanners;

        public DownloadSandbox(SwapConfig config)
        {
            _config = config;
            _scanners = new ScannerSet(config);
        }

        public sealed class Result
        {
            public bool Success { get; set; }
            public bool Blocked { get; set; }
            public string StagedPath { get; set; }
            public string Summary { get; set; }
        }

        public Result FetchAndPublish(string[] urls, string destinationPath)
        {
            var sandboxRoot = _config.ResolvedSandboxDirectory;
            Directory.CreateDirectory(sandboxRoot);
            var stagedPath = Path.Combine(sandboxRoot, Guid.NewGuid().ToString("N") + Path.GetExtension(destinationPath));

            try
            {
                Download(urls, stagedPath);
            }
            catch (Exception ex)
            {
                return new Result { Success = false, Blocked = false, StagedPath = stagedPath, Summary = "下载失败：" + ex.Message };
            }

            var verdicts = _scanners.Scan(stagedPath);
            bool clean = _scanners.AllClean(verdicts, out var summary);
            if (!clean && !_config.AllowDegradedCommit)
                return new Result { Success = false, Blocked = true, StagedPath = stagedPath, Summary = summary };

            try
            {
                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                if (File.Exists(destinationPath)) File.Delete(destinationPath);
                File.Copy(stagedPath, destinationPath, true);
                ZoneIdentifier.Stamp(destinationPath);
                if (!_config.KeepSandboxFiles)
                {
                    try { File.Delete(stagedPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                return new Result { Success = false, Blocked = false, StagedPath = stagedPath, Summary = "落盘失败：" + ex.Message };
            }

            return new Result { Success = true, Blocked = false, StagedPath = stagedPath, Summary = summary };
        }

        private static void Download(string[] urls, string stagedPath)
        {
            Exception last = null;
            foreach (var url in urls)
            {
                try
                {
                    using (var client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", "PCL2/DLLSwap");
                        client.DownloadFile(url, stagedPath);
                    }
                    ZoneIdentifier.Stamp(stagedPath);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }
            throw last ?? new IOException("没有可用下载链接");
        }
    }

    public static class ZoneIdentifier
    {
        private const int GENERIC_WRITE = 0x40000000;
        private const int CREATE_ALWAYS = 2;
        private const int FILE_ATTRIBUTE_NORMAL = 0x80;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(string lpFileName, int dwDesiredAccess, int dwShareMode,
            IntPtr lpSecurityAttributes, int dwCreationDisposition, int dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        public static void Stamp(string path)
        {
            try
            {
                var stream = path + ":Zone.Identifier";
                var handle = CreateFileW(stream, GENERIC_WRITE, 0, IntPtr.Zero, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
                if (handle == new IntPtr(-1)) return;
                try
                {
                    var payload = Encoding.ASCII.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\n");
                    WriteFile(handle, payload, payload.Length, out _, IntPtr.Zero);
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            catch
            {
                // Mark-of-the-Web is defence in depth; a failure must not break the download path.
            }
        }
    }
}



// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover