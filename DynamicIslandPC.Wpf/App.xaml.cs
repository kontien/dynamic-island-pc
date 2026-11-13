using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DynamicIslandPC.Wpf.Infrastructure;

namespace DynamicIslandPC.Wpf
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            Log.Info("=== Dynamic Island PC запущен ===");
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("Необработанное исключение UI-потока", e.Exception);
            MessageBox.Show(
                "Ошибка:\n" + e.Exception.Message + "\n\nПодробности в логе:\n%LocalAppData%\\DynamicIslandPC\\logs",
                "Dynamic Island PC", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; // не роняем оверлей из-за одной ошибки
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Log.Error("Необработанное исключение Task", e.Exception);
            e.SetObserved();
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Error("Фатальное исключение домена", e.ExceptionObject as Exception);
        }
    }
}