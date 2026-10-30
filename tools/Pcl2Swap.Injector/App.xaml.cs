using Microsoft.UI.Xaml;

namespace Pcl2Swap.Injector;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs();
        if (commandLine.Length > 1 &&
            string.Equals(commandLine[1], "--cli", StringComparison.OrdinalIgnoreCase))
        {
            var exitCode = await Cli.RunAsync(commandLine.Skip(2).ToArray());
            Environment.Exit(exitCode);
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }
}
