using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SetupHub180Hz.Views
{
    public partial class ThemedMessageBoxWindow : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public ThemedMessageBoxWindow(
            string message,
            string title,
            MessageBoxButton button,
            MessageBoxImage icon)
        {
            InitializeComponent();

            TitleTextBlock.Text = string.IsNullOrWhiteSpace(title) ? "180Hz Setup Hub" : title;
            MessageTextBlock.Text = message;

            ConfigureIcon(icon);
            ConfigureButtons(button);

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Result = button switch
                    {
                        MessageBoxButton.YesNo => MessageBoxResult.No,
                        MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
                        MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
                        _ => MessageBoxResult.OK
                    };
                    Close();
                }
                else if (e.Key == Key.Enter)
                {
                    Result = button switch
                    {
                        MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
                        _ => MessageBoxResult.OK
                    };
                    Close();
                }
            };
        }

        private void ConfigureIcon(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Error:
                    IconGlyph.Text = "✕";
                    IconGlyph.Foreground = (Brush)FindResource("BrushError");
                    IconBadge.BorderBrush = (Brush)FindResource("BrushError");
                    break;
                case MessageBoxImage.Warning:
                    IconGlyph.Text = "⚠️";
                    IconGlyph.Foreground = (Brush)FindResource("BrushWarning");
                    IconBadge.BorderBrush = (Brush)FindResource("BrushWarning");
                    break;
                case MessageBoxImage.Question:
                    IconGlyph.Text = "❓";
                    IconGlyph.Foreground = (Brush)FindResource("BrushAccent");
                    IconBadge.BorderBrush = (Brush)FindResource("BrushAccent");
                    break;
                case MessageBoxImage.Information:
                default:
                    IconGlyph.Text = "⚡";
                    IconGlyph.Foreground = (Brush)FindResource("BrushAccent");
                    IconBadge.BorderBrush = (Brush)FindResource("BrushAccent");
                    break;
            }
        }

        private void ConfigureButtons(MessageBoxButton button)
        {
            Btn1.Visibility = Visibility.Collapsed;
            Btn2.Visibility = Visibility.Collapsed;
            Btn3.Visibility = Visibility.Collapsed;

            switch (button)
            {
                case MessageBoxButton.OK:
                    Btn3.Content = "OK";
                    Btn3.Style = (Style)FindResource("AccentButton");
                    Btn3.Visibility = Visibility.Visible;
                    Btn3.IsDefault = true;
                    break;

                case MessageBoxButton.OKCancel:
                    Btn2.Content = "Cancel";
                    Btn2.Style = (Style)FindResource("OutlineButton");
                    Btn2.Visibility = Visibility.Visible;
                    Btn2.IsCancel = true;

                    Btn3.Content = "OK";
                    Btn3.Style = (Style)FindResource("AccentButton");
                    Btn3.Visibility = Visibility.Visible;
                    Btn3.IsDefault = true;
                    break;

                case MessageBoxButton.YesNo:
                    Btn2.Content = "No";
                    Btn2.Style = (Style)FindResource("OutlineButton");
                    Btn2.Visibility = Visibility.Visible;
                    Btn2.IsCancel = true;

                    Btn3.Content = "Yes";
                    Btn3.Style = (Style)FindResource("AccentButton");
                    Btn3.Visibility = Visibility.Visible;
                    Btn3.IsDefault = true;
                    break;

                case MessageBoxButton.YesNoCancel:
                    Btn1.Content = "Cancel";
                    Btn1.Style = (Style)FindResource("OutlineButton");
                    Btn1.Visibility = Visibility.Visible;
                    Btn1.IsCancel = true;

                    Btn2.Content = "No";
                    Btn2.Style = (Style)FindResource("OutlineButton");
                    Btn2.Visibility = Visibility.Visible;

                    Btn3.Content = "Yes";
                    Btn3.Style = (Style)FindResource("AccentButton");
                    Btn3.Visibility = Visibility.Visible;
                    Btn3.IsDefault = true;
                    break;
            }
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            Close();
        }

        private void Btn1_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            Close();
        }

        private void Btn2_Click(object sender, RoutedEventArgs e)
        {
            if (Btn2.Content?.ToString() == "No")
                Result = MessageBoxResult.No;
            else
                Result = MessageBoxResult.Cancel;
            Close();
        }

        private void Btn3_Click(object sender, RoutedEventArgs e)
        {
            if (Btn3.Content?.ToString() == "Yes")
                Result = MessageBoxResult.Yes;
            else
                Result = MessageBoxResult.OK;
            Close();
        }
    }
}
