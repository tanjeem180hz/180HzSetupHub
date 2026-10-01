using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SetupHub180Hz.Services
{
    /// <summary>
    /// Provides Windows 11 / Edge standard smooth scroll physics and animation across
    /// all ScrollViewers, ListBoxes, and scrollable panels throughout the entire application.
    /// Snappy 180ms CubicEase deceleration with momentum chaining for rapid wheel flicks.
    /// </summary>
    public static class SmoothScrollHelper
    {
        public static readonly DependencyProperty IsSmoothScrollEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsSmoothScrollEnabled",
                typeof(bool),
                typeof(SmoothScrollHelper),
                new PropertyMetadata(false, OnIsSmoothScrollEnabledChanged));

        public static bool GetIsSmoothScrollEnabled(DependencyObject obj) => (bool)obj.GetValue(IsSmoothScrollEnabledProperty);
        public static void SetIsSmoothScrollEnabled(DependencyObject obj, bool value) => obj.SetValue(IsSmoothScrollEnabledProperty, value);

        public static readonly DependencyProperty AnimatedVerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "AnimatedVerticalOffset",
                typeof(double),
                typeof(SmoothScrollHelper),
                new PropertyMetadata(0.0, OnAnimatedVerticalOffsetChanged));

        public static double GetAnimatedVerticalOffset(DependencyObject obj) => (double)obj.GetValue(AnimatedVerticalOffsetProperty);
        public static void SetAnimatedVerticalOffset(DependencyObject obj, double value) => obj.SetValue(AnimatedVerticalOffsetProperty, value);

        public static readonly DependencyProperty AnimatedHorizontalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "AnimatedHorizontalOffset",
                typeof(double),
                typeof(SmoothScrollHelper),
                new PropertyMetadata(0.0, OnAnimatedHorizontalOffsetChanged));

        public static double GetAnimatedHorizontalOffset(DependencyObject obj) => (double)obj.GetValue(AnimatedHorizontalOffsetProperty);
        public static void SetAnimatedHorizontalOffset(DependencyObject obj, double value) => obj.SetValue(AnimatedHorizontalOffsetProperty, value);

        private class ScrollState
        {
            public double TargetVerticalOffset;
            public double TargetHorizontalOffset;
            public int VerticalAnimId;
            public int HorizontalAnimId;
            public bool IsVerticalAnimating;
            public bool IsHorizontalAnimating;
            public long LastVerticalTimeMs;
            public long LastHorizontalTimeMs;
        }

        private static readonly ConditionalWeakTable<ScrollViewer, ScrollState> _states = new();
        private static readonly Stopwatch _clock = Stopwatch.StartNew();

        private static void OnIsSmoothScrollEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
                sv.PreviewMouseWheel -= Sv_PreviewMouseWheel;
                sv.ScrollChanged -= Sv_ScrollChanged;

                if ((bool)e.NewValue)
                {
                    sv.PreviewMouseWheel += Sv_PreviewMouseWheel;
                    sv.ScrollChanged += Sv_ScrollChanged;
                }
            }
            else if (d is FrameworkElement fe)
            {
                bool isEnabled = (bool)e.NewValue;
                bool TryAttach()
                {
                    var childSv = FindChildScrollViewer(fe);
                    if (childSv != null)
                    {
                        SetIsSmoothScrollEnabled(childSv, isEnabled);
                        return true;
                    }
                    return false;
                }

                if (!TryAttach() && isEnabled)
                {
                    EventHandler? layoutHandler = null;
                    layoutHandler = (_, _) =>
                    {
                        if (TryAttach() && layoutHandler != null)
                        {
                            fe.LayoutUpdated -= layoutHandler;
                        }
                    };
                    fe.LayoutUpdated += layoutHandler;

                    fe.Loaded += (_, _) =>
                    {
                        if (TryAttach() && layoutHandler != null)
                        {
                            fe.LayoutUpdated -= layoutHandler;
                        }
                    };
                }
            }
        }

        public static ScrollViewer? FindChildScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer sv) return sv;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer childSv) return childSv;
                var sub = FindChildScrollViewer(child);
                if (sub != null) return sub;
            }
            return null;
        }

        public static void ScrollToTopImmediate(ScrollViewer sv)
        {
            if (sv == null) return;
            var state = _states.GetOrCreateValue(sv);
            state.IsVerticalAnimating = false;
            state.VerticalAnimId++;
            sv.BeginAnimation(AnimatedVerticalOffsetProperty, null);
            SetAnimatedVerticalOffset(sv, 0);
            state.TargetVerticalOffset = 0;
            sv.ScrollToVerticalOffset(0);
            sv.ScrollToTop();
        }

        public static void ScrollToOffsetImmediate(ScrollViewer sv, double offset)
        {
            if (sv == null) return;
            var state = _states.GetOrCreateValue(sv);
            state.IsVerticalAnimating = false;
            state.VerticalAnimId++;
            sv.BeginAnimation(AnimatedVerticalOffsetProperty, null);
            SetAnimatedVerticalOffset(sv, offset);
            state.TargetVerticalOffset = offset;
            sv.ScrollToVerticalOffset(offset);
        }

        private static void Sv_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                var state = _states.GetOrCreateValue(sv);

                // If user is directly dragging scrollbar thumb or clicking track, cancel active animation immediately
                if (Mouse.LeftButton == MouseButtonState.Pressed)
                {
                    if (state.IsVerticalAnimating)
                    {
                        state.IsVerticalAnimating = false;
                        state.VerticalAnimId++;
                        sv.BeginAnimation(AnimatedVerticalOffsetProperty, null);
                    }
                    if (state.IsHorizontalAnimating)
                    {
                        state.IsHorizontalAnimating = false;
                        state.HorizontalAnimId++;
                        sv.BeginAnimation(AnimatedHorizontalOffsetProperty, null);
                    }
                    state.TargetVerticalOffset = sv.VerticalOffset;
                    SetAnimatedVerticalOffset(sv, sv.VerticalOffset);
                    state.TargetHorizontalOffset = sv.HorizontalOffset;
                    SetAnimatedHorizontalOffset(sv, sv.HorizontalOffset);
                    return;
                }

                if (!state.IsVerticalAnimating)
                {
                    state.TargetVerticalOffset = sv.VerticalOffset;
                    SetAnimatedVerticalOffset(sv, sv.VerticalOffset);
                }
                if (!state.IsHorizontalAnimating)
                {
                    state.TargetHorizontalOffset = sv.HorizontalOffset;
                    SetAnimatedHorizontalOffset(sv, sv.HorizontalOffset);
                }
            }
        }

        private static void Sv_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer sv || !GetIsSmoothScrollEnabled(sv)) return;
            HandleMouseWheel(sv, e, forceHorizontal: false);
        }

        public static void HandleMouseWheel(ScrollViewer sv, MouseWheelEventArgs e, bool forceHorizontal = false)
        {
            if (sv == null || e == null) return;

            bool isHorizontal = forceHorizontal
                || (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift
                || (sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled && sv.ScrollableWidth > 0 && sv.ScrollableHeight <= 0.001);

            var state = _states.GetOrCreateValue(sv);
            long now = _clock.ElapsedMilliseconds;

            // Windows-standard wheel scroll travel
            double lines = SystemParameters.WheelScrollLines > 0 ? SystemParameters.WheelScrollLines : 3;
            double travelPerNotch = lines * 40.0; // ~120px travel per notch (Windows 11 / Edge standard)

            if (isHorizontal)
            {
                if (sv.ScrollableWidth <= 0) return;

                // Momentum chaining for quick flicks
                double speedMultiplier = 1.0;
                long elapsed = now - state.LastHorizontalTimeMs;
                if (elapsed < 140 && elapsed > 0)
                {
                    speedMultiplier = Math.Min(1.8, 1.0 + (140 - elapsed) / 140.0 * 0.8);
                }
                state.LastHorizontalTimeMs = now;

                double currentTarget = state.IsHorizontalAnimating ? state.TargetHorizontalOffset : sv.HorizontalOffset;
                double delta = (e.Delta / 120.0) * travelPerNotch * speedMultiplier;
                double target = Math.Clamp(currentTarget - delta, 0, sv.ScrollableWidth);

                if (Math.Abs(target - sv.HorizontalOffset) < 0.1 && Math.Abs(target - currentTarget) < 0.1) return;

                state.TargetHorizontalOffset = target;
                int animId = ++state.HorizontalAnimId;
                state.IsHorizontalAnimating = true;

                var anim = new DoubleAnimation
                {
                    To = target,
                    Duration = TimeSpan.FromMilliseconds(180),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                anim.Completed += (_, _) =>
                {
                    if (state.HorizontalAnimId == animId)
                    {
                        state.IsHorizontalAnimating = false;
                        sv.BeginAnimation(AnimatedHorizontalOffsetProperty, null);
                        SetAnimatedHorizontalOffset(sv, target);
                        sv.ScrollToHorizontalOffset(target);
                    }
                };

                sv.BeginAnimation(AnimatedHorizontalOffsetProperty, anim, HandoffBehavior.SnapshotAndReplace);
                e.Handled = true;
            }
            else
            {
                if (sv.ScrollableHeight <= 0) return;

                // Momentum chaining for quick flicks
                double speedMultiplier = 1.0;
                long elapsed = now - state.LastVerticalTimeMs;
                if (elapsed < 140 && elapsed > 0)
                {
                    speedMultiplier = Math.Min(1.8, 1.0 + (140 - elapsed) / 140.0 * 0.8);
                }
                state.LastVerticalTimeMs = now;

                double currentTarget = state.IsVerticalAnimating ? state.TargetVerticalOffset : sv.VerticalOffset;
                double delta = (e.Delta / 120.0) * travelPerNotch * speedMultiplier;
                double target = Math.Clamp(currentTarget - delta, 0, sv.ScrollableHeight);

                if (Math.Abs(target - sv.VerticalOffset) < 0.1 && Math.Abs(target - currentTarget) < 0.1) return;

                state.TargetVerticalOffset = target;
                int animId = ++state.VerticalAnimId;
                state.IsVerticalAnimating = true;

                var anim = new DoubleAnimation
                {
                    To = target,
                    Duration = TimeSpan.FromMilliseconds(180),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                anim.Completed += (_, _) =>
                {
                    if (state.VerticalAnimId == animId)
                    {
                        state.IsVerticalAnimating = false;
                        sv.BeginAnimation(AnimatedVerticalOffsetProperty, null);
                        SetAnimatedVerticalOffset(sv, target);
                        sv.ScrollToVerticalOffset(target);
                    }
                };

                sv.BeginAnimation(AnimatedVerticalOffsetProperty, anim, HandoffBehavior.SnapshotAndReplace);
                e.Handled = true;
            }
        }

        private static void OnAnimatedVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv && e.NewValue is double offset)
            {
                sv.ScrollToVerticalOffset(offset);
            }
        }

        private static void OnAnimatedHorizontalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv && e.NewValue is double offset)
            {
                sv.ScrollToHorizontalOffset(offset);
            }
        }
    }
}
