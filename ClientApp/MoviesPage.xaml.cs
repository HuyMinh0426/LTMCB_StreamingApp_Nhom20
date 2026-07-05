using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClientApp
{
    public partial class MoviesPage : Page
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private string _loggedInUser = "";

        private List<MovieInfo> _allMovies = new List<MovieInfo>();
        public ObservableCollection<MovieInfo> DisplayMovies { get; set; } = new();

        public MoviesPage()
        {
            InitializeComponent();

            _client = Session.Client;
            _stream = Session.Stream;
            _loggedInUser = Session.Username;

            txtUser.Text = _loggedInUser;
            icMovies.ItemsSource = DisplayMovies;

            LoadMovies();
            if (Session.UserStatus == "warned")
                warningBanner.Visibility = Visibility.Visible;

            
        }

        private void LoadMovies()
        {
            try
            {
                SendCommand("GET_MOVIES");

                _allMovies.Clear();
                while (true)
                {
                    string line = ReadLine();
                    if (line == null || line == "END_MOVIES") break;
                    if (line == "MOVIES_START") continue;

                    string[] parts = line.Split('|');
                    if (parts.Length >= 5)
                    {
                        _allMovies.Add(new MovieInfo
                        {
                            Id = int.Parse(parts[0]),
                            Title = parts[1],
                            Category = parts[2],
                            Poster = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "posters", parts[3]),
                            Description = parts[4]
                        });
                    }
                }

                BuildRows();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi tải phim: {ex.Message}");
            }
        }

        private void ShowMovies(List<MovieInfo> movies)
        {
            DisplayMovies.Clear();
            foreach (var m in movies)
                DisplayMovies.Add(m);
        }

        private void BuildRows()
        {
            var categories = new[] { "Anime", "Phim Việt", "Phim Hàn", "Chiếu Rạp" };
            var rows = new List<CategoryRow>();
            foreach (var cat in categories)
            {
                var moviesInCat = _allMovies.Where(m => m.Category == cat).ToList();
                if (moviesInCat.Count > 0)
                    rows.Add(new CategoryRow { CategoryName = cat, Movies = moviesInCat });
            }
            icRows.ItemsSource = rows;
        }

        private void ShowRowsMode()
        {
            rowsView.Visibility = Visibility.Visible;
            gridView.Visibility = Visibility.Collapsed;
        }

        private void ShowGridMode(List<MovieInfo> movies)
        {
            ShowMovies(movies);
            rowsView.Visibility = Visibility.Collapsed;
            gridView.Visibility = Visibility.Visible;
        }

        private Button _activeNav = null;

        private void Category_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string category = btn.Tag.ToString();

            HighlightNav(btn);

            if (category == "All")
                ShowRowsMode();
            else
                ShowGridMode(_allMovies.Where(m => m.Category == category).ToList());
        }

        private void ViewAllCategory_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string category = btn.Tag.ToString();

            ShowGridMode(_allMovies.Where(m => m.Category == category).ToList());

            foreach (var navBtn in FindNavButtons())
            {
                if (navBtn.Tag != null && navBtn.Tag.ToString() == category)
                {
                    HighlightNav(navBtn);
                    break;
                }
            }
        }

        private IEnumerable<Button> FindNavButtons()
        {
            var result = new List<Button>();
            CollectButtons(this, result);
            return result.Where(b => b.Style == (Style)FindResource("NavItem"));
        }

        private void CollectButtons(DependencyObject parent, List<Button> list)
        {
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < n; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is Button b) list.Add(b);
                CollectButtons(child, list);
            }
        }

        private void HighlightNav(Button btn)
        {
            if (_activeNav != null)
            {
                _activeNav.Foreground = (System.Windows.Media.Brush)
                    new System.Windows.Media.BrushConverter().ConvertFrom("#AAAAAA");
                SetUnderline(_activeNav, false);
            }
            btn.Foreground = System.Windows.Media.Brushes.White;
            SetUnderline(btn, true);
            _activeNav = btn;
        }

        private void SetUnderline(Button btn, bool visible)
        {
            btn.ApplyTemplate();
            var underline = btn.Template.FindName("underline", btn) as Border;
            if (underline != null)
                underline.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string keyword = txtSearch.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(keyword))
                ShowRowsMode();
            else
                ShowGridMode(_allMovies.Where(m => m.Title.ToLower().Contains(keyword)).ToList());
        }

        private void txtSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            txtPlaceholder.Visibility = Visibility.Collapsed;
        }

        private void txtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtSearch.Text))
                txtPlaceholder.Visibility = Visibility.Visible;
        }

        private void Movie_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            var movie = border.DataContext as MovieInfo;
            if (movie == null) return;

            NavigationService.Navigate(new MovieDetailPage(movie));
        }

        // ===== Menu user =====
        private void UserButton_Click(object sender, MouseButtonEventArgs e)
        {
            userMenu.IsOpen = !userMenu.IsOpen;
        }

        private void MenuInfo_Click(object sender, RoutedEventArgs e)
        {
            userMenu.IsOpen = false;
            MessageBox.Show($"Tài khoản đang đăng nhập: {_loggedInUser}",
                "Thông tin tài khoản", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MenuFavorites_Click(object sender, RoutedEventArgs e)
        {
            userMenu.IsOpen = false;
            MessageBox.Show("Tính năng Hộp phim đang phát triển.");
        }

        private void MenuHistory_Click(object sender, RoutedEventArgs e)
        {
            userMenu.IsOpen = false;
            NavigationService.Navigate(new HistoryPage());
        }

        private void MenuLogout_Click(object sender, RoutedEventArgs e)
        {
            userMenu.IsOpen = false;
            try { Session.Client?.Close(); } catch { }
            Session.Client = null;
            Session.Stream = null;
            Session.Username = null;
            NavigationService.Navigate(new LoginPage());
        }

        // ===== Cuộn dãy ngang =====
        private void RowScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = sender as ScrollViewer;
            if (sv == null) return;

            e.Handled = true;
            var parent = FindParentScrollViewer(sv);
            parent?.ScrollToVerticalOffset(parent.VerticalOffset - e.Delta);
        }

        private ScrollViewer FindParentScrollViewer(DependencyObject child)
        {
            var p = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (p != null && !(p is ScrollViewer && ((ScrollViewer)p).Name == "rowsView"))
                p = System.Windows.Media.VisualTreeHelper.GetParent(p);
            return p as ScrollViewer;
        }

        private void ScrollLeft_Click(object sender, RoutedEventArgs e)
        {
            var sv = FindRowScroll(sender as DependencyObject);
            sv?.ScrollToHorizontalOffset(sv.HorizontalOffset - 360);
        }

        private void ScrollRight_Click(object sender, RoutedEventArgs e)
        {
            var sv = FindRowScroll(sender as DependencyObject);
            sv?.ScrollToHorizontalOffset(sv.HorizontalOffset + 360);
        }

        private ScrollViewer FindRowScroll(DependencyObject btn)
        {
            var grid = System.Windows.Media.VisualTreeHelper.GetParent(btn);
            while (grid != null && !(grid is Grid)) grid = System.Windows.Media.VisualTreeHelper.GetParent(grid);
            if (grid == null) return null;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(grid); i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(grid, i);
                if (c is ScrollViewer sv) return sv;
            }
            return null;
        }

        private void SendCommand(string command)
        {
            byte[] data = Encoding.UTF8.GetBytes(command + "\n");
            _stream.Write(data, 0, data.Length);
        }
       

        private string ReadLine()
        {
            var bytes = new List<byte>();
            int b;
            while ((b = _stream.ReadByte()) != -1)
            {
                if (b == '\n') break;
                if (b == '\r') continue;
                bytes.Add((byte)b);
            }
            if (b == -1 && bytes.Count == 0) return null;
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }
}