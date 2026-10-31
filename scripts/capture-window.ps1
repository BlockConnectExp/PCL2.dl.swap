[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [long]$Handle,
    [Parameter(Mandatory = $true)]
    [string]$Output,
    [switch]$FromScreen
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('WindowCapture' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class WindowCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    public static void Save(IntPtr hWnd, string path)
    {
        RECT rect;
        if (!GetWindowRect(hWnd, out rect))
            throw new InvalidOperationException("GetWindowRect failed.");
        using (var bitmap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top,
                   PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                var hdc = graphics.GetHdc();
                try
                {
                    if (!PrintWindow(hWnd, hdc, 2))
                        throw new InvalidOperationException("PrintWindow failed.");
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }
            var parent = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) System.IO.Directory.CreateDirectory(parent);
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    public static void SaveScreen(IntPtr hWnd, string path)
    {
        RECT rect;
        if (!GetWindowRect(hWnd, out rect))
            throw new InvalidOperationException("GetWindowRect failed.");
        using (var bitmap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top,
                   PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size,
                    CopyPixelOperation.SourceCopy);
            var parent = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) System.IO.Directory.CreateDirectory(parent);
            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
'@
}

$path = [IO.Path]::GetFullPath($Output)
if ($FromScreen) {
    [WindowCapture]::SaveScreen([IntPtr]$Handle, $path)
} else {
    [WindowCapture]::Save([IntPtr]$Handle, $path)
}
Get-Item -LiteralPath $path | Select-Object FullName, Length, LastWriteTime


# GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
