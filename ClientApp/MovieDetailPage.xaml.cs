using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;

namespace ClientApp
{
    public class CommentItem
    {
        public string Username { get; set; }
        public string Content { get; set; }
        public string Time { get; set; }
    }

    public partial class MovieDetailPage : Page
    {
        private MovieInfo _movie;
        private NetworkStream _stream;

        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private System.Windows.Threading.DispatcherTimer _timer;
        private System.Windows.Threading.DispatcherTimer _udpTimer;
        private bool _isDragging = false;
        private bool _isFullscreen = false;
        private bool _ended = false;
        private bool _isPlaying = false;   // cờ: đang phát thì mới gửi UDP
        private bool _chatOpen = false;

        private UdpClient _udpClient;
        private IPEndPoint _serverUdpEndPoint;

        public MovieDetailPage(MovieInfo movie)
        {
            InitializeComponent();

            _movie = movie;
            _stream = Session.Stream;

            txtTitle.Text = movie.Title;
            txtCategory.Text = movie.Category;
            txtDescription.Text = movie.Description;
            LoadPoster(movie.Poster);
            // Khởi tạo UDP client (ép IPv4)
            _serverUdpEndPoint = new IPEndPoint(IPAddress.Loopback, 8889);
            _udpClient = new UdpClient();
            _udpClient.Connect(_serverUdpEndPoint);

            Core.Initialize();
            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);
            videoView.MediaPlayer = _mediaPlayer;

            _mediaPlayer.EndReached += (s, e) =>
                Dispatcher.BeginInvoke(new Action(OnVideoEnded));

            // Timer cập nhật thanh tua
            _timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += Timer_Tick;

            // Timer gửi heartbeat UDP + cập nhật số người xem mỗi 3 giây
            _udpTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _udpTimer.Tick += UdpTimer_Tick;
            _udpTimer.Start();   // bắt đầu hỏi số viewer ngay khi vào trang

            this.Unloaded += MovieDetailPage_Unloaded;

            LoadComments();
            LoadViewCount();
        }

        private void UdpTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // Nếu đang phát thì gửi heartbeat "tôi đang xem phim này"
                if (_isPlaying)
                {
                    byte[] heartbeat = Encoding.UTF8.GetBytes(
                         $"WATCHING|{_movie.Id}|{Session.Username}");
                    _udpClient.Send(heartbeat, heartbeat.Length);
                }

