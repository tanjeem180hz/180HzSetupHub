using System;
using Microsoft.Toolkit.Uwp.Notifications;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public static class NotificationService
    {
        public static void Notify(string title, string message)
        {
            try
            {
                if (!SettingsService.Instance.Current.ShowNotifications) return;

                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .Show();
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Could not display toast notification: {ex.Message}", ActivityType.Warning);
            }
        }
    }
}
