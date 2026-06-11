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
        public DbSet<Wallet> Wallets { get; set; } = null!;
        public DbSet<Leaderboard> Leaderboards { get; set; } = null!;
        public DbSet<GameConfig> GameConfigs { get; set; } = null!;
        public DbSet<AdminLog> AdminLogs { get; set; } = null!;
        public DbSet<GameItem> GameItems { get; set; } = null!;
        public DbSet<PlayerInventory> PlayerInventories { get; set; } = null!;
        public DbSet<Transaction> Transactions { get; set; } = null!;
        public DbSet<PlayerHero> PlayerHeroes { get; set; } = null!;
        public DbSet<HeroEquipment> HeroEquipments { get; set; } = null!;
        public DbSet<MailboxItem> MailboxItems { get; set; } = null!;
        public DbSet<PlayerBroadcastClaim> PlayerBroadcastClaims { get; set; } = null!;
        public DbSet<MarketplaceListing> MarketplaceListings { get; set; } = null!;
        public DbSet<MarketplaceReservation> MarketplaceReservations { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. Cấu hình bảng Profiles
            modelBuilder.Entity<Profile>()
                .Property(p => p.CreatedAt)
                .HasColumnType("timestamp with time zone");

            // 1.1 Cấu hình bảng Wallets (Ví tiền tệ một-một với Profiles)
            modelBuilder.Entity<Wallet>()
                .Property(w => w.UpdatedAt)
                .HasColumnType("timestamp with time zone");

            modelBuilder.Entity<Wallet>()
                .HasOne(w => w.Profile)
                .WithOne()
                .HasForeignKey<Wallet>(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);

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
                .HasFilter("\"deleted_at\" IS NULL")
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
                .Property(t => t.UpdatedAt)
                .HasColumnType("timestamp with time zone");

            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.PlayerProfile)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // 8. Cấu hình bảng PlayerHero
            modelBuilder.Entity<PlayerHero>()
                .Property(ph => ph.UnlockedAt)
                .HasColumnType("timestamp with time zone");

            modelBuilder.Entity<PlayerHero>()
                .HasIndex(ph => new { ph.UserId, ph.HeroKey })
                .IsUnique()
                .HasDatabaseName("unique_user_hero");

            modelBuilder.Entity<PlayerHero>()
                .HasOne(ph => ph.PlayerProfile)
                .WithMany()
                .HasForeignKey(ph => ph.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // 9. Cấu hình bảng HeroEquipment
            modelBuilder.Entity<HeroEquipment>()
                .Property(he => he.EquippedAt)
                .HasColumnType("timestamp with time zone");

            modelBuilder.Entity<HeroEquipment>()
                .HasIndex(he => new { he.PlayerHeroId, he.SlotType })
                .IsUnique()
                .HasFilter("\"deleted_at\" IS NULL")
                .HasDatabaseName("unique_hero_slot");

            modelBuilder.Entity<HeroEquipment>()
                .HasOne(he => he.HeroDetails)
                .WithMany()
                .HasForeignKey(he => he.PlayerHeroId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<HeroEquipment>()
                .HasOne(he => he.InventoryItem)
                .WithMany()
                .HasForeignKey(he => he.InventoryItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // 10. Cấu hình bảng MailboxItem
            modelBuilder.Entity<MailboxItem>()
                .Property(m => m.CreatedAt)
                .HasColumnType("timestamp with time zone");

            modelBuilder.Entity<MailboxItem>()
                .Property(m => m.ExpiredAt)
                .HasColumnType("timestamp with time zone");

            modelBuilder.Entity<MailboxItem>()
                .HasOne(m => m.ReceiverProfile)
                .WithMany()
                .HasForeignKey(m => m.ReceiverId)
                .OnDelete(DeleteBehavior.Cascade);

            // 11. Cấu hình bảng PlayerBroadcastClaim
            modelBuilder.Entity<PlayerBroadcastClaim>()
                .Property(c => c.ClaimedAt)
                .HasColumnType("timestamp with time zone");

            modelBuilder.Entity<PlayerBroadcastClaim>()
                .HasIndex(c => new { c.UserId, c.MailId })
                .IsUnique()
                .HasDatabaseName("unique_user_broadcast_claim");

            modelBuilder.Entity<PlayerBroadcastClaim>()
                .HasOne(c => c.PlayerProfile)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PlayerBroadcastClaim>()
                .HasOne(c => c.MailboxItem)
                .WithMany()
                .HasForeignKey(c => c.MailId)
                .OnDelete(DeleteBehavior.Cascade);

            // 12. Cấu hình bảng MarketplaceListing
            modelBuilder.Entity<MarketplaceListing>()
                .Property(ml => ml.CreatedAt)
                .HasColumnType("timestamp with time zone");

            modelBuilder.Entity<MarketplaceListing>()
                .Property(ml => ml.UpdatedAt)
                .HasColumnType("timestamp with time zone");

            modelBuilder.Entity<MarketplaceListing>()
                .HasOne(ml => ml.SellerProfile)
                .WithMany()
                .HasForeignKey(ml => ml.SellerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<MarketplaceListing>()
                .HasOne(ml => ml.BuyerProfile)
                .WithMany()
                .HasForeignKey(ml => ml.BuyerId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<MarketplaceListing>()
                .HasOne(ml => ml.InventoryItem)
                .WithMany()
                .HasForeignKey(ml => ml.InventoryItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // 13. Cấu hình bảng MarketplaceReservation
            modelBuilder.Entity<MarketplaceReservation>()
                .Property(r => r.ReservedAt)
                .HasColumnType("timestamp with time zone");

            modelBuilder.Entity<MarketplaceReservation>()
                .HasIndex(r => r.ListingId)
                .IsUnique()
                .HasDatabaseName("unique_listing_reservation");

            modelBuilder.Entity<MarketplaceReservation>()
                .HasOne(r => r.Listing)
                .WithOne()
                .HasForeignKey<MarketplaceReservation>(r => r.ListingId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<MarketplaceReservation>()
                .HasOne(r => r.Profile)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // 14. Thiết lập Global Query Filters cho Soft Deletes
            modelBuilder.Entity<PlayerInventory>().HasQueryFilter(pi => pi.DeletedAt == null);
            modelBuilder.Entity<HeroEquipment>().HasQueryFilter(he => he.DeletedAt == null);
            modelBuilder.Entity<MailboxItem>().HasQueryFilter(m => m.DeletedAt == null);
            modelBuilder.Entity<MarketplaceListing>().HasQueryFilter(ml => ml.DeletedAt == null);
        }
    }
}
