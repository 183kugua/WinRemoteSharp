using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfImage = System.Windows.Controls.Image;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfApplication = System.Windows.Application;
using WpfCursors = System.Windows.Input.Cursors;
using WpfPoint = System.Windows.Point;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace WinRemoteSharp
{
    /// <summary>
    /// 自定义输入对话框 - 无系统标题栏、圆角、自定义图标、主题配色、动画效果
    /// </summary>
    public class InputDialog : Window
    {
        private WpfTextBox _textBox;
        private PasswordBox _passwordBox;
        private bool _isPassword;
        private Border _mainBorder;

        public string InputText { get; private set; } = "";

        public InputDialog(string title, string prompt, string defaultValue = "", bool isPassword = false)
        {
            _isPassword = isPassword;

            // 窗口基础设置 - 无边框、圆角、居中、置顶
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = WpfBrushes.Transparent;
            Width = 440;
            Height = 220;
            MinWidth = 400;
            MinHeight = 200;
            MaxWidth = 500;
            MaxHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;

            // 设置自定义图标
            try
            {
                var iconUri = new Uri("pack://application:,,,/Resources/App.ico");
                var bitmap = new BitmapImage(iconUri);
                Icon = bitmap;
            }
            catch { }

            // 主容器 - 带阴影的圆角边框
            _mainBorder = new Border
            {
                Background = GetSafeBrush("BgLightBrush", WpfBrushes.White),
                CornerRadius = new CornerRadius(16),
                BorderBrush = GetSafeBrush("CardBorderBrush", new SolidColorBrush(Colors.LightGray)),
                BorderThickness = new Thickness(1.5),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 270,
                    ShadowDepth = 4,
                    BlurRadius = 20,
                    Opacity = 0.15
                },
                Margin = new Thickness(8) // 留空间给阴影
            };

            var grid = new Grid { Margin = new Thickness(24) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // 图标+标题
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // 提示文字
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 输入框
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // 按钮区

            // === 第0行：图标 + 标题 ===
            var headerPanel = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            
            // 自定义图标
            var iconImage = new WpfImage
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = WpfVerticalAlignment.Center
            };
            try
            {
                var iconUri = new Uri("pack://application:,,,/Resources/DialogIcon.png");
                iconImage.Source = new BitmapImage(iconUri);
            }
            catch { }
            headerPanel.Children.Add(iconImage);

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetSafeBrush("TextPrimaryBrush", WpfBrushes.Black),
                VerticalAlignment = WpfVerticalAlignment.Center
            };
            headerPanel.Children.Add(titleBlock);
            Grid.SetRow(headerPanel, 0);
            grid.Children.Add(headerPanel);

            // === 第1行：提示文字 ===
            var promptBlock = new TextBlock
            {
                Text = prompt,
                Foreground = GetSafeBrush("TextPrimaryBrush", WpfBrushes.Black),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16),
                LineHeight = 20
            };
            Grid.SetRow(promptBlock, 1);
            grid.Children.Add(promptBlock);

            // === 第2行：输入框 ===
            var inputContainer = new Border
            {
                Background = GetSafeBrush("BgCardBrush", WpfBrushes.White),
                CornerRadius = new CornerRadius(8),
                BorderBrush = GetSafeBrush("BorderLightBrush", new SolidColorBrush(Colors.LightGray)),
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(0)
            };

            if (_isPassword)
            {
                _passwordBox = new PasswordBox
                {
                    Password = defaultValue,
                    Padding = new Thickness(14, 10, 14, 10),
                    BorderThickness = new Thickness(0),
                    Background = WpfBrushes.Transparent,
                    Foreground = GetSafeBrush("TextPrimaryBrush", WpfBrushes.Black),
                    FontSize = 14,
                    VerticalContentAlignment = WpfVerticalAlignment.Center,
                    CaretBrush = GetSafeBrush("MintMainBrush", WpfBrushes.Teal),
                };
                _passwordBox.Focus();
                inputContainer.Child = _passwordBox;
            }
            else
            {
                _textBox = new WpfTextBox
                {
                    Text = defaultValue,
                    Padding = new Thickness(14, 10, 14, 10),
                    BorderThickness = new Thickness(0),
                    Background = WpfBrushes.Transparent,
                    Foreground = GetSafeBrush("TextPrimaryBrush", WpfBrushes.Black),
                    FontSize = 14,
                    VerticalContentAlignment = WpfVerticalAlignment.Center,
                    CaretBrush = GetSafeBrush("MintMainBrush", WpfBrushes.Teal),
                };
                _textBox.Focus();
                _textBox.SelectAll();
                inputContainer.Child = _textBox;
            }

            Grid.SetRow(inputContainer, 2);
            grid.Children.Add(inputContainer);

            // === 第3行：按钮区 ===
            var buttonPanel = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            // 取消按钮
            var cancelButton = CreateButton("取消", false);
            cancelButton.Click += (s, e) => { DialogResult = false; CloseWithAnimation(); };
            buttonPanel.Children.Add(cancelButton);

            // 确定按钮
            var okButton = CreateButton("确定", true);
            okButton.Click += (s, e) => { Confirm(); };
            okButton.Margin = new Thickness(10, 0, 0, 0);
            buttonPanel.Children.Add(okButton);

            Grid.SetRow(buttonPanel, 3);
            grid.Children.Add(buttonPanel);

            _mainBorder.Child = grid;
            Content = _mainBorder;

            // 入场动画
            Loaded += (s, e) => PlayEntryAnimation();
            
            // 支持拖拽移动窗口
            MouseLeftButtonDown += (s, e) => 
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };

            // ESC 关闭
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                    CloseWithAnimation();
                else if (e.Key == Key.Enter && !_isPassword)
                    Confirm();
            };
        }

        private WpfButton CreateButton(string content, bool isPrimary)
        {
            var brushKey = isPrimary ? "MintMainBrush" : "TextSecondaryBrush";
            var hoverBrushKey = isPrimary ? "MintDeepBrush" : "TextMutedBrush";

            var btn = new WpfButton
            {
                Content = content,
                Padding = new Thickness(24, 10, 24, 10),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetSafeBrush("TextLightBrush", WpfBrushes.White),
                Background = GetSafeBrush(brushKey, WpfBrushes.Teal),
                BorderThickness = new Thickness(0),
                Cursor = WpfCursors.Hand,
                MinWidth = 88
            };

            // 使用 Style + ControlTemplate 代替旧的 FrameworkElementFactory
            var template = new ControlTemplate(typeof(WpfButton));
            var borderFactory = new FrameworkElementFactory(typeof(Border), "Bd");
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(WpfButton.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(WpfButton.PaddingProperty));
            borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);
            
            var presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            presenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, WpfHorizontalAlignment.Center);
            presenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, WpfVerticalAlignment.Center);
            presenterFactory.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            borderFactory.AppendChild(presenterFactory);
            
            template.VisualTree = borderFactory;

            // 触发器
            // IsMouseOver
            var mouseOverTrigger = new Trigger { Property = WpfButton.IsMouseOverProperty, Value = true };
            mouseOverTrigger.Setters.Add(new Setter(WpfButton.BackgroundProperty, GetSafeBrush(hoverBrushKey, WpfBrushes.DarkGreen)));
            template.Triggers.Add(mouseOverTrigger);

            // IsPressed
            var pressedTrigger = new Trigger { Property = WpfButton.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(WpfButton.OpacityProperty, 0.9));
            var pressStoryboard = new Storyboard();
            var scaleXAnim = new DoubleAnimation(0.95, new Duration(TimeSpan.FromMilliseconds(80)));
            Storyboard.SetTargetProperty(scaleXAnim, new PropertyPath("RenderTransform.(ScaleTransform.ScaleX)"));
            var scaleYAnim = new DoubleAnimation(0.95, new Duration(TimeSpan.FromMilliseconds(80)));
            Storyboard.SetTargetProperty(scaleYAnim, new PropertyPath("RenderTransform.(ScaleTransform.ScaleY)"));
            pressStoryboard.Children.Add(scaleXAnim);
            pressStoryboard.Children.Add(scaleYAnim);
            pressedTrigger.EnterActions.Add(new BeginStoryboard { Storyboard = pressStoryboard });
            template.Triggers.Add(pressedTrigger);

            // IsEnabled
            var disabledTrigger = new Trigger { Property = WpfButton.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(WpfButton.OpacityProperty, 0.4));
            template.Triggers.Add(disabledTrigger);

            btn.Template = template;
            btn.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
            btn.RenderTransform = new ScaleTransform(1, 1);

            return btn;
        }

        private void PlayEntryAnimation()
        {
            // 初始状态：透明 + 向下偏移 + 缩小
            Opacity = 0;
            _mainBorder.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
            _mainBorder.RenderTransform = new TransformGroup
            {
                Children = new TransformCollection
                {
                    new ScaleTransform(0.9, 0.9),
                    new TranslateTransform(0, 20)
                }
            };

            var storyboard = new Storyboard();
            
            // 淡入
            var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeIn, this);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(Window.OpacityProperty));
            storyboard.Children.Add(fadeIn);

            // 缩放
            var scaleX = new DoubleAnimation(0.9, 1.0, new Duration(TimeSpan.FromMilliseconds(250)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(scaleX, _mainBorder);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("RenderTransform.Children[0].(ScaleTransform.ScaleX)"));
            storyboard.Children.Add(scaleX);

            var scaleY = new DoubleAnimation(0.9, 1.0, new Duration(TimeSpan.FromMilliseconds(250)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(scaleY, _mainBorder);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("RenderTransform.Children[0].(ScaleTransform.ScaleY)"));
            storyboard.Children.Add(scaleY);

            // 上移
            var translateY = new DoubleAnimation(20, 0, new Duration(TimeSpan.FromMilliseconds(250)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(translateY, _mainBorder);
            Storyboard.SetTargetProperty(translateY, new PropertyPath("RenderTransform.Children[1].(TranslateTransform.Y)"));
            storyboard.Children.Add(translateY);

            storyboard.Begin();
        }

        private void CloseWithAnimation()
        {
            var storyboard = new Storyboard();
            
            // 淡出
            var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(150)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(fadeOut, this);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(Window.OpacityProperty));
            storyboard.Children.Add(fadeOut);

            // 缩小
            var scaleX = new DoubleAnimation(1.0, 0.95, new Duration(TimeSpan.FromMilliseconds(150)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(scaleX, _mainBorder);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("RenderTransform.Children[0].(ScaleTransform.ScaleX)"));
            storyboard.Children.Add(scaleX);

            var scaleY = new DoubleAnimation(1.0, 0.95, new Duration(TimeSpan.FromMilliseconds(150)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(scaleY, _mainBorder);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("RenderTransform.Children[0].(ScaleTransform.ScaleY)"));
            storyboard.Children.Add(scaleY);

            // 下移
            var translateY = new DoubleAnimation(0, 15, new Duration(TimeSpan.FromMilliseconds(150)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(translateY, _mainBorder);
            Storyboard.SetTargetProperty(translateY, new PropertyPath("RenderTransform.Children[1].(TranslateTransform.Y)"));
            storyboard.Children.Add(translateY);

            storyboard.Completed += (s, e) => Close();
            storyboard.Begin();
        }

        private static SolidColorBrush GetSafeBrush(string resourceKey, SolidColorBrush fallback)
        {
            try
            {
                if (WpfApplication.Current?.Resources[resourceKey] is SolidColorBrush brush)
                    return brush;
            }
            catch { }
            return fallback;
        }

        private void Confirm()
        {
            if (_isPassword)
                InputText = _passwordBox?.Password ?? "";
            else
                InputText = _textBox?.Text ?? "";
            
            DialogResult = true;
            CloseWithAnimation();
        }

        public static string Show(Window owner, string title, string prompt, string defaultValue = "", bool isPassword = false)
        {
            var dlg = new InputDialog(title, prompt, defaultValue, isPassword);
            if (owner != null)
            {
                dlg.Owner = owner;
                // 确保在所有者窗口上方
                dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            bool? result = dlg.ShowDialog();
            if (result == true)
                return dlg.InputText;
            return null;
        }
    }
}
