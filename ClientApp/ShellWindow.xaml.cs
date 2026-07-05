using System;
using System.Windows;

namespace ClientApp
{
    public partial class ShellWindow : Window
    {
        public ShellWindow()
        {
            InitializeComponent();
            MainFrame.Navigate(new LoginPage());  
        }

        private void btnMin_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void btnMax_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized
                             ? WindowState.Normal : WindowState.Maximized;

        private void btnClose_Click(object sender, RoutedEventArgs e)
            => Application.Current.Shutdown();
        private void MainFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            if (e.Content is System.Windows.UIElement el)
            {
                el.Opacity = 0;
                var anim = new System.Windows.Media.Animation.DoubleAnimation(
                    0, 1, new Duration(TimeSpan.FromMilliseconds(250)));
                el.BeginAnimation(System.Windows.UIElement.OpacityProperty, anim);
            }
        }
    }
}