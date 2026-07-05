using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace ServerApp
{
    public partial class MainWindow : Window
    {
        private TcpListener _server;
        private bool _isRunning = false;
        private DatabaseManager _db = new DatabaseManager();

        private ConcurrentDictionary<string, NetworkStream> _onlineUsers
            = new ConcurrentDictionary<string, NetworkStream>();
        private ConcurrentDictionary<string, (int movieId, DateTime lastSeen)> _viewers
            = new ConcurrentDictionary<string, (int, DateTime)>();
            
        private ConcurrentDictionary<int, List<(string username, string content, DateTime time)>> _chatMessages
            = new ConcurrentDictionary<int, List<(string, string, DateTime)>>();

        public MainWindow()         
        {
            InitializeComponent();
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;
            _isRunning = true;
            txtStatus.Text = "Online";
            txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(76, 175, 80));
            statusDot.Fill = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(76, 175, 80));
            btnStart.IsEnabled = false;

            Thread serverThread = new Thread(StartServer);
            serverThread.IsBackground = true;
            serverThread.Start();

            Thread udpThread = new Thread(StartUdpServer);
            udpThread.IsBackground = true;
            udpThread.Start();
        }

        private void StartServer()
        {
            _server = new TcpListener(IPAddress.Any, 8888);
            _server.Start();
            Log("Server TCP đã bật! Đang chờ kết nối...");

            while (_isRunning)
            {
                try
                {
                    TcpClient client = _server.AcceptTcpClient();
                    Log($"Client kết nối: {client.Client.RemoteEndPoint}");
                    _clientCount++;
                    Dispatcher.Invoke(() => txtClientCount.Text = _clientCount.ToString());
                    Thread t = new Thread(() => HandleClient(client));
                    t.IsBackground = true;
                    t.Start();
                }
                catch { break; }
            }
        }

        private void StartUdpServer()
        {
            UdpClient udp = new UdpClient(8889);
            Log("Server UDP đã bật trên cổng 8889!");

            while (_isRunning)
            {
                try
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = udp.Receive(ref remote);
                    string msg = Encoding.UTF8.GetString(data);

                    if (msg.StartsWith("WATCHING|"))
                    {
                        // WATCHING|movieId|username — client đang xem phim
                        string[] parts = msg.Split('|');
                        if (parts.Length >= 3)
                        {
                            int movieId = int.Parse(parts[1]);
                            string username = parts[2];
                            _viewers[username] = (movieId, DateTime.Now);
                        }
                    }
                    else if (msg.StartsWith("GET_VIEWERS|"))
                    {
                        // GET_VIEWERS|movieId — client hỏi có bao nhiêu người đang xem
                        string[] parts = msg.Split('|');
                        if (parts.Length >= 2)
                        {
                            int movieId = int.Parse(parts[1]);

                            // Dọn những client không gửi gói trong 6 giây (đã rời)
                            var now = DateTime.Now;
                            foreach (var key in _viewers.Keys)
                            {
                                if ((now - _viewers[key].lastSeen).TotalSeconds > 6)
                                    _viewers.TryRemove(key, out _);
                            }

                            // Đếm số người đang xem đúng phim này
                            int count = 0;
                            foreach (var v in _viewers.Values)
                                if (v.movieId == movieId) count++;

                            // Trả về số lượng qua UDP
                            byte[] reply = Encoding.UTF8.GetBytes($"VIEWERS|{count}");
                            udp.Send(reply, reply.Length, remote);
                        }
                    }
                    else if (msg.StartsWith("CHAT|"))
                    {
                        // CHAT|movieId|username|nội dung
                        string[] parts = msg.Split('|');
                        if (parts.Length >= 4)
                        {
                            int movieId = int.Parse(parts[1]);
                            string username = parts[2];
                            string content = parts[3];

                            var list = _chatMessages.GetOrAdd(movieId, _ => new List<(string, string, DateTime)>());
                            lock (list)
                            {
                                list.Add((username, content, DateTime.Now));
                                // Giữ tối đa 100 tin nhắn gần nhất
                                if (list.Count > 100) list.RemoveAt(0);
                            }
                        }
                    }
                    else if (msg.StartsWith("GET_CHAT|"))
                    {
                        // GET_CHAT|movieId — trả tin nhắn 30 giây gần nhất
                        string[] parts = msg.Split('|');
                        if (parts.Length >= 2)
                        {
                            int movieId = int.Parse(parts[1]);
                            var sb = new StringBuilder("CHATMSG");

                            if (_chatMessages.TryGetValue(movieId, out var list))
                            {
                                lock (list)
                                {
                                    var recent = list.FindAll(m => (DateTime.Now - m.time).TotalSeconds <= 30);
                                    foreach (var m in recent)
                                        sb.Append($"|{m.username}:{m.content}");
                                }
                            }

                            byte[] reply = Encoding.UTF8.GetBytes(sb.ToString());
                            udp.Send(reply, reply.Length, remote);
                        }
                    }
                }
                catch { }
            }

            udp.Close();
        }

        private void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            try
            {
                while (true)
                {
                    string request = ReadLine(stream);
                    if (request == null) break;
                    request = request.Trim();
                    if (request.Length == 0) continue;

                    bool wasEncrypted = false;
                    if (request.StartsWith("ENC|"))
                    {
                        Log($"[Bản mã nhận được] {request}");
                        try { request = CryptoHelper.Decrypt(request.Substring(4)); }
                        catch { SendLine(stream, "ERROR|DECRYPT_FAIL"); continue; }
                        Log($"[Đã giải mã] {request}");
                        wasEncrypted = true;
                    }

                    Log($"Nhận lệnh: {request}");

                    if (request == "GET_MOVIES")
                    {
                        var movies = _db.GetAllMovies();
                        SendLine(stream, "MOVIES_START");
                        foreach (var m in movies)
                            SendLine(stream, $"{m.Id}|{m.Title}|{m.Category}|{m.Poster}|{m.Description}");
                        SendLine(stream, "END_MOVIES");
                        Log($"Đã gửi danh sách {movies.Count} phim");
                    }
                    else if (request.StartsWith("GET_SALT|"))
                    {
                        string username = request.Split('|')[1];
                        var (exists, salt) = _db.GetUserSalt(username);
                        if (exists)
                        {
                            if (wasEncrypted) SendEncrypted(stream, $"SALT|{salt}");
                            else SendLine(stream, $"SALT|{salt}");
                        }
                        else
                        {
                            if (wasEncrypted) SendEncrypted(stream, "LOGIN_FAIL|Tài khoản không tồn tại");
                            else SendLine(stream, "LOGIN_FAIL|Tài khoản không tồn tại");
                        }
                    }
                    else if (request.StartsWith("REGISTER|"))
                    {
                        string[] parts = request.Split('|');
                        bool ok = _db.Register(parts[1], parts[2], parts[3]);
                        string regResp = ok ? "REGISTER_OK" : "REGISTER_FAIL|Username đã tồn tại";
                        if (wasEncrypted) SendEncrypted(stream, regResp);
                        else SendLine(stream, regResp);
                        Log($"Register: {parts[1]} → {(ok ? "OK" : "FAIL")}");
                    }
                    else if (request.StartsWith("LOGIN|"))
                    {
                        string[] parts = request.Split('|');
                        string username = parts[1];
                        string passwordHash = parts[2];
                        bool ok = _db.VerifyLogin(username, passwordHash);
                        string loginResp = ok ? $"LOGIN_OK|{username}" : "LOGIN_FAIL|Sai tài khoản hoặc mật khẩu";
                        if (ok)
                        {
                            string status = _db.GetUserStatus(username);
                            if (status == "banned")
                            {
                                string bannedResp = "LOGIN_FAIL|Tài khoản đã bị xóa vì vi phạm tiêu chuẩn cộng đồng";
                                if (wasEncrypted) SendEncrypted(stream, bannedResp);
                                else SendLine(stream, bannedResp);
                                Log($"Login BLOCKED (banned): {username}");
                                continue;
                            }
                            // Gộp status vào LOGIN_OK luôn — không gửi 2 dòng riêng
                            string okResp = $"LOGIN_OK|{username}|{status}";
                            if (wasEncrypted) SendEncrypted(stream, okResp);
                            else SendLine(stream, okResp);
                            _onlineUsers[username] = stream;
                        }
                        else
                        {
                            string failResp = "LOGIN_FAIL|Sai tài khoản hoặc mật khẩu";
                            if (wasEncrypted) SendEncrypted(stream, failResp);
                            else SendLine(stream, failResp);
                        }
                        Log($"Login: {username} → {(ok ? "OK" : "FAIL")}");
                    }
                    else if (request.StartsWith("PLAY|"))
                    {
                        int movieId = int.Parse(request.Split('|')[1]);
                        var movie = _db.GetMovieById(movieId);
                        string fullPath = movie == null ? "" :
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, movie.FilePath);

                        if (movie == null || !File.Exists(fullPath))
                        {
                            SendLine(stream, "ERROR|FILE_NOT_FOUND");
                            Log($"Không tìm thấy file: {fullPath}");
                            continue;
                        }

                        Log($"Đang stream: {movie.Title}");
                        long fileSize = new FileInfo(fullPath).Length;
                        SendLine(stream, $"VIDEO|{fileSize}");

                        byte[] chunk = new byte[65536];
                        using FileStream fs = File.OpenRead(fullPath);
                        int n;
                        while ((n = fs.Read(chunk, 0, chunk.Length)) > 0)
                            stream.Write(chunk, 0, n);

                        Log($"Stream xong: {movie.Title}");
                    }
                    else if (request.StartsWith("ADD_COMMENT|"))
                    {
                        string[] parts = request.Split('|');
                        if (parts.Length >= 4)
                        {
                            _db.AddComment(int.Parse(parts[1]), parts[2], parts[3]);
                            SendLine(stream, "COMMENT_OK");
                            Log($"Comment: {parts[2]} → phim {parts[1]}");
                        }
                    }
                    else if (request.StartsWith("GET_COMMENTS|"))
                    {
                        int movieId = int.Parse(request.Split('|')[1]);
                        var comments = _db.GetComments(movieId);
                        SendLine(stream, $"COMMENTS_START|{comments.Count}");
                        foreach (var c in comments)
                            SendLine(stream, $"{c.username}|{c.content}|{c.time}");
                        SendLine(stream, "COMMENTS_END");
                    }
                    else if (request.StartsWith("ADD_HISTORY|"))
                    {
                        string[] parts = request.Split('|');
                        if (parts.Length >= 4)
                        {
                            _db.AddHistory(parts[1], int.Parse(parts[2]), parts[3]);
                            SendLine(stream, "HISTORY_OK");
                            Log($"History: {parts[1]} xem \"{parts[3]}\"");
                        }
                    }
                    else if (request.StartsWith("GET_HISTORY|"))
                    {
                        string username = request.Split('|')[1];
                        var history = _db.GetHistory(username);
                        SendLine(stream, "HISTORY_START");
                        foreach (var h in history)
                            SendLine(stream, $"{h.title}|{h.watchedAt}|{h.movieId}|{h.category}|{h.poster}");
                        SendLine(stream, "HISTORY_END");
                    }
                    else if (request.StartsWith("REPORT|"))
                    {
                        string[] parts = request.Split('|');
                        if (parts.Length >= 7)
                        {
                            _db.AddReport(parts[1], parts[2], int.Parse(parts[3]), parts[4], parts[5], parts[6]);
                            SendLine(stream, "REPORT_OK");
                            int wc = _db.GetWarnCount(parts[2]);
                            string wcText = wc > 0 ? $" [Lần bị báo cáo thứ {wc + 1}]" : "";
                            Log($"⚠ BÁO CÁO: {parts[1]} báo cáo {parts[2]}{wcText} — Lý do: {parts[6]} — Phim: {parts[4]}");
                        }
                    }
                    else if (request.StartsWith("GET_VIEW_COUNT|"))
                    {
                        int movieId = int.Parse(request.Split('|')[1]);
                        int count = _db.GetMovieViewCount(movieId);
                        SendLine(stream, $"VIEW_COUNT|{count}");
                        Log($"View count phim {movieId}: {count}");
                    }

                    else if (request == "GET_REPORTS")
                    {
                        var reports = _db.GetReports();
                        SendLine(stream, "REPORTS_START");
                        foreach (var r in reports)
                            SendLine(stream, $"{r.reporter}|{r.reported}|{r.movieTitle}|{r.commentContent}|{r.reason}|{r.reportedAt}");
                        SendLine(stream, "REPORTS_END");
                    }
                    else
                    {
                        SendLine(stream, "ERROR|UNKNOWN");
                    }
                }
            }
            catch { }
            finally
            {
                // Xóa khỏi danh sách online
                foreach (var key in _onlineUsers.Keys)
                    if (_onlineUsers[key] == stream)
                        _onlineUsers.TryRemove(key, out _);
                client.Close();
                _clientCount--;
                Dispatcher.Invoke(() => txtClientCount.Text = _clientCount.ToString());
                Log("Client đã ngắt kết nối.");
            }
        }

        private void SendLine(NetworkStream stream, string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text + "\n");
            stream.Write(data, 0, data.Length);
        }
        private void SendEncrypted(NetworkStream stream, string text)
        {
            string enc = "ENC|" + CryptoHelper.Encrypt(text);
            Log($"[Gửi mã hóa] {text}");
            byte[] data = Encoding.UTF8.GetBytes(enc + "\n");
            stream.Write(data, 0, data.Length);
        }

        private string ReadLine(NetworkStream stream)
        {
            var bytes = new List<byte>();
            int b;
            while ((b = stream.ReadByte()) != -1)
            {
                if (b == '\n') break;
                if (b == '\r') continue;
                bytes.Add((byte)b);
            }
            if (b == -1 && bytes.Count == 0) return null;
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        private int _clientCount = 0;

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                string time = DateTime.Now.ToString("HH:mm:ss");
                string prefix = "  ";
                System.Windows.Media.Brush color = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(204, 204, 204));

                if (message.StartsWith("[Bản mã nhận được]"))
                    color = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 165, 0));
                else if (message.StartsWith("[Đã giải mã]"))
                    color = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(100, 200, 100));
                else if (message.StartsWith("[Gửi mã hóa]"))
                    color = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(100, 180, 255));
                else if (message.Contains("kết nối"))
                    color = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(76, 175, 80));
                else if (message.Contains("ngắt kết nối"))
                    color = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(244, 67, 54));
                else if (message.Contains("Login") || message.Contains("Register"))
                    color = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(156, 39, 176));

                var tb = new TextBlock
                {
                    Text = $"[{time}]  {message}",
                    Foreground = color,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 12
                };
                lstLog.Items.Add(tb);

                
                lstLog.ScrollIntoView(tb);

                
                txtTime.Text = $"Last update: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
            });
        }
        private void btnReports_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var reports = _db.GetReportsWithId();
                if (reports.Count == 0)
                {
                    MessageBox.Show("Chưa có báo cáo nào.", "Báo cáo người dùng");
                    return;
                }

                var win = new Window
                {
                    Title = $"Quản lý báo cáo ({reports.Count})",
                    Width = 750,
                    Height = 550,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(15, 15, 15))
                };

                var mainGrid = new Grid { Margin = new Thickness(15) };
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var header = new TextBlock
                {
                    Text = $"⚠ Danh sách báo cáo cần xử lý ({reports.Count})",
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(229, 9, 20)),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                Grid.SetRow(header, 0);
                mainGrid.Children.Add(header);

                var scroll = new ScrollViewer();
                var stack = new StackPanel();

                foreach (var r in reports)
                {
                    var reportId = r.id;
                    var reported = r.reported;

                    var card = new Border
                    {
                        Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(25, 25, 25)),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(15, 10, 15, 10),
                        Margin = new Thickness(0, 0, 0, 10)
                    };

                    var cardGrid = new Grid();
                    cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var infoPanel = new StackPanel();
                    int warnCount = _db.GetWarnCount(r.reported);
                    string warnLabel = warnCount > 0 ? $"  [Đã bị cảnh cáo {warnCount} lần]" : "";

                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = $"⚠ {r.reporter} báo cáo {r.reported}{warnLabel}",
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(229, 9, 20)),
                        FontWeight = FontWeights.Bold,
                        FontSize = 13
                    });
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = $"Lý do: {r.reason}  |  Phim: {r.movieTitle}  |  {r.reportedAt}",
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(255, 165, 0)),
                        FontSize = 12,
                        Margin = new Thickness(0, 4, 0, 0)
                    });
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = $"Nội dung: \"{r.commentContent}\"",
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(180, 180, 180)),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0)
                    });
                    Grid.SetColumn(infoPanel, 0);
                    cardGrid.Children.Add(infoPanel);

                    var btnPanel = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(12, 0, 0, 0)
                    };

                    var btnWarn = new Button
                    {
                        Content = "⚑ Cảnh cáo",
                        Height = 30,
                        Width = 120,
                        Margin = new Thickness(0, 0, 0, 6),
                        Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(255, 152, 0)),
                        Foreground = System.Windows.Media.Brushes.White,
                        BorderThickness = new Thickness(0),
                        Cursor = System.Windows.Input.Cursors.Hand,
                        FontSize = 12
                    };
                    btnWarn.Click += (s, ev) =>
                    {
                        _db.SetUserStatus(reported, "warned");
                        if (_onlineUsers.TryGetValue(reported, out var userStream))
                            try { SendLine(userStream, "WARNED|Tài khoản của bạn đã bị cảnh cáo."); } catch { }
                        _db.DeleteReport(reportId);
                        Log($"⚑ Đã cảnh cáo: {reported}");
                        MessageBox.Show($"Đã gắn cờ cảnh cáo tài khoản {reported}.");
                        win.Close();
                        
                    };

                    var btnBan = new Button
                    {
                        Content = "🚫 Ban vĩnh viễn",
                        Height = 30,
                        Width = 120,
                        Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(229, 9, 20)),
                        Foreground = System.Windows.Media.Brushes.White,
                        BorderThickness = new Thickness(0),
                        Cursor = System.Windows.Input.Cursors.Hand,
                        FontSize = 12
                    };
                    btnBan.Click += (s, ev) =>
                    {
                        var confirm = MessageBox.Show(
                            $"Ban vĩnh viễn tài khoản {reported}?",
                            "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (confirm != MessageBoxResult.Yes) return;
                        _db.SetUserStatus(reported, "banned");
                        if (_onlineUsers.TryGetValue(reported, out var userStream))
                            try { SendLine(userStream, "BANNED|Tài khoản đã bị xóa vì vi phạm tiêu chuẩn cộng đồng."); } catch { }
                        _db.DeleteReport(reportId);
                        Log($"🚫 Đã ban: {reported}");
                        MessageBox.Show($"Đã ban vĩnh viễn tài khoản {reported}.");
                        win.Close();
                        btnReports_Click(null, null);
                    };

                    btnPanel.Children.Add(btnWarn);
                    btnPanel.Children.Add(btnBan);
                    Grid.SetColumn(btnPanel, 1);
                    cardGrid.Children.Add(btnPanel);
                    card.Child = cardGrid;
                    stack.Children.Add(card);
                }

                scroll.Content = stack;
                Grid.SetRow(scroll, 1);
                mainGrid.Children.Add(scroll);
                win.Content = mainGrid;
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi: " + ex.Message);
            }
        }
        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            lstLog.Items.Clear();
        }
    }
}