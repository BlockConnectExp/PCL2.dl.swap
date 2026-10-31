using System.Security.Cryptography;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pcl2Swap.Injector.Models;
using Pcl2Swap.Injector.Services;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Pcl2Swap.Injector;

public sealed partial class MainWindow : Window
{
    private static readonly string InjectorVersion = "v26.2-Alpha 9";

    private readonly List<TargetInfo> _targets = new();
    private readonly List<ReleaseInfo> _releases = new();
    private readonly List<string> _steps = new();
    private TargetInfo? _selectedTarget;
    private ReleaseInfo? _selectedRelease;
    private bool _busy;
    private bool _loaded;

    public MainWindow()
    {
        InitializeComponent();
        Title = "PCL2 Swap Injector · " + InjectorVersion;

        var hwnd = WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        appWindow.Resize(new SizeInt32(1120, 760));
        PaintTitleBar(appWindow);

        Activated += OnFirstActivated;
    }

    /// <summary>Matches the native caption to the WinUI surface so light/dark themes agree.</summary>
    private static void PaintTitleBar(AppWindow appWindow)
    {
        var titleBar = appWindow.TitleBar;
        var dark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        var background = dark
            ? Windows.UI.Color.FromArgb(255, 39, 39, 39)
            : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        var foreground = dark ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
                              : Windows.UI.Color.FromArgb(255, 0, 0, 0);
        var hover = dark ? Windows.UI.Color.FromArgb(255, 58, 58, 58)
                         : Windows.UI.Color.FromArgb(255, 226, 226, 226);

        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = dark ? Windows.UI.Color.FromArgb(255, 160, 160, 160) : foreground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = dark ? Windows.UI.Color.FromArgb(255, 160, 160, 160) : foreground;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = hover;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        Activated -= OnFirstActivated;
        await RefreshAsync();
    }

    // ------------------------------------------------------------------ refresh

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        SetBusy(true, "正在扫描运行中的 PCL2 …");
        try
        {
            var found = await Task.Run(() =>
            {
                var list = new List<TargetInfo>();
                foreach (var pid in TargetProbe.FindCandidateProcessIds())
                    list.Add(TargetProbe.DescribeProcess(pid));
                return list;
            });

            _targets.Clear();
            _targets.AddRange(found);
            _selectedTarget = null;
            TargetList.ItemsSource = null;
            TargetList.ItemsSource = _targets;
            TargetEmptyHint.Visibility = _targets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_targets.Count > 0) TargetList.SelectedIndex = 0;

