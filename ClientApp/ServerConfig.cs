namespace ClientApp
{
    
    public static class ServerConfig
    {
        // DNS name của VM Ubuntu trên Azure East Asia
        public const string Host = "minhflix.eastasia.cloudapp.azure.com";

        // Cổng TCP chính cho protocol MINHFLIX
        public const int TcpPort = 8888;

        // Cổng UDP cho viewer count và live chat
        public const int UdpPort = 8889;
    }
}