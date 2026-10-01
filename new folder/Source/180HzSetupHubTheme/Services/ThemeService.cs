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
            { "BrushAccent", ("ColorAccent", "ColorAccentLight") },
            { "BrushAccentHover", ("ColorAccentHover", "ColorAccentHoverLight") },
            { "BrushAccentPressed", ("ColorAccentPressed", "ColorAccentPressedLight") },
            { "BrushTextPrimary", ("ColorTextPrimary", "ColorTextPrimaryLight") },
            { "BrushTextSecondary", ("ColorTextSecondary", "ColorTextSecondaryLight") },
            { "BrushAccentForeground", ("ColorAccentForeground", "ColorAccentForegroundLight") }
        };

        public static event Action<bool>? ThemeChanged;

        public static void ApplyTheme(bool dark)
        {
            if (Application.Current == null) return;

            foreach (var (brushKey, (darkKey, lightKey)) in ThemeColorMap)
            {
                var colorKey = dark ? darkKey : lightKey;
                if (Application.Current.TryFindResource(colorKey) is Color targetColor)
                {
                    var newBrush = new SolidColorBrush(targetColor);
                    newBrush.Freeze();
                    Application.Current.Resources[brushKey] = newBrush;

                    foreach (var dict in Application.Current.Resources.MergedDictionaries)
                    {
                        if (dict.Contains(brushKey))
                        {
                            dict[brushKey] = newBrush;
                        }
                    }
                }
            }

            SettingsService.Instance.Current.DarkTheme = dark;
            SettingsService.Instance.Save();

            ThemeChanged?.Invoke(dark);
        }
    }
}
