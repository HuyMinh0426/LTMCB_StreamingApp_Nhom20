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
using LibVLCSharp.Shared;

namespace ClientApp
{
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
        private bool _isPlaying = false;
        private bool _chatOpen = false;

        private UdpClient _udpClient;
        private IPEndPoint _serverUdpEndPoint;

        public MovieDetailPage(MovieInfo movie)
        {
            InitializeComponent();

            _movie = movie;
            _stream = Session.Stream;

            // Hiển thị tên phim ở thanh trên
            txtTitle.Text = movie.Title;

            // Khởi tạo UDP client (ép IPv4)
            _serverUdpEndPoint = new IPEndPoint(IPAddress.Loopback, 8889);
            _udpClient = new UdpClient();
            _udpClient.Connect(_serverUdpEndPoint);

            // Khởi tạo LibVLC
            Core.Initialize();
            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);
            videoView.MediaPlayer = _mediaPlayer;

            _mediaPlayer.EndReached += (s, e) =>
                Dispatcher.BeginInvoke(new Action(OnVideoEnded));

            // Timer cập nhật thanh tua mỗi 500ms
            _timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += Timer_Tick;

            // Timer gửi heartbeat UDP + cập nhật viewer count mỗi 3 giây
            _udpTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _udpTimer.Tick += UdpTimer_Tick;
            _udpTimer.Start();

            this.Unloaded += MovieDetailPage_Unloaded;

            // Tự động bắt đầu tải phim ngay khi mở trang
            StartStreaming();
        }

        // Tự động bắt đầu stream video
        private void StartStreaming()
        {
            txtStatus.Text = "Đang yêu cầu phim từ server...";
            Thread t = new Thread(StreamVideo);
            t.IsBackground = true;
            t.Start();
        }

        // Timer UDP: gửi heartbeat + lấy viewer count + chat
        private void UdpTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (_isPlaying)
                {
                    byte[] heartbeat = Encoding.UTF8.GetBytes(
                         $"WATCHING|{_movie.Id}|{Session.Username}");
                    _udpClient.Send(heartbeat, heartbeat.Length);
                }

                byte[] query = Encoding.UTF8.GetBytes($"GET_VIEWERS|{_movie.Id}");
                _udpClient.Send(query, query.Length);

                _udpClient.Client.ReceiveTimeout = 1000;
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] reply = _udpClient.Receive(ref remote);
                string msg = Encoding.UTF8.GetString(reply);

                if (msg.StartsWith("VIEWERS|"))
                {
                    int count = int.Parse(msg.Split('|')[1]);
                    Dispatcher.Invoke(() =>
                    {
                        txtViewers.Text = count > 0 ? $"👁 {count} đang xem" : "👁 0";
                    });
                }

                // Lấy chat nếu panel đang mở
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
            catch { }
        }

        // Trang load xong -> focus để nhận phím tắt
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            Keyboard.Focus(this);
        }

        // Phím tắt: Space = pause, Esc = thoát fullscreen
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

        // Click chuột vào video: 1 click = pause, 2 click = fullscreen
        private void VideoArea_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_mediaPlayer == null) return;
            if (e.ClickCount == 2)
                btnFull_Click(null, null);
            else
                btnPlayPause_Click(null, null);
        }

        // Xử lý khi video xem hết
        private void OnVideoEnded()
        {
            _ended = true;
            _isPlaying = false;
            btnPlayPause.Content = "►";
            seekBar.Value = 0;
            txtCurrent.Text = "0:00";
        }

        // Nút quay lại về trang thông tin
        private void btnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_isFullscreen) btnFull_Click(null, null);
            if (NavigationService != null && NavigationService.CanGoBack)
                NavigationService.GoBack();
        }

        // Dọn dẹp khi thoát trang
        private void MovieDetailPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _udpTimer?.Stop();
            _timer?.Stop();
            _udpClient?.Close();
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
        }

        // Timer cập nhật thanh tua
        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_mediaPlayer == null || _mediaPlayer.Length <= 0) return;
            if (_isDragging) return;

            seekBar.Value = _mediaPlayer.Position * 1000;
            txtCurrent.Text = FormatTime(_mediaPlayer.Time);
            txtTotal.Text = FormatTime(_mediaPlayer.Length);
        }

        // Nút Play/Pause
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

        // Tua lùi 10 giây
        private void btnBack10_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer != null)
                _mediaPlayer.Time = Math.Max(0, _mediaPlayer.Time - 10000);
        }

        // Tua tới 10 giây
        private void btnFwd10_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer != null && _mediaPlayer.Length > 0)
                _mediaPlayer.Time = Math.Min(_mediaPlayer.Length, _mediaPlayer.Time + 10000);
        }

        // Kéo thanh tua
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

        // Chỉnh volume
        private void volBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mediaPlayer != null)
                _mediaPlayer.Volume = (int)volBar.Value;
            if (btnMute != null)
                btnMute.Content = volBar.Value == 0 ? "🔇" : "🔊";
        }

        // Nút Mute
        private void btnMute_Click(object sender, RoutedEventArgs e)
        {
            if (volBar.Value > 0) volBar.Value = 0;
            else volBar.Value = 100;
        }

        // Nút Fullscreen
        private void btnFull_Click(object sender, RoutedEventArgs e)
        {
            var win = Window.GetWindow(this);
            if (win == null) return;

            _isFullscreen = !_isFullscreen;
            if (_isFullscreen)
            {
                topBar.Visibility = Visibility.Collapsed;
                controlBar.Visibility = Visibility.Collapsed;
                win.WindowStyle = WindowStyle.None;
                win.WindowState = WindowState.Maximized;
                btnFull.Content = "🗗";
            }
            else
            {
                topBar.Visibility = Visibility.Visible;
                controlBar.Visibility = Visibility.Visible;
                win.WindowState = WindowState.Normal;
                btnFull.Content = "⛶";
            }
        }

        // Chuyển ms -> mm:ss
        private string FormatTime(long ms)
        {
            var t = TimeSpan.FromMilliseconds(ms);
            return $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        }

        // Toggle panel chat
        private void btnChat_Click(object sender, RoutedEventArgs e)
        {
            _chatOpen = !_chatOpen;
            if (_chatOpen)
            {
                chatColumn.Width = new GridLength(320);
                chatPanel.Visibility = Visibility.Visible;
            }
            else
            {
                chatColumn.Width = new GridLength(0);
                chatPanel.Visibility = Visibility.Collapsed;
            }
        }

        // Gửi tin nhắn chat qua UDP
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

        // Nhấn Enter cũng gửi chat
        private void txtChatInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                btnSendChat_Click(null, null);
        }

        // Stream video từ server qua TCP
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

                    // Tạo file tạm, tên có Ticks để tránh trùng khi nhiều client cùng tải
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

                    // Tải xong -> phát video
                    Dispatcher.Invoke(() =>
                    {
                        videoPlaceholder.Visibility = Visibility.Collapsed;
                        _ended = false;
                        _isPlaying = true;
                        var media = new Media(_libVLC, new Uri(tempPath));
                        _mediaPlayer.Play(media);
                        controlBar.Visibility = Visibility.Visible;
                        btnPlayPause.Content = "❚❚";
                        _timer.Start();

                        // Lưu lịch sử xem phim
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

        // Gửi lệnh TCP lên server
        private void SendCommand(string command)
        {
            byte[] data = Encoding.UTF8.GetBytes(command + "\n");
            _stream.Write(data, 0, data.Length);
        }

        // Đọc 1 dòng từ server
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