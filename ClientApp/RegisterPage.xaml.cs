using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClientApp
{
    public partial class RegisterPage : Page
    {
        private string _ip;

        public RegisterPage()
        {
            InitializeComponent();
        }

        private void btnRegister_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password;
            string confirm = txtConfirmPassword.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                txtMessage.Text = "Vui lòng nhập đầy đủ thông tin!";
                return;
            }
            if (password != confirm)
            {
                txtMessage.Text = "Mật khẩu nhập lại không khớp!";
                return;
            }
            if (password.Length < 3)
            {
                txtMessage.Text = "Mật khẩu phải có ít nhất 3 ký tự!";
                return;
            }

            try
            {
                using var client = new TcpClient(_ip, 8888);
                using var stream = client.GetStream();

                string salt = GenerateSalt();
                string hash = HashPassword(password, salt);

                string encReg = "ENC|" + CryptoHelper.Encrypt($"REGISTER|{username}|{hash}|{salt}");
                byte[] data = Encoding.UTF8.GetBytes(encReg + "\n");
                stream.Write(data, 0, data.Length);

                string response = ReadLine(stream);
                if (response != null && response.StartsWith("ENC|"))
                    response = CryptoHelper.Decrypt(response.Substring(4));
                if (response != null && response.StartsWith("REGISTER_OK"))
                {
                    MessageBox.Show("Đăng ký thành công! Hãy đăng nhập.",
                        "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    if (NavigationService != null && NavigationService.CanGoBack)
                        NavigationService.GoBack();   // quay về trang đăng nhập
                }
                else
                {
                    txtMessage.Text = response != null && response.Contains("|")
                        ? response.Split('|')[1] : "Đăng ký thất bại";
                }
            }
            catch (Exception ex)
            {
                txtMessage.Text = $"Lỗi: {ex.Message}";
            }
        }
        private void txtConfirmPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                btnRegister_Click(null, null);
        }

        private void GoLogin_Click(object sender, MouseButtonEventArgs e)
        {
            if (NavigationService != null && NavigationService.CanGoBack)
                NavigationService.GoBack();
        }

        private string HashPassword(string password, string salt)
        {
            string combined = password + salt;
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
            return Convert.ToHexString(bytes).ToLower();
        }

        private string GenerateSalt()
        {
            byte[] saltBytes = new byte[16];
            RandomNumberGenerator.Fill(saltBytes);
            return Convert.ToHexString(saltBytes).ToLower();
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
    }
}