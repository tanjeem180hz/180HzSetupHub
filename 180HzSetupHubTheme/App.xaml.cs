using System;
using System.Windows;
using System.Windows.Threading;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Don't let one bad Task/UI exception crash the whole app —
            // log it and keep the shell alive.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            ActivityLogger.Instance.Log("Application started.", ActivityType.Info);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ActivityLogger.Instance.Log($"Unexpected error: {e.Exception.Message}", ActivityType.Error);
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                ActivityLogger.Instance.Log($"Fatal error: {ex.Message}", ActivityType.Error);
        }
    }
}
