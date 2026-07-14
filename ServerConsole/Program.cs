using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ServerApp; // dùng chung DatabaseManager và CryptoHelper (đã link từ ServerApp)

namespace ServerConsole
{
    class Program
    {
        // Trạng thái Server và các cấu trúc dữ liệu quản lý phiên
        private static bool _isRunning = true;
        private static TcpListener _server;
        private static readonly DatabaseManager _db = new DatabaseManager();
        private static int _clientCount = 0;

        // Danh sách user online, dùng để đá session khi bị warn/ban
        private static readonly ConcurrentDictionary<string, NetworkStream> _onlineUsers
            = new ConcurrentDictionary<string, NetworkStream>();

        // Theo dõi ai đang xem phim nào cho tính năng đếm viewer real-time qua UDP
        private static readonly ConcurrentDictionary<string, (int movieId, DateTime lastSeen)> _viewers
            = new ConcurrentDictionary<string, (int, DateTime)>();

        // Bộ nhớ tạm tin nhắn chat trực tiếp (không lưu DB) — chỉ tồn tại trong RAM
        private static readonly ConcurrentDictionary<int, List<(string username, string content, DateTime time)>> _chatMessages
            = new ConcurrentDictionary<int, List<(string, string, DateTime)>>();

        static void Main(string[] args)
        {
            Log("======================================");
            Log("  MINHFLIX Streaming Server - v2.0");
            Log("  Deployed on Cloud VM");
            Log("======================================");

            // Chạy TCP và UDP song song, không blocking Main
            Task.Run(() => StartServer());
            Task.Run(() => StartUdpServer());

            // Giữ tiến trình sống mãi mãi (systemd sẽ quản lý stop khi cần)
            Task.Delay(-1).Wait();
        }

        // TCP Server — nghe cổng 8888, mỗi Client được xử lý trong thread riêng
        private static void StartServer()
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
                    Interlocked.Increment(ref _clientCount);
                    Log($"[Clients: {_clientCount}]");

                    Thread t = new Thread(() => HandleClient(client));
                    t.IsBackground = true;
                    t.Start();
                }
                catch { break; }
            }
        }

        // UDP Server — nghe cổng 8889 cho viewer count và live chat
        private static void StartUdpServer()
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
                        // WATCHING|movieId|username — client báo mình đang xem phim
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

                            // Dọn viewer không gửi gói trong 6 giây (coi như đã thoát)
                            var now = DateTime.Now;
                            foreach (var key in _viewers.Keys)
                            {
                                if ((now - _viewers[key].lastSeen).TotalSeconds > 6)
                                    _viewers.TryRemove(key, out _);
                            }

                            // Đếm số người còn xem đúng phim này
                            int count = 0;
                            foreach (var v in _viewers.Values)
                                if (v.movieId == movieId) count++;

                            byte[] reply = Encoding.UTF8.GetBytes($"VIEWERS|{count}");
                            udp.Send(reply, reply.Length, remote);
                        }
                    }
                    else if (msg.StartsWith("CHAT|"))
                    {
                        // CHAT|movieId|username|nội dung — client gửi tin nhắn
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
                                if (list.Count > 100) list.RemoveAt(0);
                            }
                        }
                    }
                    else if (msg.StartsWith("GET_CHAT|"))
                    {
                        // GET_CHAT|movieId — trả tin nhắn trong 30 giây gần nhất
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

        // Handler cho mỗi Client — xử lý toàn bộ command TCP theo protocol MINHFLIX
        private static void HandleClient(TcpClient client)
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

                    // Nếu gói bắt đầu bằng ENC| thì là bản mã AES cần giải mã
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
                            // Gộp status vào LOGIN_OK
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

                        // Stream video 64KB mỗi chunk
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
                // Xóa user khỏi danh sách online khi ngắt kết nối
                foreach (var key in _onlineUsers.Keys)
                    if (_onlineUsers[key] == stream)
                        _onlineUsers.TryRemove(key, out _);
                client.Close();
                Interlocked.Decrement(ref _clientCount);
                Log($"Client đã ngắt kết nối. [Clients: {_clientCount}]");
            }
        }

        private static void SendLine(NetworkStream stream, string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text + "\n");
            stream.Write(data, 0, data.Length);
        }

        private static void SendEncrypted(NetworkStream stream, string text)
        {
            string enc = "ENC|" + CryptoHelper.Encrypt(text);
            Log($"[Gửi mã hóa] {text}");
            byte[] data = Encoding.UTF8.GetBytes(enc + "\n");
            stream.Write(data, 0, data.Length);
        }

        private static string ReadLine(NetworkStream stream)
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

        // Log ra console, format y hệt WPF cũ để dễ theo dõi trên journalctl
        private static void Log(string message)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}