            HeaderSubtitle.Text = $"注入器 {InjectorVersion} · 偏移清单 {OffsetManifest.Targets.Count} 个宿主目标";
            SetStatus(_targets.Count > 0
                ? $"检测到 {_targets.Count} 个运行中的 PCL2。"
                : "未检测到运行中的 PCL2，可手动选择可执行文件。");
        }
        finally
        {
            SetBusy(false, null);
        }

        await LoadReleasesAsync();
    }

    private async Task LoadReleasesAsync()
    {
        ReleaseEmptyHint.Text = "正在读取 Release 列表…";
        ReleaseEmptyHint.Visibility = Visibility.Visible;
        try
        {
            var (releases, error) = await ReleaseFeed.LoadAsync();
            _releases.Clear();
            _releases.AddRange(releases);
            ReleaseList.ItemsSource = null;
            ReleaseList.ItemsSource = _releases;

            if (error != null)
            {
                ReleaseEmptyHint.Text = "Release 列表读取失败：" + error +
                                        "\n请确认已安装并登录 GitHub CLI（gh auth login）。";
                ReleaseSourceLabel.Text = "离线（无 Release 列表）";
            }
            else
            {
                ReleaseEmptyHint.Visibility = _releases.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;
                ReleaseEmptyHint.Text = "没有可用的 Release。";
                ReleaseSourceLabel.Text = $"来自 GitHub Releases · {_releases.Count} 个版本";
                if (_releases.Count > 0) ReleaseList.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            ReleaseEmptyHint.Text = "Release 列表读取异常：" + ex.Message;
        }
    }

    // ------------------------------------------------------------------ selection

    private void OnTargetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedTarget = TargetList.SelectedItem as TargetInfo;
        UpdateSelectionSummary();
        UpdateInjectButton();
    }

    private void OnReleaseSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedRelease = ReleaseList.SelectedItem as ReleaseInfo;
        if (_selectedRelease != null)
            MarkdownRenderer.Fill(NotePanel, _selectedRelease.Body, LoadNoteImageAsync);
        UpdateInjectButton();
    }

    private void UpdateSelectionSummary()
    {
        if (_selectedTarget == null)
        {
            SelectedTargetDetail.Text = "";
            return;
        }

        var t = _selectedTarget;
        var sb = new StringBuilder();
        sb.AppendLine(t.ExePath);
        sb.AppendLine($"版本 {t.VersionLabel} · {t.SupportLabel}");
        if (!string.IsNullOrEmpty(t.Mvid)) sb.AppendLine("MVID " + t.Mvid);
        SelectedTargetDetail.Text = sb.ToString().TrimEnd();
    }

    private void UpdateInjectButton()
    {
        if (_busy)
        {
            InjectButton.IsEnabled = false;
            return;
        }

        var hasTarget = _selectedTarget != null;
        var hasRelease = _selectedRelease != null;
        InjectButton.IsEnabled = hasTarget && hasRelease;
        InjectButton.Content = _selectedTarget?.Pid > 0 ? "注入到运行中的进程" : "启动并注入";
    }

    private void SetBusy(bool busy, string? status)
    {
        _busy = busy;
        BusyRing.IsActive = busy;
        RefreshButton.IsEnabled = !busy;
        PickFileButton.IsEnabled = !busy;
        TargetList.IsEnabled = !busy;
        ReleaseList.IsEnabled = !busy;
        if (status != null) SetStatus(status);
        UpdateInjectButton();
    }

    private void SetStatus(string text) => StatusText.Text = text;

    // ------------------------------------------------------------------ file target

    private async void OnPickFileClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".exe");
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var info = await Task.Run(() => TargetProbe.DescribeFile(file.Path));
            var existing = _targets.FindIndex(t => t.Pid == 0 &&
                string.Equals(t.ExePath, info.ExePath, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) _targets.RemoveAt(existing);
            _targets.Insert(0, info);

            TargetList.ItemsSource = null;
            TargetList.ItemsSource = _targets;
            TargetEmptyHint.Visibility = Visibility.Collapsed;
            TargetList.SelectedIndex = 0;
            SetStatus(info.Supported
                ? $"已选择 {Path.GetFileName(info.ExePath)}（{info.Match!.Version}），点击「启动并注入」。"
                : $"已选择 {Path.GetFileName(info.ExePath)}，但它不在偏移清单中，注入结果可能不稳定。");
        }
        catch (Exception ex)
        {
            SetStatus("选择文件失败：" + ex.Message);
        }
    }

    // ------------------------------------------------------------------ injection

    private async void OnInjectClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _selectedTarget == null || _selectedRelease == null) return;
        await RunInjectionAsync(_selectedTarget, _selectedRelease);
    }

    private async Task RunInjectionAsync(TargetInfo target, ReleaseInfo release)
    {
        SetBusy(true, "准备注入…");
        ResultBar.IsOpen = false;
        _steps.Clear();

        var context = new InjectionContext
        {
            InjectorVersion = InjectorVersion,
            Target = target,
            Release = release
        };

        var progress = new Progress<string>(message =>
        {
            SetStatus(message);
            _steps.Add(message);
        });

        try
        {
            if (target.Pid > 0 && !IsProcessAlive(target.Pid))
            {
                SetStatus("目标进程已经退出，请刷新列表后重试。");
                return;
            }

            var (shim, shimError) = await ShimProvider.EnsureAsync(release, progress, CancellationToken.None);
            if (shim == null)
            {
                SetStatus("注入包不可用。");
                await ShowFailureAsync(context, shimError ?? "无法获得对应的注入包。");
                return;
            }

            context.ShimPath = shim;
            InjectionResult result;
            if (target.Pid > 0)
            {
                result = await InjectorEngine.InjectAsync(target.Pid, shim, progress, CancellationToken.None);
            }
            else
            {
                var (pid, launched) = await InjectorEngine.LaunchAndInjectAsync(
                    target.ExePath, shim, progress, CancellationToken.None);
                result = launched;
                if (pid > 0 && result.Success) _steps.Add($"已启动 PID {pid}。");
            }

            context.Result = result;
            if (result.Success)
            {
                SetStatus(result.Detail ?? "注入成功。");
                ResultBar.Severity = InfoBarSeverity.Success;
                ResultBar.Title = "注入成功";
                ResultBar.Message = result.Verified
                    ? "Swap 已确认在目标进程中运行。设置页会出现 Swap Addon 与 Swap Settings。"
                    : "注入已派发；请到 PCL2 的设置页确认 Swap Addon 是否出现。";
                ResultBar.IsOpen = true;

                if (result.Pid > 0)
                {
                    // The process keeps running; drop a dead row so the list stays honest.
                    await Task.Delay(1500);
                }
            }
            else
            {
                SetStatus("注入失败：" + (result.Detail ?? result.Stage));
                await ShowFailureAsync(context, null);
            }
        }
        catch (Exception ex)
        {
            context.Result ??= new InjectionResult { Stage = "Unexpected", Success = false, Detail = ex.ToString() };
            SetStatus("注入流程异常：" + ex.Message);
            await ShowFailureAsync(context, ex.Message);
        }
        finally
        {
            SetBusy(false, null);
            UpdateInjectButton();
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var _ = System.Diagnostics.Process.GetProcessById(pid);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ failure dialog

    private async Task ShowFailureAsync(InjectionContext context, string? extra)
    {
        var summary = new StringBuilder();
        summary.AppendLine(context.Result?.Detail ?? extra ?? "注入没有完成。");
        if (context.Target != null)
        {
            summary.AppendLine();
            summary.AppendLine("目标：" + context.Target.ProcessLabel);
            summary.AppendLine("版本：" + context.Target.VersionLabel);
            summary.AppendLine("匹配：" + context.Target.SupportLabel);
        }

        if (context.Release != null) summary.AppendLine("注入包：" + context.Release.TagName);

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "注入失败",
            Content = new ScrollViewer
            {
                MaxHeight = 320,
                Content = new TextBlock
                {
                    Text = summary.ToString(),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            },
            PrimaryButtonText = "提交 Issue",
            SecondaryButtonText = "复制详情",
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close
        };

        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.Primary)
            await SubmitIssueAsync(context);
        else if (choice == ContentDialogResult.Secondary)
            CopyToClipboard(Diagnostics.BuildTitle(context) + Environment.NewLine +
                            Diagnostics.BuildBody(context));
    }

    private async Task SubmitIssueAsync(InjectionContext context)
    {
        SetStatus("正在通过 gh 提交 Issue …");
        var title = Diagnostics.BuildTitle(context);
        var body = Diagnostics.BuildBody(context);
        var (ok, message) = await Diagnostics.SubmitAsync(title, body);

        ResultBar.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        ResultBar.Title = ok ? "Issue 已提交" : "Issue 提交失败";
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
        SetStatus(ok ? "Issue 已提交，感谢反馈。" : "Issue 提交失败，诊断已保存到本地。");
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            SetStatus("详情已复制到剪贴板。");
        }
        catch (Exception ex)
        {
            SetStatus("复制失败：" + ex.Message);
        }
    }

    // ------------------------------------------------------------------ note images

    private async Task<ImageSource?> LoadNoteImageAsync(string url)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "pcl2swap-note-images");
            Directory.CreateDirectory(dir);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
            var extension = ".img";
            var uri = new Uri(url);
            var nameExtension = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrEmpty(nameExtension)) extension = nameExtension;
            var file = Path.Combine(dir, hash + extension);

            if (!File.Exists(file))
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Pcl2Swap.Injector");
                var bytes = await http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(file, bytes);
            }

            var bitmap = new BitmapImage();
            using var stream = await FileRandomAccessStream.OpenAsync(file, FileAccessMode.Read);
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
