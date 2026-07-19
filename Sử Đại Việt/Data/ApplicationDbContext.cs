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

            // 15. Dữ liệu mẫu (Seed Data) cho tất cả các vật phẩm và trang phục trong game
            var baseDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            modelBuilder.Entity<GameItem>().HasData(
                new GameItem { Id = "tran_thao_son_tra", Name = "Trân Thảo Sơn Trà", Description = "Hồi đầy 100% sinh lực cho nghĩa sĩ ngay tức khắc.", PriceGold = 80, PriceGem = 0, PriceVnd = 0, ItemType = "Consumable", Attributes = "{\"slot\": \"none\", \"effect\": \"heal\", \"value\": 100.0, \"duration\": 0.0}", CreatedAt = baseDate },
                new GameItem { Id = "linh_dan_hoi_khi", Name = "Linh Đan Hồi Khí", Description = "Nạp đầy Nộ Khí để tung tuyệt kỹ liền tay.", PriceGold = 60, PriceGem = 0, PriceVnd = 0, ItemType = "Consumable", Attributes = "{\"slot\": \"none\", \"effect\": \"rage\", \"value\": 100.0, \"duration\": 0.0}", CreatedAt = baseDate },
                new GameItem { Id = "ruou_de_quy_nhon", Name = "Rượu Đế Quy Nhơn", Description = "Tăng 30% sát thương trong 30 giây xung trận.", PriceGold = 200, PriceGem = 0, PriceVnd = 0, ItemType = "Consumable", Attributes = "{\"slot\": \"none\", \"effect\": \"dmg_buff\", \"value\": 0.30, \"duration\": 30.0}", CreatedAt = baseDate },
                new GameItem { Id = "khien_dong_son", Name = "Khiên Đồng Đông Sơn", Description = "Lá chắn đồng bất hoại, miễn nhiễm sát thương 12 giây.", PriceGold = 250, PriceGem = 0, PriceVnd = 0, ItemType = "Consumable", Attributes = "{\"slot\": \"none\", \"effect\": \"shield\", \"value\": 0.0, \"duration\": 12.0}", CreatedAt = baseDate },
                new GameItem { Id = "co_dao_phuc_sinh", Name = "Cờ Đào Phục Sinh", Description = "Hồi sinh tại trận một lần (50% máu) khi nghĩa sĩ gục ngã.", PriceGold = 500, PriceGem = 0, PriceVnd = 0, ItemType = "Consumable", Attributes = "{\"slot\": \"none\", \"effect\": \"revive\", \"value\": 0.0, \"duration\": 0.0}", CreatedAt = baseDate },
                
                new GameItem { Id = "hoang_de_co_dao", Name = "Hoàng Đế Cổ Đao", Description = "Đại đao hoàng triều — vĩnh viễn +12% sát thương.", PriceGold = 640, PriceGem = 0, PriceVnd = 0, ItemType = "Equipment", Attributes = "{\"slot\": \"weapon\", \"effect\": \"equip_dmg\", \"value\": 0.12, \"duration\": 0.0}", CreatedAt = baseDate },
                new GameItem { Id = "co_kiem_binh_dinh", Name = "Cổ Kiếm Bình Định", Description = "Bảo kiếm khai quốc — vĩnh viễn +18% sát thương.", PriceGold = 1200, PriceGem = 0, PriceVnd = 0, ItemType = "Equipment", Attributes = "{\"slot\": \"weapon\", \"effect\": \"equip_dmg\", \"value\": 0.18, \"duration\": 0.0}", CreatedAt = baseDate },
                new GameItem { Id = "thiet_thuong_tayson", Name = "Thiết Thương Tây Sơn", Description = "Trường thương bọc sắt — vĩnh viễn +25% sát thương.", PriceGold = 2600, PriceGem = 0, PriceVnd = 0, ItemType = "Equipment", Attributes = "{\"slot\": \"weapon\", \"effect\": \"equip_dmg\", \"value\": 0.25, \"duration\": 0.0}", CreatedAt = baseDate },
                new GameItem { Id = "song_thiet_con", Name = "Song Thiết Côn", Description = "Côn sắt song đầu — vĩnh viễn +32% sát thương.", PriceGold = 4200, PriceGem = 0, PriceVnd = 0, ItemType = "Equipment", Attributes = "{\"slot\": \"weapon\", \"effect\": \"equip_dmg\", \"value\": 0.32, \"duration\": 0.0}", CreatedAt = baseDate },
                new GameItem { Id = "than_kinh_tayson", Name = "Tây Sơn Thần Kính", Description = "Thần khí tối thượng — vĩnh viễn +45% sát thương.", PriceGold = 0, PriceGem = 300, PriceVnd = 0, ItemType = "Equipment", Attributes = "{\"slot\": \"weapon\", \"effect\": \"equip_dmg\", \"value\": 0.45, \"duration\": 0.0}", CreatedAt = baseDate },
                
                new GameItem { Id = "an_ngoc_hoang_de", Name = "Ấn Ngọc Hoàng Đế", Description = "Ấn ngọc danh giá — biểu tượng bậc đế vương (trang trí hồ sơ).", PriceGold = 0, PriceGem = 120, PriceVnd = 0, ItemType = "Cosmetic", Attributes = "{\"slot\": \"none\", \"effect\": \"none\", \"value\": 0.0, \"duration\": 0.0}", CreatedAt = baseDate },
                
                new GameItem { Id = "giap_da_tayson", Name = "Tây Sơn Giáp Da", Description = "Áo giáp da dẻo dai — tăng 20% sinh lực tối đa.", PriceGold = 1000, PriceGem = 0, PriceVnd = 0, ItemType = "Equipment", Attributes = "{\"slot\": \"armor\", \"effect\": \"equip_hp\", \"value\": 0.20, \"duration\": 0.0}", CreatedAt = baseDate },
                new GameItem { Id = "thiet_giap_tayson", Name = "Tây Sơn Thiết Giáp", Description = "Giáp sắt kiên cố của nghĩa quân — tăng 40% sinh lực tối đa.", PriceGold = 2500, PriceGem = 0, PriceVnd = 0, ItemType = "Equipment", Attributes = "{\"slot\": \"armor\", \"effect\": \"equip_hp\", \"value\": 0.40, \"duration\": 0.0}", CreatedAt = baseDate },
                new GameItem { Id = "hoang_gia_chien_giap", Name = "Hoàng Gia Chiến Giáp", Description = "Chiến giáp hoàng triều đúc bằng đồng quý — tăng 70% sinh lực tối đa.", PriceGold = 5000, PriceGem = 0, PriceVnd = 0, ItemType = "Equipment", Attributes = "{\"slot\": \"armor\", \"effect\": \"equip_hp\", \"value\": 0.70, \"duration\": 0.0}", CreatedAt = baseDate },
                new GameItem { Id = "bao_tinh_giap", Name = "Bảo Tinh Giáp", Description = "Thần giáp bảo thạch hộ thân — tăng 100% sinh lực tối đa.", PriceGold = 0, PriceGem = 400, PriceVnd = 0, ItemType = "Equipment", Attributes = "{\"slot\": \"armor\", \"effect\": \"equip_hp\", \"value\": 1.00, \"duration\": 0.0}", CreatedAt = baseDate },
                new GameItem { Id = "equipment_weapon_long_tinh_dao", Name = "Long Tinh Đao", Description = "Đại đao khắc họa long hình tôn nghiêm — vĩnh viễn +35% sát thương.", PriceGold = 5000, PriceGem = 150, PriceVnd = 0, ItemType = "Equipment", Attributes = "{\"slot\": \"weapon\", \"effect\": \"equip_dmg\", \"value\": 0.35, \"duration\": 0.0, \"icon_path\": \"res://assets/sprites/items/long_tinh_dao.png\"}", CreatedAt = baseDate },
                new GameItem { Id = "equipment_armor_hac_ho_giap", Name = "Hắc Hổ Thiết Giáp", Description = "Thiết giáp khắc họa hình hổ đen dũng mãnh — tăng 55% sinh lực tối đa.", PriceGold = 3500, PriceGem = 100, PriceVnd = 0, ItemType = "Equipment", Attributes = "{\"slot\": \"armor\", \"effect\": \"equip_hp\", \"value\": 0.55, \"duration\": 0.0, \"icon_path\": \"res://assets/sprites/items/hac_ho_giap.png\"}", CreatedAt = baseDate },
                new GameItem { Id = "equipment_weapon_than_co_thuong", Name = "Thần Cơ Thương", Description = "Bảo khí súng hỏa mai Thần Cơ cải tiến — vĩnh viễn +40% sát thương.", PriceGold = 4500, PriceGem = 200, PriceVnd = 0, ItemType = "Equipment", Attributes = "{\"slot\": \"weapon\", \"effect\": \"equip_dmg\", \"value\": 0.40, \"duration\": 0.0, \"icon_path\": \"res://assets/sprites/items/than_co_thuong.png\"}", CreatedAt = baseDate }
            );
        }
    }
}
