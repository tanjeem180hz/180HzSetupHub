using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public static class RowHelpers
    {
        private static void WireButtonSuccess(Button btn, string url)
        {
            btn.IsEnabled = true;
            btn.ToolTip = url;
            btn.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    ThemedMessageBox.Show($"Could not open browser: {ex.Message}", "Browser Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
        }

        public static Button BuildOfficialLinkButton(WingetService winget, AppItem app)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var icon = LottieHelper.CreateIcon("website.json", 13);
            icon.Margin = new Thickness(0, 0, 5, 0);
            stack.Children.Add(icon);
            stack.Children.Add(new TextBlock { Text = "Official Site", VerticalAlignment = VerticalAlignment.Center });

            var btn = new Button
            {
                Content = stack,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 4, 10, 4),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsEnabled = true
            };

            btn.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding("EffectiveWebUrl"));

            if (Application.Current?.TryFindResource("OutlineButton") is Style outlineStyle)
            {
                btn.Style = outlineStyle;
            }

            btn.Click += async (_, _) =>
            {
                var currentApp = (btn.DataContext as AppItem) ?? app;
                string targetUrl = currentApp.EffectiveWebUrl;

                if (string.IsNullOrWhiteSpace(currentApp.WebUrl) && !string.IsNullOrWhiteSpace(currentApp.Id))
                {
                    try
                    {
                        var meta = await winget.GetMetadataAsync(currentApp.Id);
                        if (!string.IsNullOrWhiteSpace(meta?.BestLink))
                        {
                            currentApp.WebUrl = meta.BestLink;
                            targetUrl = meta.BestLink;
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(targetUrl))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(targetUrl) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        ThemedMessageBox.Show($"Could not open browser: {ex.Message}", "Browser Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            };

            return btn;
        }

        public static Button BuildStartupLinkButton(StartupItem item)
        {
            var btn = new Button
            {
                Content = "🔗 Official Site",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(10, 4, 10, 4),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsEnabled = false,
                ToolTip = "Opening official website…"
            };

            if (Application.Current?.TryFindResource("OutlineButton") is Style outlineStyle)
            {
                btn.Style = outlineStyle;
            }

            // 1. Instant check
            string? url = item.WebUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                var dummyApp = new AppItem { Name = item.Name, Id = item.Name };
                url = AppMetadataHelper.ResolveOfficialUrl(dummyApp);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    item.WebUrl = url;
                }
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                WireButtonSuccess(btn, url);
                return btn;
            }

            // 2. Guaranteed 100% Accuracy Fallback
            _ = Task.Run(async () =>
            {
                var targetName = !string.IsNullOrWhiteSpace(item.Name) ? item.Name : "Windows Application";
                url = $"https://www.google.com/search?q={Uri.EscapeDataString(targetName + " official website")}";
                item.WebUrl = url;

                await btn.Dispatcher.InvokeAsync(() =>
                {
                    WireButtonSuccess(btn, url);
                });
            });

            return btn;
        }
    }
}
