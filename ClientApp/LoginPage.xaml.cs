using System;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClientApp
{
    public partial class LoginPage : Page
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private string _pendingPassword = "";
        private string _pendingUsername = "";
        private string _saltResponse = "";

        public LoginPage()
        {
            InitializeComponent();
        }

        private void GoRegister_Click(object sender, MouseButtonEventArgs e)
        {
            NavigationService.Navigate(new RegisterPage());
        }

        private async void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                txtMessage.Text = "Vui lòng nhập đầy đủ thông tin!";
                return;
            }

            txtMessage.Text = "";
            loadingOverlay.Visibility = Visibility.Visible;   // hiện spinner
            btnLogin.IsEnabled = false;

            try
            {
                _pendingUsername = username;
                _pendingPassword = password;

                // Đẩy phần kết nối + chờ phản hồi sang luồng nền để spinner xoay mượt
                await System.Threading.Tasks.Task.Run(() =>
                {
                    KetNoi();
                    string enc = "ENC|" + CryptoHelper.Encrypt($"GET_SALT|{username}");
                    byte[] data = Encoding.UTF8.GetBytes(enc + "\n");
                    _stream.Write(data, 0, data.Length);
                    _saltResponse = DecryptIfNeeded(ReadLine());  // lưu phản hồi salt
                });

                // Quay lại luồng UI xử lý tiếp
                HandleResponse(_saltResponse);
            }
            catch (Exception ex)
            {
                txtMessage.Text = $"Lỗi kết nối: {ex.Message}";
            }
            finally
            {
                loadingOverlay.Visibility = Visibility.Collapsed;  // ẩn spinner
                btnLogin.IsEnabled = true;
            }
        }
        private void txtPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                btnLogin_Click(null, null);
        }

        private void KetNoi()
        {
            if (_client != null && _client.Connected) return;
            _client = new TcpClient(ServerConfig.Host, ServerConfig.TcpPort);
            _stream = _client.GetStream();
        }

        private void SendCommand(string command)
        {
            string enc = "ENC|" + CryptoHelper.Encrypt(command);
            byte[] data = Encoding.UTF8.GetBytes(enc + "\n");
            _stream.Write(data, 0, data.Length);

            string response = ReadLine();
            HandleResponse(response);
        }

        private void HandleResponse(string response)
        {
            if (response == null) return;

            if (response.StartsWith("SALT|"))
            {
                string salt = response.Split('|')[1];
                string hash = HashPassword(_pendingPassword, salt);
                string encLogin = "ENC|" + CryptoHelper.Encrypt($"LOGIN|{_pendingUsername}|{hash}");
                byte[] data = Encoding.UTF8.GetBytes(encLogin + "\n");
                _stream.Write(data, 0, data.Length);

                string loginResponse = DecryptIfNeeded(ReadLine());
                if (loginResponse != null && loginResponse.StartsWith("LOGIN_OK|"))
                {
                    string[] parts = loginResponse.Split('|');
                    string username = parts[1];
                    string status = parts.Length >= 3 ? parts[2] : "normal";

                    Session.Client = _client;
                    Session.Stream = _stream;
                    Session.Username = username;
                    Session.UserStatus = status;

                    NavigationService.Navigate(new MoviesPage());
                }
                else if (loginResponse != null && loginResponse.StartsWith("LOGIN_FAIL|"))
                {
                    txtMessage.Text = loginResponse.Split('|')[1];
                    try { _client?.Close(); } catch { }
                    _client = null;
                }
                else
                {
                    txtMessage.Text = "Sai tài khoản hoặc mật khẩu!";
                    _client.Close();
                    _client = null;
                }
            }
            else if (response.StartsWith("LOGIN_FAIL|"))
            {
                txtMessage.Text = response.Split('|')[1];
            }
        }

        private string DecryptIfNeeded(string line)
        {
            if (line != null && line.StartsWith("ENC|"))
                return CryptoHelper.Decrypt(line.Substring(4));
            return line;
        }
        private string ReadLine()
        {
            var bytes = new System.Collections.Generic.List<byte>();
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

        private string HashPassword(string password, string salt)
        {
            string combined = password + salt;
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
            return Convert.ToHexString(bytes).ToLower();
        }
    }
}