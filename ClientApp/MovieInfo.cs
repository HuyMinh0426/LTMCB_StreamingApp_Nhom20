using System;

namespace ClientApp
{
    public class MovieInfo
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public string Poster { get; set; }
        public string Description { get; set; }

        public System.Windows.Media.Imaging.BitmapImage PosterImage
        {
            get
            {
                try
                {
                    if (!System.IO.File.Exists(Poster))
                    {
                        System.Diagnostics.Debug.WriteLine($"[POSTER] KHÔNG TỒN TẠI: {Poster}");
                        return null;
                    }
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(Poster);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 200;
                    bmp.EndInit();
                    return bmp;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[POSTER] LỖI {Poster}: {ex.Message}");
                    return null;
                }
            }
        }
    }
}