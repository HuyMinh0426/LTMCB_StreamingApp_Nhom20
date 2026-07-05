using System.Collections.Generic;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClientApp
{
    public class HistoryItem
    {
        public string Title { get; set; }
        public string Category { get; set; }
        public string WatchedAt { get; set; }
        public string Poster { get; set; }
        public int MovieId { get; set; }
    }

    public partial class HistoryPage : Page
    {
        private List<HistoryItem> _historyItems = new List<HistoryItem>();

        public HistoryPage()
        {
            InitializeComponent();
            LoadHistory();
        }

        private void LoadHistory()
        {
            try
            {
                SendCommand($"GET_HISTORY|{Session.Username}");
                string line = ReadLine();
                if (line == "HISTORY_START")
                {
                    while (true)
                    {
                        line = ReadLine();
                        if (line == null || line == "HISTORY_END") break;
                        // format: Title|WatchedAt|MovieId|Category|Poster
                        string[] parts = line.Split('|');
                        if (parts.Length >= 5)
                            _historyItems.Add(new HistoryItem
                            {
                                Title = parts[0],
                                WatchedAt = parts[1],
                                MovieId = int.Parse(parts[2]),
                                Category = parts[3],
                                Poster = System.IO.Path.Combine(
                                    AppDomain.CurrentDomain.BaseDirectory, "posters", parts[4])
                            });
                    }
                }
                icHistory.ItemsSource = _historyItems;
            }
            catch { }
        }

        private void Movie_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as System.Windows.Controls.Border;
            var item = border?.DataContext as HistoryItem;
            if (item == null) return;

            var movie = new MovieInfo
            {
                Id = item.MovieId,
                Title = item.Title,
                Category = item.Category,
                Poster = item.Poster, 
                Description = ""
            };
            NavigationService.Navigate(new MovieDetailPage(movie));
        }

        private void btnBack_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
                NavigationService.GoBack();
        }

        private void SendCommand(string command)
        {
            byte[] data = Encoding.UTF8.GetBytes(command + "\n");
            Session.Stream.Write(data, 0, data.Length);
        }

        private string ReadLine()
        {
            var bytes = new List<byte>();
            int b;
            while ((b = Session.Stream.ReadByte()) != -1)
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