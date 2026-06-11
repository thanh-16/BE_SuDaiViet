# Hướng Dẫn Thiết Lập & Tài Liệu Database - Sử Đại Việt Backend

Tài liệu này lưu trữ cấu trúc cơ sở dữ liệu PostgreSQL (chạy trên Supabase BaaS) và định hướng tích hợp cho dự án game **Sử Đại Việt (Tây Sơn Chapter)**.

---

## 🛠️ 1. SCRIPT SQL KHỞI TẠO HOÀN CHỈNH (database_setup.sql)

Toàn bộ mã lệnh khởi tạo, cấu hình bảo mật RLS, các Trigger tự động đồng bộ và Stored Procedures nguyên tử được duy trì đầy đủ trong file [database_setup.sql](file:///d:/Exe202/t%C3%A2y-s%C6%A1n/S%E1%BB%AD%20%C4%90%E1%BA%A1i%20Vi%E1%BB%87t/S%E1%BB%AD%20%C4%90%E1%BA%A1i%20Vi%E1%BB%87t/database_setup.sql).

### Các điểm nhấn kiến trúc nâng cao đã triển khai:
1.  **Phân tách Ví Tiền tệ độc lập (`wallets`)**: Tách khỏi bảng `profiles` để loại bỏ 100% rủi ro tranh chấp tài nguyên (race condition) dưới tải lớn 10k CCU.
2.  **Chỉ mục một phần chống xóa mềm (Soft-Delete Partial Indexes)**:
    - `player_inventories` unique trên `(user_id, item_id) WHERE deleted_at IS NULL`.
    - `hero_equipments` unique trên `(player_hero_id, slot_type) WHERE deleted_at IS NULL`.
    Điều này cho phép người chơi mua lại hoặc trang bị lại vào slot cũ sau khi đã xóa/tháo đồ mà không bị lỗi trùng khóa (vi phạm UNIQUE constraint).
3.  **Hệ thống Phân vùng động theo yêu cầu (On-Demand Partitioning Triggers)**:
    - Bật phân vùng theo thời gian hàng tháng cho các bảng log cực lớn (`mailbox`, `battle_logs`, `player_audit_logs`, `transactions`).
    - Gắn trigger `BEFORE INSERT` tự động phát hiện và sinh bảng con phân vùng mới theo tháng tại thời gian thực (real-time) khi có dữ liệu mới chèn vào. Tránh tạo sẵn hàng trăm bảng con gây rác giao diện Supabase.
4.  **Bảo mật RLS đa tầng chống lách luật (RLS Enforcement on Partitions)**:
    - Tự động áp dụng `ALTER TABLE ... ENABLE ROW LEVEL SECURITY` trực tiếp cho tất cả các phân vùng con được sinh ra để bảo vệ an toàn dữ liệu từ client.
    - Khóa chặt quyền cập nhật `leaderboard` từ phía người dùng đã đăng nhập (chỉ cho phép SELECT công khai). Mọi thay đổi điểm số phải đi qua Service Backend để chống gian lận.

---

## 📊 2. CHI TIẾT 21 BẢNG TRONG HỆ THỐNG DATABASE

### Nhóm 1: Hệ thống Người chơi & Tiền tệ
*   **`profiles`**: Hồ sơ người chơi (Cấp độ, kinh nghiệm, vai trò). Có trigger cập nhật `updated_at` tự động.
*   **`wallets`**: Ví vàng (`gold_balance`) và ngọc (`gem_balance`). Có cột `version` cho optimistic locking và trigger tự động tạo ví mới ngay khi tạo hồ sơ.
*   **`leaderboard`**: Bảng vinh danh thành tích anh kiệt. Có trigger đồng bộ tên tự động từ `profiles`.

### Nhóm 2: Hệ thống RPG & Kho đồ
*   **`game_items`**: Danh mục toàn bộ vật phẩm trong game (binh khí, đan dược, ngoại trang).
*   **`player_inventories`**: Rương đồ sở hữu của người chơi. Có trigger `auto_soft_delete_inventory` tự động đánh dấu `deleted_at = now()` khi số lượng vật phẩm giảm về 0.
*   **`player_heroes`**: Danh sách tướng lĩnh Tây Sơn sở hữu (`hue`, `nhac`, `lu`).
*   **`hero_equipments`**: Các trang bị đang mặc trên người tướng (Weapon, Armor, Accessory).
*   **`mailbox`**: Bảng cha phân vùng quản lý thư cá nhân và thư sự kiện đính kèm quà tặng.
*   **`player_broadcast_claims`**: Nhật ký nhận thưởng sự kiện chung để chống nhận trùng quà.

### Nhóm 3: Hệ thống Chợ giao dịch P2P
*   **`marketplace_listings`**: Tin rao bán vật phẩm giữa người chơi.
*   **`marketplace_reservations`**: Đặt chỗ giữ hàng tạm thời trên chợ (Tự hủy sau 5 phút và hồi trạng thái tin rao bán về `'Active'`).

### Nhóm 4: Hệ thống Nhiệm vụ & Thành tựu
*   **`game_quests`**: Danh mục tĩnh chứa các nhiệm vụ chính thức của game.
*   **`quest_progress`**: Tiến trình làm nhiệm vụ của nghĩa sĩ Tây Sơn.
*   **`game_achievements`**: Danh mục tĩnh chứa các thành tựu vinh danh.
*   **`achievements`**: Danh sách thành tựu người chơi cụ thể đã đạt được.

### Nhóm 5: Hệ thống Log gameplay & Kiểm toán (Phân vùng)
*   **`game_sessions`**: Lịch sử phiên chơi game.
*   **`battle_logs`**: Nhật ký trận đánh chi tiết (Thắng/Thua, sát thương, điểm số, kinh nghiệm).
*   **`transactions`**: Nhật ký giao dịch ví vàng/ngọc (Mua shop, bán chợ, claim mail).
*   **`admin_logs`**: Nhật ký kiểm toán thao tác quản trị viên.
*   **`player_audit_logs`**: Nhật ký bảo mật người chơi chống gian lận.

---

## 🧭 3. HƯỚNG DẪN KẾT NỐI TÍCH HỢP CHO EF CORE (C# BACKEND)

1.  **Cấu hình Soft-Delete Global Query Filters**:
    Đăng ký bộ lọc tự động lọc bỏ các bản ghi đã xóa mềm trong phương thức `OnModelCreating` của `ApplicationDbContext.cs`:
    ```csharp
    modelBuilder.Entity<PlayerInventory>().HasQueryFilter(pi => pi.DeletedAt == null);
    modelBuilder.Entity<HeroEquipment>().HasQueryFilter(he => he.DeletedAt == null);
    modelBuilder.Entity<MailboxItem>().HasQueryFilter(m => m.DeletedAt == null);
    modelBuilder.Entity<MarketplaceListing>().HasQueryFilter(ml => ml.DeletedAt == null);
    ```

2.  **Đồng bộ Khớp Chỉ mục một phần (HasFilter)**:
    Để khớp hoàn hảo với PostgreSQL, bắt buộc phải khai báo filter `deleted_at IS NULL` cho các chỉ mục duy nhất:
    ```csharp
    modelBuilder.Entity<PlayerInventory>()
        .HasIndex(pi => new { pi.UserId, pi.ItemId })
        .IsUnique()
        .HasFilter("\"deleted_at\" IS NULL")
        .HasDatabaseName("unique_user_item");

    modelBuilder.Entity<HeroEquipment>()
        .HasIndex(he => new { he.PlayerHeroId, he.SlotType })
        .IsUnique()
        .HasFilter("\"deleted_at\" IS NULL")
        .HasDatabaseName("unique_hero_slot");
    ```

3.  **Sử dụng Stored Procedure Nguyên tử**:
    Luôn gọi Stored Procedure thông qua Raw SQL của EF Core thay vì xử lý nhiều câu lệnh rải rác ở C# để tránh race condition và tối ưu hóa hiệu năng:
    ```csharp
    var result = await _context.Database
        .SqlQueryRaw<string>("SELECT public.buy_marketplace_item({0}, {1}) as \"Value\"", buyerId, listingId)
        .ToListAsync();
    ```
