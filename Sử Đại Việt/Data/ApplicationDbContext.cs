using Microsoft.EntityFrameworkCore;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Profile> Profiles { get; set; } = null!;
        public DbSet<Leaderboard> Leaderboards { get; set; } = null!;
        public DbSet<GameConfig> GameConfigs { get; set; } = null!;
        public DbSet<AdminLog> AdminLogs { get; set; } = null!;
        public DbSet<GameItem> GameItems { get; set; } = null!;
        public DbSet<PlayerInventory> PlayerInventories { get; set; } = null!;
        public DbSet<Transaction> Transactions { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. Cấu hình bảng Profiles
            modelBuilder.Entity<Profile>()
                .Property(p => p.CreatedAt)
                .HasColumnType("timestamp with time zone");

            // 2. Cấu hình bảng Leaderboard
            modelBuilder.Entity<Leaderboard>()
                .Property(l => l.UpdatedAt)
                .HasColumnType("timestamp with time zone");

            // Thiết lập Index cho trường Score để tối ưu hoá tốc độ lọc điểm số giảm dần
            modelBuilder.Entity<Leaderboard>()
                .HasIndex(l => l.Score)
                .HasDatabaseName("leaderboard_score_idx");

            // Thiết lập Index duy nhất cho UserId để đảm bảo mỗi người chơi chỉ xuất hiện 1 lần trên bảng vinh danh
            modelBuilder.Entity<Leaderboard>()
                .HasIndex(l => l.UserId)
                .IsUnique()
                .HasDatabaseName("leaderboard_user_id_unique_idx");

            // Thiết lập mối liên kết khoá ngoại rõ ràng giữa Leaderboard và Profiles
            modelBuilder.Entity<Leaderboard>()
                .HasOne(l => l.Profile)
                .WithMany()
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // 3. Cấu hình bảng GameConfig
            modelBuilder.Entity<GameConfig>()
                .Property(c => c.UpdatedAt)
                .HasColumnType("timestamp with time zone");

            // 4. Cấu hình bảng AdminLog
            modelBuilder.Entity<AdminLog>()
                .Property(al => al.CreatedAt)
                .HasColumnType("timestamp with time zone");

            // 5. Cấu hình bảng GameItem
            modelBuilder.Entity<GameItem>()
                .Property(gi => gi.CreatedAt)
                .HasColumnType("timestamp with time zone");

            // 6. Cấu hình bảng PlayerInventory
            modelBuilder.Entity<PlayerInventory>()
                .Property(pi => pi.AcquiredAt)
                .HasColumnType("timestamp with time zone");

            // Chỉ số duy nhất phức hợp để tránh trùng lắp vật phẩm sở hữu (chỉ tăng số lượng)
            modelBuilder.Entity<PlayerInventory>()
                .HasIndex(pi => new { pi.UserId, pi.ItemId })
                .IsUnique()
                .HasDatabaseName("unique_user_item");

            modelBuilder.Entity<PlayerInventory>()
                .HasOne(pi => pi.PlayerProfile)
                .WithMany()
                .HasForeignKey(pi => pi.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PlayerInventory>()
                .HasOne(pi => pi.ItemDetails)
                .WithMany()
                .HasForeignKey(pi => pi.ItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // 7. Cấu hình bảng Transaction
            modelBuilder.Entity<Transaction>()
                .Property(t => t.CreatedAt)
                .HasColumnType("timestamp with time zone");

            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.PlayerProfile)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
