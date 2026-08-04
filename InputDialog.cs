using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinRemoteSharp
{
    public class InputDialog : Window
    {
        private TextBox _textBox;
        private PasswordBox _passwordBox;
        private bool _isPassword;

        public string InputText { get; private set; } = "";

        public InputDialog(string title, string prompt, string defaultValue = "", bool isPassword = false)
        {
            _isPassword = isPassword;
            Title = title;
            Width = 420;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = (SolidColorBrush)Application.Current.Resources["BgLightBrush"];

            var grid = new Grid
            {
                Margin = new Thickness(20)
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = prompt,
                Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"],
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 13
            };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            if (_isPassword)
            {
                _passwordBox = new PasswordBox
                {
                    Password = defaultValue,
                    Margin = new Thickness(0, 0, 0, 12),
                    Padding = new Thickness(10, 6, 10, 6),
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderLightBrush"],
                    BorderThickness = new Thickness(1.5),
                    Background = (SolidColorBrush)Application.Current.Resources["BgCardBrush"],
                    Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"]
                };
                Grid.SetRow(_passwordBox, 1);
                grid.Children.Add(_passwordBox);
            }
            else
            {
                _textBox = new TextBox
                {
                    Text = defaultValue,
                    Margin = new Thickness(0, 0, 0, 12),
                    Padding = new Thickness(10, 6, 10, 6),
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderLightBrush"],
                    BorderThickness = new Thickness(1.5),
                    Background = (SolidColorBrush)Application.Current.Resources["BgCardBrush"],
                    Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"],
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(_textBox, 1);
                grid.Children.Add(_textBox);
            }

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okButton = new Button
            {
                Content = "确定",
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(4),
                Background = (SolidColorBrush)Application.Current.Resources["MintMainBrush"],
                Foreground = (SolidColorBrush)Application.Current.Resources["TextLightBrush"],
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            okButton.Click += (s, e) => { Confirm(); };

            var cancelButton = new Button
            {
                Content = "取消",
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(4),
                Background = (SolidColorBrush)Application.Current.Resources["TextSecondaryBrush"],
                Foreground = (SolidColorBrush)Application.Current.Resources["TextLightBrush"],
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }

        private void Confirm()
        {
            if (_isPassword)
                InputText = _passwordBox.Password;
            else
                InputText = _textBox.Text;
            DialogResult = true;
            Close();
        }

        public static string Show(Window owner, string title, string prompt, string defaultValue = "", bool isPassword = false)
        {
            var dlg = new InputDialog(title, prompt, defaultValue, isPassword);
            if (owner != null)
                dlg.Owner = owner;
            bool? result = dlg.ShowDialog();
            if (result == true)
                return dlg.InputText;
            return null;
        }
    }
}