                // Hỏi có bao nhiêu người đang xem phim này
                byte[] query = Encoding.UTF8.GetBytes($"GET_VIEWERS|{_movie.Id}");
                _udpClient.Send(query, query.Length);
                // Nhận phản hồi (timeout 1 giây)
                _udpClient.Client.ReceiveTimeout = 1000;
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] reply = _udpClient.Receive(ref remote);
                string msg = Encoding.UTF8.GetString(reply);

                if (msg.StartsWith("VIEWERS|"))
                {
                    int count = int.Parse(msg.Split('|')[1]);
                    Dispatcher.Invoke(() =>
                    {
                        txtViewers.Text = count > 0 ? $"👁 {count}" : "👁 0";
                    });
                }

                // Lấy tin nhắn chat nếu đang mở panel
                if (_chatOpen)
                {
                    byte[] chatQuery = Encoding.UTF8.GetBytes($"GET_CHAT|{_movie.Id}");
                    _udpClient.Send(chatQuery, chatQuery.Length);

                    _udpClient.Client.ReceiveTimeout = 1000;
                    IPEndPoint remote2 = new IPEndPoint(IPAddress.Any, 0);
                    byte[] chatReply = _udpClient.Receive(ref remote2);
                    string chatMsg = Encoding.UTF8.GetString(chatReply);

                    if (chatMsg.StartsWith("CHATMSG"))
                    {
                        var chatItems = new List<CommentItem>();
                        string[] parts = chatMsg.Split('|');
                        for (int i = 1; i < parts.Length; i++)
                        {
                            int colon = parts[i].IndexOf(':');
                            if (colon > 0)
                                chatItems.Add(new CommentItem
                                {
                                    Username = parts[i].Substring(0, colon),
                                    Content = parts[i].Substring(colon + 1)
                                });
                        }
                        Dispatcher.Invoke(() =>
                        {
                            icChat.ItemsSource = chatItems;
                            chatScroll.ScrollToEnd();
                        });
                    }
                }
            }
            catch (Exception ex)
                {
                    Dispatcher.Invoke(() => txtViewers.Text = "UDP lỗi: " + ex.Message);
                }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            Keyboard.Focus(this);
        }

        private void Page_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                btnPlayPause_Click(null, null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _isFullscreen)
            {
                btnFull_Click(null, null);
                e.Handled = true;
            }
        }

        private void VideoArea_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_mediaPlayer == null) return;
            if (e.ClickCount == 2)
                btnFull_Click(null, null);
            else
                btnPlayPause_Click(null, null);
        }

        private void OnVideoEnded()
        {
            _ended = true;
            _isPlaying = false;
            btnPlayPause.Content = "►";
            seekBar.Value = 0;
            txtCurrent.Text = "0:00";
            txtStatus.Text = "Đã xem xong - bấm phát để xem lại";
        }

        private void btnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_isFullscreen) btnFull_Click(null, null);
            if (NavigationService != null && NavigationService.CanGoBack)
                NavigationService.GoBack();
        }

        private void MovieDetailPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _udpTimer?.Stop();
            _timer?.Stop();
            _udpClient?.Close();
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
        }

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

        private void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            btnPlay.IsEnabled = false;
            txtStatus.Text = "Đang yêu cầu phim từ server...";
            txtVideoPlaceholder.Visibility = Visibility.Collapsed;

            Thread t = new Thread(StreamVideo);
            t.IsBackground = true;
            t.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_mediaPlayer == null || _mediaPlayer.Length <= 0) return;
            if (_isDragging) return;

            seekBar.Value = _mediaPlayer.Position * 1000;
            txtCurrent.Text = FormatTime(_mediaPlayer.Time);
            txtTotal.Text = FormatTime(_mediaPlayer.Length);
        }

        private void btnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;

            if (_ended)
            {
                _ended = false;
                _isPlaying = true;
                _mediaPlayer.Stop();
                _mediaPlayer.Play();
                _mediaPlayer.Time = 0;
                btnPlayPause.Content = "❚❚";
                txtStatus.Text = "Đang phát...";
                return;
            }

            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
                _isPlaying = false;
                btnPlayPause.Content = "►";
            }
            else
            {
                _mediaPlayer.Play();
                _isPlaying = true;
                btnPlayPause.Content = "❚❚";
            }
        }

        private void btnBack10_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer != null)
                _mediaPlayer.Time = Math.Max(0, _mediaPlayer.Time - 10000);
        }

        private void btnFwd10_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer != null && _mediaPlayer.Length > 0)
                _mediaPlayer.Time = Math.Min(_mediaPlayer.Length, _mediaPlayer.Time + 10000);
        }

        private void seekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDragging && _mediaPlayer != null && _mediaPlayer.Length > 0)
                _mediaPlayer.Position = (float)(seekBar.Value / 1000.0);
        }

        private void seekBar_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _isDragging = true;
        }

        private void seekBar_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (_mediaPlayer != null && _mediaPlayer.Length > 0)
                _mediaPlayer.Position = (float)(seekBar.Value / 1000.0);
            _isDragging = false;
        }

        private void volBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mediaPlayer != null)
                _mediaPlayer.Volume = (int)volBar.Value;
            if (btnMute != null)
                btnMute.Content = volBar.Value == 0 ? "🔇" : "🔊";
        }

        private void btnMute_Click(object sender, RoutedEventArgs e)
        {
            if (volBar.Value > 0) volBar.Value = 0;
            else volBar.Value = 100;
        }

        private void btnFull_Click(object sender, RoutedEventArgs e)
        {
            var win = Window.GetWindow(this);
            if (win == null) return;

            _isFullscreen = !_isFullscreen;
            if (_isFullscreen)
            {
                infoColumn.Width = new GridLength(0);
                infoPanel.Visibility = Visibility.Collapsed;
                topBar.Visibility = Visibility.Collapsed;
                win.WindowStyle = WindowStyle.None;
                win.WindowState = WindowState.Maximized;
                btnFull.Content = "🗗";
            }
            else
            {
                infoColumn.Width = new GridLength(320);
                infoPanel.Visibility = Visibility.Visible;
                topBar.Visibility = Visibility.Visible;
                win.WindowState = WindowState.Normal;
                btnFull.Content = "⛶";
            }
        }

        private string FormatTime(long ms)
        {
            var t = TimeSpan.FromMilliseconds(ms);
            return $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        }
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

        private void txtComment_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                btnSendComment_Click(null, null);
        }
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

            // Chọn lý do
            var reasons = new[] { "Bạo lực ngôn ngữ", "Spam", "Nội dung không phù hợp", "Quấy rối" };
            var dialog = new System.Windows.Controls.ComboBox();

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

        // === CHAT LIVE (UDP) ===
        private void btnChat_Click(object sender, RoutedEventArgs e)
        {
            _chatOpen = !_chatOpen;
            if (_chatOpen)
            {
                chatColumn.Width = new GridLength(280);
                chatPanel.Visibility = Visibility.Visible;
            }
            else
            {
                chatColumn.Width = new GridLength(0);
                chatPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void btnSendChat_Click(object sender, RoutedEventArgs e)
        {
            string content = txtChatInput.Text.Trim();
            if (string.IsNullOrEmpty(content)) return;

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(
                    $"CHAT|{_movie.Id}|{Session.Username}|{content}");
                _udpClient.Send(data, data.Length);
                txtChatInput.Text = "";
            }
            catch { }
        }

        private void txtChatInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                btnSendChat_Click(null, null);
        }
        private void StreamVideo()
        {
            try
            {
                SendCommand($"PLAY|{_movie.Id}");

                string line = ReadLine();
                if (line == null) return;

                if (line.StartsWith("ERROR|"))
                {
                    Dispatcher.Invoke(() => txtStatus.Text = "Lỗi: " + line.Split('|')[1]);
                    return;
                }

                if (line.StartsWith("VIDEO|"))
                {
                    long fileSize = long.Parse(line.Split('|')[1]);
                    Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = $"Đang tải video... ({fileSize / 1024} KB)";
                        loadingArea.Visibility = Visibility.Visible;
                        pbLoading.Value = 0;
                        txtPercent.Text = "0%";
                    });

                    string tempPath = Path.Combine(Path.GetTempPath(), $"movie_{_movie.Id}_{DateTime.Now.Ticks}.mp4");
                    using (FileStream fs = File.Create(tempPath))
                    {
                        byte[] chunk = new byte[65536];
                        long received = 0;
                        while (received < fileSize)
                        {
                            int toRead = (int)Math.Min(chunk.Length, fileSize - received);
                            int bytesRead = _stream.Read(chunk, 0, toRead);
                            if (bytesRead == 0) break;
                            fs.Write(chunk, 0, bytesRead);
                            received += bytesRead;

                            int percent = (int)(received * 100 / fileSize);
                            Dispatcher.Invoke(() =>
                            {
                                pbLoading.Value = percent;
                                txtPercent.Text = $"{percent}%";
                            });
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = "Đang phát...";
                        loadingArea.Visibility = Visibility.Collapsed;
                        _ended = false;
                        _isPlaying = true;
                        var media = new Media(_libVLC, new Uri(tempPath));
                        _mediaPlayer.Play(media);
                        controlBar.Visibility = Visibility.Visible;
                        btnPlayPause.Content = "❚❚";
                        _timer.Start();
                        SendCommand($"ADD_HISTORY|{Session.Username}|{_movie.Id}|{_movie.Title}");
                        ReadLine();
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => txtStatus.Text = "Lỗi: " + ex.Message);
            }
        }

        private void SendCommand(string command)
        {
            byte[] data = Encoding.UTF8.GetBytes(command + "\n");
            _stream.Write(data, 0, data.Length);
        }
        private void LoadViewCount()
        {
            try
            {
                SendCommand($"GET_VIEW_COUNT|{_movie.Id}");
                string resp = ReadLine();
                Dispatcher.Invoke(() =>
                    txtTotalViews.Text = resp ?? "null");
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