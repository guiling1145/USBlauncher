using System.Configuration;
using System.Data;
using System.Windows;

namespace WpfApp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 全局异常处理
        this.DispatcherUnhandledException += (sender, args) =>
        {
            MessageBox.Show($"发生未处理异常:\n{args.Exception.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show($"发生严重错误:\n{ex?.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }
}

