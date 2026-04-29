using System.Windows;

namespace VrcOscSender;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Hook into the main window closing event as soon as it's created
        MainWindow = new MainWindow();
        MainWindow.Closing += (s, args) =>
        {
            if (MainWindow.DataContext is MainViewModel vm)
                vm.SaveSettings();
        };
        MainWindow.Show();
    }
}
