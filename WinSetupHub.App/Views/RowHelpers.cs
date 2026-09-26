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
                string? url = null;

                // 1. Try winget show manifest
                if (!string.IsNullOrWhiteSpace(app.Id))
                {
                    var meta = await winget.GetMetadataAsync(app.Id);
                    url = meta?.BestLink;
                }

                // 2. Fall back to preset WebUrl if winget had no metadata
                if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(app.WebUrl))
                {
                    url = app.WebUrl;
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
    }
}
