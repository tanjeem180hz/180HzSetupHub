using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SetupHub180Hz.Services
{
    public static class ThemeService
    {
        private static readonly Dictionary<string, (string DarkKey, string LightKey)> ThemeColorMap = new()
        {
            { "BrushBackground", ("ColorBackground", "ColorBackgroundLight") },
            { "BrushSurface", ("ColorSurface", "ColorSurfaceLight") },
            { "BrushSurfaceHover", ("ColorSurfaceHover", "ColorSurfaceHoverLight") },
            { "BrushBorder", ("ColorBorder", "ColorBorderLight") },
            { "BrushTextPrimary", ("ColorTextPrimary", "ColorTextPrimaryLight") },
            { "BrushTextSecondary", ("ColorTextSecondary", "ColorTextSecondaryLight") }
        };

        public static void ApplyTheme(bool dark)
        {
            if (Application.Current == null) return;

            foreach (var (brushKey, (darkKey, lightKey)) in ThemeColorMap)
            {
                var colorKey = dark ? darkKey : lightKey;
                if (Application.Current.TryFindResource(brushKey) is SolidColorBrush brush &&
                    Application.Current.TryFindResource(colorKey) is Color targetColor)
                {
                    if (brush.IsFrozen)
                    {
                        Application.Current.Resources[brushKey] = new SolidColorBrush(targetColor);
                    }
                    else
                    {
                        brush.Color = targetColor;
                    }
                }
            }

            SettingsService.Instance.Current.DarkTheme = dark;
            SettingsService.Instance.Save();
        }
    }
}
