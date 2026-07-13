using Npgsql;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace ServerApp
{
    public class DatabaseManager
    {
        // Chuỗi kết nối tới PostgreSQL trên Azure Cloud
        // SslMode=Require vì Azure bắt buộc mã hóa kết nối
        private string _connString =
            "Host=minhflix-db-24521090.postgres.database.azure.com;" +
            "Port=5432;" +
            "Username=minhadmin;" +
            "Password=Uit2026@Password!;" +
            "Database=minhflix;" +
            "SslMode=Require;" +
            "Trust Server Certificate=true";

        // Giữ tạm _dbPath cho các method chưa migrate
        private string _dbPath = "movies.db";

        public DatabaseManager()
        {
            InitDatabase();
        }

        private void InitDatabase()
        {
            using var conn = new NpgsqlConnection(_connString);
            conn.Open();

            // Tạo bảng Users nếu chưa có
            // SERIAL PRIMARY KEY là kiểu ID tự tăng của PostgreSQL (thay cho AUTOINCREMENT của SQLite)
            // Bọc "..." vì PostgreSQL phân biệt hoa/thường trong tên bảng và cột
            string createUsers = @"
                CREATE TABLE IF NOT EXISTS ""Users"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""Username"" TEXT NOT NULL UNIQUE,
                    ""PasswordHash"" TEXT NOT NULL,
                    ""Salt"" TEXT NOT NULL,
                    ""Status"" TEXT NOT NULL DEFAULT 'normal',
                    ""WarnCount"" INTEGER NOT NULL DEFAULT 0
                );";
            using (var cmd = new NpgsqlCommand(createUsers, conn))
                cmd.ExecuteNonQuery();

            // Tạo bảng Movies nếu chưa có
            string createMovies = @"
                CREATE TABLE IF NOT EXISTS ""Movies"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""Title"" TEXT NOT NULL,
                    ""FilePath"" TEXT NOT NULL,
                    ""Category"" TEXT,
                    ""Poster"" TEXT,
                    ""Description"" TEXT,
                    ""Duration"" INTEGER
                );";
            using (var cmd = new NpgsqlCommand(createMovies, conn))
                cmd.ExecuteNonQuery();

            // Tạo bảng Comments nếu chưa có
            string createComments = @"
                CREATE TABLE IF NOT EXISTS ""Comments"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""MovieId"" INTEGER NOT NULL,
                    ""Username"" TEXT NOT NULL,
                    ""Content"" TEXT NOT NULL,
                    ""CreatedAt"" TEXT NOT NULL
                );";
            using (var cmd2 = new NpgsqlCommand(createComments, conn))
                cmd2.ExecuteNonQuery();

            // Tạo bảng History nếu chưa có
            string createHistory = @"
                CREATE TABLE IF NOT EXISTS ""History"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""Username"" TEXT NOT NULL,
                    ""MovieId"" INTEGER NOT NULL,
                    ""MovieTitle"" TEXT NOT NULL,
                    ""WatchedAt"" TEXT NOT NULL
                );";
            using (var cmd3 = new NpgsqlCommand(createHistory, conn))
                cmd3.ExecuteNonQuery();

            // Tạo bảng Reports nếu chưa có
            string createReports = @"
                CREATE TABLE IF NOT EXISTS ""Reports"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""Reporter"" TEXT NOT NULL,
                    ""Reported"" TEXT NOT NULL,
                    ""MovieId"" INTEGER NOT NULL,
                    ""MovieTitle"" TEXT NOT NULL,
                    ""CommentContent"" TEXT NOT NULL,
                    ""Reason"" TEXT NOT NULL,
                    ""ReportedAt"" TEXT NOT NULL
                );";
            using (var cmd4 = new NpgsqlCommand(createReports, conn))
                cmd4.ExecuteNonQuery();

            // Data 60 phim đã được migrate sang PostgreSQL từ trước
            // Vẫn giữ InsertSampleData để phòng khi deploy trên server mới chưa có data
            InsertSampleData(conn); 
        }

        private void InsertSampleData(NpgsqlConnection conn)
        {
            // Kiểm tra bảng Movies đã có data chưa, nếu có rồi thì skip
            using var checkCmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Movies\"", conn);
            long count = (long)checkCmd.ExecuteScalar();
            if (count > 0) return;

            var movies = new List<(string Title, string Poster, string Category, string Desc)>
            {
                ("Naruto", "naruto.jpg", "Anime", "Hành trình của cậu nhóc ninja Naruto Uzumaki theo đuổi giấc mơ trở thành Hokage."),
                ("One Piece", "onepiece.jpg", "Anime", "Luffy và băng hải tặc Mũ Rơm phiêu lưu tìm kho báu One Piece huyền thoại."),
                ("Demon Slayer", "demonslayer.jpg", "Anime", "Tanjiro trở thành thợ săn quỷ để cứu em gái và trả thù cho gia đình."),
                ("Attack on Titan", "attackontitan.jpg", "Anime", "Nhân loại chiến đấu sinh tồn trước những Titan khổng lồ ăn thịt người."),
                ("Dragon Ball Z", "dragonball.jpg", "Anime", "Songoku và các chiến binh Z bảo vệ Trái Đất khỏi những kẻ thù vũ trụ."),
                ("Fullmetal Alchemist", "fullmetal.jpg", "Anime", "Hai anh em nhà Elric tìm Hòn đá Triết gia để khôi phục cơ thể."),
                ("Death Note", "deathnote.jpg", "Anime", "Light Yagami sở hữu cuốn sổ tử thần và cuộc đấu trí với thám tử L."),
                ("Jujutsu Kaisen", "jujutsu.jpg", "Anime", "Yuji Itadori bước vào thế giới chú thuật sư chống lại nguyền hồn."),
                ("My Hero Academia", "myhero.jpg", "Anime", "Trong thế giới ai cũng có siêu năng lực, Deku theo đuổi giấc mơ anh hùng."),
                ("Hunter x Hunter", "hunterxhunter.jpg", "Anime", "Gon lên đường trở thành Thợ Săn để đi tìm người cha của mình."),
                ("Bleach", "bleach.jpg", "Anime", "Ichigo trở thành Thần Chết thực tập bảo vệ thế giới khỏi Hollow."),
                ("Sword Art Online", "sao.jpg", "Anime", "Người chơi mắc kẹt trong game thực tế ảo, chết trong game là chết thật."),
                ("Re:Zero", "rezero.jpg", "Anime", "Subaru lạc vào thế giới khác với năng lực quay ngược thời gian mỗi khi chết."),
                ("Tokyo Ghoul", "tokyoghoul.jpg", "Anime", "Kaneki trở thành nửa người nửa ghoul và đấu tranh để sinh tồn."),
                ("Vinland Saga", "vinland.jpg", "Anime", "Hành trình báo thù và trưởng thành của Thorfinn trong thời đại Viking."),
                ("Nhà Bà Nữ", "nhabannu.jpg", "Phim Việt", "Câu chuyện gia đình ba thế hệ với những mâu thuẫn và yêu thương."),
                ("Bố Già", "bogia.jpg", "Phim Việt", "Ông Ba Sang và những hi sinh thầm lặng của người cha nơi xóm lao động."),
                ("Mắt Biếc", "matbiec.jpg", "Phim Việt", "Chuyện tình đơn phương day dứt của Ngạn dành cho Hà Lan."),
                ("Hai Phượng", "haiphuong.jpg", "Phim Việt", "Người mẹ đơn thân liều mình giải cứu con gái khỏi băng bắt cóc."),
                ("Cua Lại Vợ Bầu", "cualaivobau.jpg", "Phim Việt", "Bi hài kịch về chàng trai tìm cách níu kéo người yêu đang mang thai."),
                ("Em Là Bà Nội Của Anh", "emlabanoi.jpg", "Phim Việt", "Bà cụ 70 tuổi bỗng trẻ lại thành thiếu nữ 20 và những rắc rối đáng yêu."),
                ("Tôi Thấy Hoa Vàng Trên Cỏ Xanh", "hoavang.jpg", "Phim Việt", "Ký ức tuổi thơ trong trẻo nơi làng quê Việt Nam."),
                ("Cô Ba Sài Gòn", "cobasaigon.jpg", "Phim Việt", "Câu chuyện về áo dài và những thế hệ phụ nữ Sài Gòn xưa."),
                ("Lật Mặt 6", "latmat6.jpg", "Phim Việt", "Phim hành động hài kịch về tình bạn và những bí mật giấu kín."),
                ("Tiệc Trăng Máu", "tiectrangmau.jpg", "Phim Việt", "Bảy người bạn chơi trò công khai điện thoại và những bí mật vỡ lở."),
                ("Ròm", "rom.jpg", "Phim Việt", "Cậu bé Ròm mưu sinh bằng nghề ghi đề số ở khu chung cư cũ."),
                ("Sắc Đẹp Ngàn Cân", "sacdep.jpg", "Phim Việt", "Cô gái ngoại hình quá khổ phẫu thuật thẩm mỹ để đổi đời."),
                ("Gái Già Lắm Chiêu", "gaigialac.jpg", "Phim Việt", "Chuyện tình và màn đấu trí trong giới thượng lưu xứ Huế."),
                ("Kẻ Thứ Ba", "kethuba.jpg", "Phim Việt", "Mối tình tay ba đầy bi kịch và những lựa chọn nghiệt ngã."),
                ("Trời Sáng Rồi Ta Ngủ Thêm Chút Nữa", "troisangroi.jpg", "Phim Việt", "Hành trình âm nhạc và tuổi trẻ của hai người bạn ở Sài Gòn."),
                ("Squid Game", "squidgame.jpg", "Phim Hàn", "456 người nợ nần tham gia trò chơi sinh tử để giành 45.6 tỷ won."),
                ("Crash Landing on You", "crashlanding.jpg", "Phim Hàn", "Nữ thừa kế Hàn Quốc rơi xuống Triều Tiên và yêu một sĩ quan quân đội."),
                ("Bloodhounds", "bloodhounds.jpg", "Phim Hàn", "Hai võ sĩ quyền anh chống lại bọn cho vay nặng lãi tàn nhẫn."),
                ("Parasite", "parasite.jpg", "Phim Hàn", "Gia đình nghèo từng bước xâm nhập vào nhà một tài phiệt giàu có."),
                ("All of Us Are Dead", "alofusdead.jpg", "Phim Hàn", "Học sinh mắc kẹt trong trường giữa đại dịch zombie."),
                ("Itaewon Class", "itaewon.jpg", "Phim Hàn", "Chàng trai khởi nghiệp quán nhậu để trả thù một tập đoàn lớn."),
                ("Vincenzo", "vincenzo.jpg", "Phim Hàn", "Luật sư mafia gốc Hàn trở về nước, dùng luật giang hồ trừng trị cái ác."),
                ("My Love From The Star", "mylovefromstar.jpg", "Phim Hàn", "Người ngoài hành tinh sống 400 năm trên Trái Đất yêu một nữ minh tinh."),
                ("Descendants of the Sun", "descendants.jpg", "Phim Hàn", "Chuyện tình giữa quân nhân và nữ bác sĩ nơi vùng chiến sự."),
                ("Kingdom", "kingdom.jpg", "Phim Hàn", "Thái tử điều tra đại dịch zombie giữa triều đại Joseon."),
                ("Stranger", "stranger.jpg", "Phim Hàn", "Công tố viên vô cảm và nữ cảnh sát hợp sức phá án tham nhũng."),
                ("Sweet Home", "sweethome.jpg", "Phim Hàn", "Cư dân chung cư chống lại những con người biến thành quái vật."),
                ("The Glory", "theglory.jpg", "Phim Hàn", "Người phụ nữ lên kế hoạch báo thù những kẻ bắt nạt thời học sinh."),
                ("Moving", "moving.jpg", "Phim Hàn", "Những người có siêu năng lực che giấu thân phận để bảo vệ con cái."),
                ("Juvenile Justice", "juvenile.jpg", "Phim Hàn", "Nữ thẩm phán lạnh lùng xét xử các vụ án tội phạm vị thành niên."),
                ("Avengers Endgame", "endgame.jpg", "Chiếu Rạp", "Biệt đội Avengers tập hợp lần cuối để đảo ngược cú búng tay của Thanos."),
                ("Interstellar", "interstellar.jpg", "Chiếu Rạp", "Phi hành gia du hành qua hố đen tìm hành tinh mới cứu nhân loại."),
                ("The Dark Knight", "darkknight.jpg", "Chiếu Rạp", "Batman đối đầu với gã hề Joker điên loạn ở thành phố Gotham."),
                ("Inception", "inception.jpg", "Chiếu Rạp", "Kẻ trộm xâm nhập giấc mơ để cấy ghép ý tưởng vào tiềm thức."),
                ("Spider-Man No Way Home", "spiderman.jpg", "Chiếu Rạp", "Người Nhện đối mặt đa vũ trụ khi phép thuật làm rạn nứt thực tại."),
                ("Oppenheimer", "oppenheimer.jpg", "Chiếu Rạp", "Câu chuyện về cha đẻ bom nguyên tử và những dằn vặt lương tâm."),
                ("Dune Part Two", "dune2.jpg", "Chiếu Rạp", "Paul Atreides liên minh với người Fremen để báo thù và định đoạt số phận."),
                ("Avatar The Way of Water", "avatar2.jpg", "Chiếu Rạp", "Gia đình Jake Sully tìm nơi nương náu giữa đại dương Pandora."),
                ("Top Gun Maverick", "topgun.jpg", "Chiếu Rạp", "Phi công Maverick trở lại huấn luyện thế hệ mới cho nhiệm vụ tử thần."),
                ("Doctor Strange Multiverse", "doctorstrange.jpg", "Chiếu Rạp", "Phù thủy tối thượng phiêu lưu qua đa vũ trụ đầy hiểm nguy."),
                ("Black Panther Wakanda Forever", "blackpanther.jpg", "Chiếu Rạp", "Wakanda đối mặt thử thách mới sau sự ra đi của nhà vua."),
                ("Thor Love and Thunder", "thor4.jpg", "Chiếu Rạp", "Thần Sấm Thor đối đầu kẻ tàn sát thần linh Gorr."),
                ("The Batman", "thebatman.jpg", "Chiếu Rạp", "Batman trẻ điều tra chuỗi án mạng của tên sát nhân Riddler."),
                ("Mission Impossible Dead Reckoning", "missionimpossible.jpg", "Chiếu Rạp", "Ethan Hunt truy đuổi một vũ khí AI nguy hiểm bậc nhất thế giới."),
                ("Fast X", "fastx.jpg", "Chiếu Rạp", "Dom Toretto và gia đình đối mặt kẻ thù nguy hiểm nhất từ quá khứ."),
            };

            foreach (var m in movies)
            {
                // Bọc tên bảng và cột trong "..." vì PostgreSQL case-sensitive
                string insertSql = @"
                    INSERT INTO ""Movies"" (""Title"", ""FilePath"", ""Category"", ""Poster"", ""Description"", ""Duration"") 
                    VALUES (@title, @path, @category, @poster, @desc, @duration)";
                using var insertCmd = new NpgsqlCommand(insertSql, conn);
                insertCmd.Parameters.AddWithValue("@title", m.Title);
                insertCmd.Parameters.AddWithValue("@path", "videos/bunny.mp4");
                insertCmd.Parameters.AddWithValue("@category", m.Category);
                insertCmd.Parameters.AddWithValue("@poster", m.Poster);
                insertCmd.Parameters.AddWithValue("@desc", m.Desc);
                insertCmd.Parameters.AddWithValue("@duration", 0);
                insertCmd.ExecuteNonQuery();
            }
        }

        public List<Movie> GetAllMovies()
        {
            var movies = new List<Movie>();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = new SqliteCommand("SELECT * FROM Movies", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                movies.Add(ReadMovie(reader));
            return movies;
        }

        public Movie? GetMovieById(int id)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = new SqliteCommand("SELECT * FROM Movies WHERE Id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read()) return ReadMovie(reader);
            return null;
        }

        private Movie ReadMovie(SqliteDataReader reader)
        {
            return new Movie
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                FilePath = reader.GetString(2),
                Category = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Poster = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Description = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Duration = reader.IsDBNull(6) ? 0 : reader.GetInt32(6)
            };
        }

        public bool Register(string username, string passwordHash, string salt)
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                conn.Open();
                string sql = "INSERT INTO Users (Username, PasswordHash, Salt) VALUES (@username, @hash, @salt)";
                using var cmd = new SqliteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@hash", passwordHash);
                cmd.Parameters.AddWithValue("@salt", salt);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch { return false; }
        }

        public (bool success, string salt) GetUserSalt(string username)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = new SqliteCommand("SELECT Salt FROM Users WHERE Username = @username", conn);
            cmd.Parameters.AddWithValue("@username", username);
            var result = cmd.ExecuteScalar();
            if (result == null) return (false, "");
            return (true, result.ToString()!);
        }

        public bool VerifyLogin(string username, string passwordHash)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM Users WHERE Username=@u AND PasswordHash=@h", conn);
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@h", passwordHash);
            long count = (long)cmd.ExecuteScalar();
            return count > 0;
        }

        public void AddComment(int movieId, string username, string content)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = "INSERT INTO Comments (MovieId, Username, Content, CreatedAt) VALUES (@mid, @user, @content, @time)";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@mid", movieId);
            cmd.Parameters.AddWithValue("@user", username);
            cmd.Parameters.AddWithValue("@content", content);
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("HH:mm dd/MM"));
            cmd.ExecuteNonQuery();
        }

        public List<(string username, string content, string time)> GetComments(int movieId)
        {
            var list = new List<(string, string, string)>();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = "SELECT Username, Content, CreatedAt FROM Comments WHERE MovieId = @mid ORDER BY Id DESC LIMIT 50";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@mid", movieId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            return list;
        }

        public void AddHistory(string username, int movieId, string movieTitle)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = "INSERT INTO History (Username, MovieId, MovieTitle, WatchedAt) VALUES (@user, @mid, @title, @time)";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@user", username);
            cmd.Parameters.AddWithValue("@mid", movieId);
            cmd.Parameters.AddWithValue("@title", movieTitle);
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("HH:mm dd/MM/yyyy"));
            cmd.ExecuteNonQuery();
        }

        public List<(string title, string watchedAt, int movieId, string category, string poster)> GetHistory(string username)
        {
            var list = new List<(string, string, int, string, string)>();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = @"SELECT h.MovieTitle, h.WatchedAt, h.MovieId, m.Category, m.Poster 
                           FROM History h 
                           LEFT JOIN Movies m ON h.MovieId = m.Id
                           WHERE h.Username=@user 
                           ORDER BY h.Id DESC LIMIT 20";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@user", username);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.IsDBNull(3) ? "" : reader.GetString(3),
                    reader.IsDBNull(4) ? "" : reader.GetString(4)
                ));
            return list;
        }

        public void AddReport(string reporter, string reported, int movieId, string movieTitle, string commentContent, string reason)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = @"INSERT INTO Reports (Reporter, Reported, MovieId, MovieTitle, CommentContent, Reason, ReportedAt)
                           VALUES (@reporter, @reported, @mid, @title, @content, @reason, @time)";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@reporter", reporter);
            cmd.Parameters.AddWithValue("@reported", reported);
            cmd.Parameters.AddWithValue("@mid", movieId);
            cmd.Parameters.AddWithValue("@title", movieTitle);
            cmd.Parameters.AddWithValue("@content", commentContent);
            cmd.Parameters.AddWithValue("@reason", reason);
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("HH:mm dd/MM/yyyy"));
            cmd.ExecuteNonQuery();
        }

        public List<(string reporter, string reported, string movieTitle, string commentContent, string reason, string reportedAt)> GetReports()
        {
            var list = new List<(string, string, string, string, string, string)>();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = "SELECT Reporter, Reported, MovieTitle, CommentContent, Reason, ReportedAt FROM Reports ORDER BY Id DESC";
            using var cmd = new SqliteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2),
                          reader.GetString(3), reader.GetString(4), reader.GetString(5)));
            return list;
        }
        public string GetUserStatus(string username)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT Status FROM Users WHERE Username=@u", conn);
            cmd.Parameters.AddWithValue("@u", username);
            var result = cmd.ExecuteScalar();
            return result?.ToString() ?? "normal";
        }

        public void SetUserStatus(string username, string status)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            if (status == "warned")
            {
                using var cmd = new SqliteCommand(
                    "UPDATE Users SET Status=@s, WarnCount=WarnCount+1 WHERE Username=@u", conn);
                cmd.Parameters.AddWithValue("@s", status);
                cmd.Parameters.AddWithValue("@u", username);
                cmd.ExecuteNonQuery();
            }
            else
            {
                using var cmd = new SqliteCommand(
                    "UPDATE Users SET Status=@s WHERE Username=@u", conn);
                cmd.Parameters.AddWithValue("@s", status);
                cmd.Parameters.AddWithValue("@u", username);
                cmd.ExecuteNonQuery();
            }
        }

        public int GetWarnCount(string username)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT WarnCount FROM Users WHERE Username=@u", conn);
            cmd.Parameters.AddWithValue("@u", username);
            var result = cmd.ExecuteScalar();
            return result == null ? 0 : Convert.ToInt32(result);
        }

        public void DeleteReport(int id)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = new SqliteCommand(
                "DELETE FROM Reports WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public List<(int id, string reporter, string reported, string movieTitle, string commentContent, string reason, string reportedAt)> GetReportsWithId()
        {
            var list = new List<(int, string, string, string, string, string, string)>();
            using var conn = new SqliteConnection(  $"Data Source={_dbPath}");
            conn.Open();
            string sql = "SELECT Id, Reporter, Reported, MovieTitle, CommentContent, Reason, ReportedAt FROM Reports ORDER BY Id DESC";
            using var cmd = new SqliteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                          reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
            return list;
        }
        public int GetMovieViewCount(int movieId)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM History WHERE MovieId=@mid", conn);
            cmd.Parameters.AddWithValue("@mid", movieId);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    public class Movie
    {
    public int Id { get; set; }
    public string Title { get; set; }
    public string FilePath { get; set; }
    public string Category { get; set; }
    public string Poster { get; set; }
    public string Description { get; set; }
    public int Duration { get; set; }
}
}