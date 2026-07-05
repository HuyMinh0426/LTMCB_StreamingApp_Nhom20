using System.Net.Sockets;

namespace ClientApp
{
    public static class Session
    {
        public static TcpClient Client;
        public static NetworkStream Stream;
        public static string Username;
        public static string UserStatus { get; set; } = "normal";
    }
}