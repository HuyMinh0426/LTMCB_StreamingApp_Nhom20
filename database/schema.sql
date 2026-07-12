-- ================================================
-- MINHFLIX Database Schema for PostgreSQL
-- Deploy trên Azure Cloud, region Indonesia Central
-- Ngày cập nhật: 12/7/2026
-- ================================================

-- Bảng 1: Users - Tài khoản người dùng
CREATE TABLE "Users" (
    "UserID" SERIAL PRIMARY KEY,
    "Username" VARCHAR(100) UNIQUE NOT NULL,
    "PasswordHash" TEXT NOT NULL,
    "Salt" TEXT NOT NULL,
    "Status" VARCHAR(20) DEFAULT 'normal',
    "WarnCount" INTEGER DEFAULT 0
);

-- Bảng 2: Movies - Danh sách phim
CREATE TABLE "Movies" (
    "MovieID" SERIAL PRIMARY KEY,
    "Title" VARCHAR(200) NOT NULL,
    "Category" VARCHAR(100),
    "PosterPath" TEXT,
    "VideoPath" TEXT,
    "Description" TEXT
);

-- Bảng 3: Comments - Bình luận
CREATE TABLE "Comments" (
    "CommentID" SERIAL PRIMARY KEY,
    "MovieID" INTEGER NOT NULL,
    "Username" VARCHAR(100) NOT NULL,
    "Content" TEXT NOT NULL,
    "CreatedAt" TIMESTAMP DEFAULT NOW()
);

-- Bảng 4: History - Lịch sử xem
CREATE TABLE "History" (
    "HistoryID" SERIAL PRIMARY KEY,
    "Username" VARCHAR(100) NOT NULL,
    "MovieID" INTEGER NOT NULL,
    "MovieTitle" VARCHAR(200),
    "WatchedAt" TIMESTAMP DEFAULT NOW()
);

-- Bảng 5: Reports - Báo cáo vi phạm
CREATE TABLE "Reports" (
    "ReportID" SERIAL PRIMARY KEY,
    "Reporter" VARCHAR(100) NOT NULL,
    "Reported" VARCHAR(100) NOT NULL,
    "MovieID" INTEGER,
    "MovieTitle" VARCHAR(200),
    "CommentContent" TEXT,
    "Reason" VARCHAR(200),
    "ReportedAt" TIMESTAMP DEFAULT NOW()
);