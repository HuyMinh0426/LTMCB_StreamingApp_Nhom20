using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ClientApp
{
    public partial class MovieInfoPage : Page
    {
        private MovieInfo _movie;
        private NetworkStream _stream;

        public MovieInfoPage(MovieInfo movie)
        {
            InitializeComponent();

            _movie = movie;
            _stream = Session.Stream;

            // Hiển thị thông tin phim
            txtTitle.Text = movie.Title;
            txtCategory.Text = movie.Category;
            txtDescription.Text = movie.Description;
            LoadPoster(movie.Poster);

            // Tải bình luận và lượt xem
            LoadComments();
            LoadViewCount();
        }

        // Load poster từ đường dẫn file
        private void LoadPoster(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    imgPoster.Source = bitmap;
                }
            }
            catch { }
        }

        // Bấm nút Quay lại về danh sách phim
        private void btnBack_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService != null && NavigationService.CanGoBack)
                NavigationService.GoBack();
        }

        // Bấm nút Xem phim -> chuyển sang trang xem
        private void btnWatch_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.Navigate(new MovieDetailPage(_movie));
        }

        // Tải danh sách bình luận từ server qua TCP
        private void LoadComments()
        {
            try
            {
                SendCommand($"GET_COMMENTS|{_movie.Id}");
                var comments = new List<CommentItem>();
                string line = ReadLine();
                if (line != null && line.StartsWith("COMMENTS_START"))
                {
                    while (true)
                    {
                        line = ReadLine();
                        if (line == null || line == "COMMENTS_END") break;
                        string[] parts = line.Split('|');
                        if (parts.Length >= 3)
                            comments.Add(new CommentItem
                            {
                                Username = parts[0],
                                Content = parts[1],
                                Time = parts[2]
                            });
                    }
                }
                comments.Reverse();
                icComments.ItemsSource = comments;
            }
            catch { }
        }

        // Gửi bình luận lên server qua TCP
        private void btnSendComment_Click(object sender, RoutedEventArgs e)
        {
            string content = txtComment.Text.Trim();
            if (string.IsNullOrEmpty(content)) return;

            try
            {
                SendCommand($"ADD_COMMENT|{_movie.Id}|{Session.Username}|{content}");
                string response = ReadLine();
                if (response != null && response.StartsWith("COMMENT_OK"))
                {
                    txtComment.Text = "";
                    LoadComments();
                }
            }
            catch { }
        }

        // Nhấn Enter trong ô bình luận cũng gửi
        private void txtComment_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                btnSendComment_Click(null, null);
        }

        // Báo cáo một bình luận
        private void btnReport_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var comment = btn?.Tag as CommentItem;
            if (comment == null) return;

            // Không cho báo cáo bình luận của chính mình
            if (comment.Username == Session.Username)
            {
                MessageBox.Show("Bạn không thể báo cáo bình luận của chính mình.");
                return;
            }

            var reasons = new[] { "Bạo lực ngôn ngữ", "Spam", "Nội dung không phù hợp", "Quấy rối" };

            var win = new Window
            {
                Title = "Báo cáo bình luận",
                Width = 380,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(30, 30, 30)),
                ResizeMode = ResizeMode.NoResize
            };

            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = $"Báo cáo bình luận của: {comment.Username}",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"\"{comment.Content}\"",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(150, 150, 150)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var combo = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 12),
                Height = 32,
                FontSize = 13
            };
            foreach (var r in reasons) combo.Items.Add(r);
            combo.SelectedIndex = 0;
            panel.Children.Add(combo);

            var btnSend = new Button
            {
                Content = "Gửi báo cáo",
                Height = 36,
                FontSize = 13,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(229, 9, 20)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnSend.Click += (s, ev) =>
            {
                string reason = combo.SelectedItem?.ToString() ?? "Không rõ";
                try
                {
                    SendCommand($"REPORT|{Session.Username}|{comment.Username}|{_movie.Id}|{_movie.Title}|{comment.Content}|{reason}");
                    string resp = ReadLine();
                    if (resp == "REPORT_OK")
                        MessageBox.Show("Đã gửi báo cáo thành công!");
                }
                catch { }
                win.Close();
            };
            panel.Children.Add(btnSend);
            win.Content = panel;
            win.ShowDialog();
        }

        // Lấy tổng lượt xem của phim
        private void LoadViewCount()
        {
            try
            {
                SendCommand($"GET_VIEW_COUNT|{_movie.Id}");
                string resp = ReadLine();
                if (resp != null && resp.StartsWith("VIEW_COUNT|"))
                {
                    int count = int.Parse(resp.Split('|')[1]);
                    Dispatcher.Invoke(() =>
                        txtTotalViews.Text = count.ToString("N0") + " lượt");
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                    txtTotalViews.Text = "Lỗi: " + ex.Message);
            }
        }

        // Gửi lệnh lên server
        private void SendCommand(string command)
        {
            byte[] data = Encoding.UTF8.GetBytes(command + "\n");
            _stream.Write(data, 0, data.Length);
        }

        // Đọc 1 dòng từ server (kết thúc bằng \n)
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