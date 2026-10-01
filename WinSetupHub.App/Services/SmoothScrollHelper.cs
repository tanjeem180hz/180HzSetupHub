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
    /// Provides 180Hz responsive, ultra-fluid physics smooth scrolling animation
    /// across all ScrollViewers, ListBoxes, and scrollable lists throughout the entire application.
    /// Fast, user-friendly CubicEase deceleration with momentum chaining for quick wheel flicks.
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
                bool TryAttach()
                {
                    var childSv = FindChildScrollViewer(fe);
                    if (childSv != null)
                    {
                        SetIsSmoothScrollEnabled(childSv, (bool)e.NewValue);
                        return true;
                    }
                    return false;
                }

                if (!TryAttach())
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

        private static void Sv_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                var state = _states.GetOrCreateValue(sv);
                if (!state.IsVerticalAnimating)
                {
                    state.TargetVerticalOffset = sv.VerticalOffset;
                }
                if (!state.IsHorizontalAnimating)
                {
                    state.TargetHorizontalOffset = sv.HorizontalOffset;
                }
            }
        }

        private static void Sv_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer sv || !GetIsSmoothScrollEnabled(sv)) return;

            bool isHorizontal = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift
                || (sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled && sv.ScrollableWidth > 0 && sv.ScrollableHeight <= 0.001);

            var state = _states.GetOrCreateValue(sv);

            // Default natural scrolling calibrated to Windows OS WheelScrollLines
            double lines = SystemParameters.WheelScrollLines > 0 ? SystemParameters.WheelScrollLines : 3;
            double defaultTravel = lines * 28.0; // Standard ~84px travel per notch

            if (isHorizontal)
            {
                if (sv.ScrollableWidth <= 0) return;

                double current = state.IsHorizontalAnimating ? state.TargetHorizontalOffset : sv.HorizontalOffset;
                double delta = (e.Delta / 120.0) * defaultTravel;
                double target = Math.Clamp(current - delta, 0, sv.ScrollableWidth);
                state.TargetHorizontalOffset = target;

                int animId = ++state.HorizontalAnimId;
                state.IsHorizontalAnimating = true;

                var anim = new DoubleAnimation
                {
                    From = sv.HorizontalOffset,
                    To = target,
                    Duration = TimeSpan.FromMilliseconds(100),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                anim.Completed += (_, _) =>
                {
                    if (state.HorizontalAnimId == animId)
                    {
                        state.IsHorizontalAnimating = false;
                    }
                };

                sv.BeginAnimation(AnimatedHorizontalOffsetProperty, anim);
                e.Handled = true;
            }
            else
            {
                if (sv.ScrollableHeight <= 0) return;

                double current = state.IsVerticalAnimating ? state.TargetVerticalOffset : sv.VerticalOffset;

                // Support both pixel scrolling and item-based virtualized lists
                if (sv.CanContentScroll && VirtualizingPanel.GetScrollUnit(sv) == ScrollUnit.Item)
                {
                    double itemStep = (e.Delta > 0 ? -1.0 : 1.0) * lines;
                    double targetItem = Math.Clamp(current + itemStep, 0, sv.ScrollableHeight);
                    state.TargetVerticalOffset = targetItem;

                    int animIdItem = ++state.VerticalAnimId;
                    state.IsVerticalAnimating = true;

                    var animItem = new DoubleAnimation
                    {
                        From = sv.VerticalOffset,
                        To = targetItem,
                        Duration = TimeSpan.FromMilliseconds(100),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    animItem.Completed += (_, _) =>
                    {
                        if (state.VerticalAnimId == animIdItem)
                        {
                            state.IsVerticalAnimating = false;
                        }
                    };

                    sv.BeginAnimation(AnimatedVerticalOffsetProperty, animItem);
                    e.Handled = true;
                    return;
                }

                // Default natural travel distance per notch with clean ease
                double delta = (e.Delta / 120.0) * defaultTravel;
                double target = Math.Clamp(current - delta, 0, sv.ScrollableHeight);
                state.TargetVerticalOffset = target;

                int animId = ++state.VerticalAnimId;
                state.IsVerticalAnimating = true;

                var anim = new DoubleAnimation
                {
                    From = sv.VerticalOffset,
                    To = target,
                    Duration = TimeSpan.FromMilliseconds(100),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                anim.Completed += (_, _) =>
                {
                    if (state.VerticalAnimId == animId)
                    {
                        state.IsVerticalAnimating = false;
                    }
                };

                sv.BeginAnimation(AnimatedVerticalOffsetProperty, anim);
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
