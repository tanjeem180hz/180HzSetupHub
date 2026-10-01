using System;
using System.Windows;

namespace SetupHub180Hz.Views
{
    public static class ThemedMessageBox
    {
        public static MessageBoxResult Show(
            string message,
            string title = "180Hz Setup Hub",
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.Information)
        {
            return Show(null, message, title, button, icon);
        }

        public static MessageBoxResult Show(
            Window? owner,
            string message,
            string title = "180Hz Setup Hub",
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.Information)
        {
            if (Application.Current == null)
            {
                return MessageBox.Show(message, title, button, icon);
            }

            if (!Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(() => Show(owner, message, title, button, icon));
            }

            Window? targetOwner = owner ?? Application.Current.MainWindow;
            if (targetOwner != null && (!targetOwner.IsLoaded || !targetOwner.IsVisible))
            {
                targetOwner = null;
            }

            try
            {
                var dialog = new ThemedMessageBoxWindow(message, title, button, icon);
                if (targetOwner != null)
                {
                    dialog.Owner = targetOwner;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                else
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                dialog.ShowDialog();
                return dialog.Result;
            }
            catch
            {
                return MessageBox.Show(message, title, button, icon);
            }
        }
    }
}
