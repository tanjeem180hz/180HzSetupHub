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
        public static Button BuildOfficialLinkButton(WingetService winget, AppItem app)
        {
            var btn = new Button
            {
                Content = "🔗 Official Site",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(10, 4, 10, 4),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsEnabled = false,
                ToolTip = "Checking official site link…"
            };

            if (Application.Current?.TryFindResource("OutlineButton") is Style outlineStyle)
            {
                btn.Style = outlineStyle;
            }

            _ = Task.Run(async () =>
            {
                // 1. Check if WebUrl is already known (preset catalog or registry)
                string? url = app.WebUrl;

                // 2. If missing, query winget show manifest asynchronously
                if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(app.Id))
                {
                    var meta = await winget.GetMetadataAsync(app.Id);
                    url = meta?.BestLink;
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        app.WebUrl = url;
                        if (string.IsNullOrWhiteSpace(app.IconUrl))
                        {
                            app.IconUrl = IconCacheService.DeriveFaviconUrl(url) ?? "";
                        }
                    }
                }

                await btn.Dispatcher.InvokeAsync(() =>
                {
                    if (!string.IsNullOrWhiteSpace(url))
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
                                MessageBox.Show($"Could not open browser: {ex.Message}", "Browser Error",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        };
                    }
                    else
                    {
                        btn.IsEnabled = false;
                        btn.ToolTip = "No official link available for this package";
                    }
                });
            });

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
                ToolTip = "Checking official site link…"
            };

            if (Application.Current?.TryFindResource("OutlineButton") is Style outlineStyle)
            {
                btn.Style = outlineStyle;
            }

            _ = Task.Run(async () =>
            {
                string? url = item.WebUrl;

                await btn.Dispatcher.InvokeAsync(() =>
                {
                    if (!string.IsNullOrWhiteSpace(url))
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
                                MessageBox.Show($"Could not open browser: {ex.Message}", "Browser Error",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        };
                    }
                    else
                    {
                        btn.IsEnabled = false;
                        btn.ToolTip = "No official website link identified for this startup program";
                    }
                });
            });

            return btn;
        }
    }
}
