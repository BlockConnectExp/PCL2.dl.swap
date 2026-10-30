using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Pcl2Swap.Injector.Models;

namespace Pcl2Swap.Injector.Services;

/// <summary>
/// Finds injectable PCL2 instances: running processes for "inject into what I already have",
/// plus manual file selection and the launch-and-inject path.
/// </summary>
public static class TargetProbe
{
    private static readonly string[] CandidateProcessNames =
    {
        "Plain Craft Launcher 2",
        "Plain Craft Launcher",
        "Pcl2SwapE2E",
        "Pcl2SwapV"
    };

    public static List<int> FindCandidateProcessIds()
    {
        var found = new List<int>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                foreach (var name in CandidateProcessNames)
                {
                    if (string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(process.Id);
                        break;
                    }
                }
            }
            catch
            {
                // process vanished while enumerating
            }
            finally
            {
                process.Dispose();
            }
        }

        return found;
    }

    /// <summary>
    /// Reads a process image path without loading it, then fingerprints the file on disk.
    /// A running single-file PCL locks nothing we need, so plain file reads are fine.
    /// </summary>
    public static TargetInfo DescribeProcess(int pid)
    {
        var info = new TargetInfo { Pid = pid };
        try
        {
            using var process = Process.GetProcessById(pid);
            info.ExePath = process.MainModule?.FileName ?? "";
        }
        catch (Exception ex)
        {
            info.ProbeError = ex.Message;
        }

        if (!string.IsNullOrEmpty(info.ExePath) && File.Exists(info.ExePath))
            Fingerprint(info);
        else if (info.ProbeError == null)
            info.ProbeError = "无法读取进程主模块路径";

        return info;
    }

    public static TargetInfo DescribeFile(string path)
    {
        var info = new TargetInfo { ExePath = path };
        if (!File.Exists(path))
        {
            info.ProbeError = "文件不存在";
            return info;
        }

        Fingerprint(info);
        return info;
    }

    private static void Fingerprint(TargetInfo info)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(info.ExePath);
            info.FileVersion = version.FileVersion;
            info.ProductVersion = version.ProductVersion;
        }
        catch (Exception ex)
        {
            info.ProbeError = "版本信息读取失败：" + ex.Message;
        }

        try
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(info.ExePath);
            info.Sha256 = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            info.ProbeError = "哈希计算失败：" + ex.Message;
        }

        try
        {
            info.Mvid = ReadMvid(info.ExePath);
        }
        catch (Exception ex)
        {
            info.Mvid = "";
            info.ProbeError ??= "MVID 读取失败：" + ex.Message;
        }

        info.Match = OffsetManifest.Match(info.Mvid, info.Sha256);
    }

    /// <summary>Reads the managed module version id straight from the CLI metadata.</summary>
    public static string ReadMvid(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        if (!reader.HasMetadata) throw new InvalidOperationException("不是托管程序集");
        var metadata = reader.GetMetadataReader();
        var module = metadata.GetModuleDefinition();
        return metadata.GetGuid(module.Mvid).ToString("D").ToLowerInvariant();
    }
}
