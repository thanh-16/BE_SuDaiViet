-- =====================================================================
-- SCRIPT SQL TOÀN DIỆN & BẢO MẬT: HỆ THỐNG DATABASE "SỬ ĐẠI VIỆT" 
-- Hỗ trợ: Email, Số điện thoại (Phone OTP), Đăng nhập MXH (Google, Facebook, Apple)
-- Tương thích hoàn hảo với các tính năng tối cao của Web Admin & RPG Systems
-- =====================================================================

-- 0. KHỞI TẠO BẢO MẬT & ĐỘNG CƠ
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS public;

-- Tạo Hàm dùng chung để tự động cập nhật thời gian thay đổi (updated_at)
CREATE OR REPLACE FUNCTION public.update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = timezone('utc'::text, now());
    RETURN NEW;
END;
$$ language 'plpgsql';


-- ---------------------------------------------------------------------
-- 1. BẢNG HỒ SƠ NGƯỜI CHƠI (profiles)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.profiles (
    id UUID REFERENCES auth.users ON DELETE CASCADE PRIMARY KEY,  -- Liên kết trực tiếp sang tài khoản của Supabase Auth
    email VARCHAR(255) NULL,                                      -- Địa chỉ Email (Cho phép NULL)
    phone VARCHAR(50) NULL,                                       -- Hỗ trợ đăng nhập qua Số Điện Thoại (Cho phép NULL)
    display_name VARCHAR(50) DEFAULT 'Nghĩa Sĩ' NOT NULL,         -- Tên hiển thị trong game
    avatar_url VARCHAR(500) NULL,                                 -- Ảnh đại diện lấy từ Google/Facebook/Apple
    is_banned BOOLEAN DEFAULT false NOT NULL,                     -- Trạng thái khóa tài khoản người chơi
    role VARCHAR(20) DEFAULT 'player' NOT NULL,                   -- Vai trò trong game: 'player' (người chơi) hoặc 'admin' (quản trị viên)
    level INTEGER DEFAULT 1 NOT NULL,                             -- Cấp độ người chơi
    experience INTEGER DEFAULT 0 NOT NULL,                         -- Điểm kinh nghiệm tích lũy
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    CONSTRAINT chk_profiles_role CHECK (role IN ('player', 'admin'))
);

-- Kích hoạt RLS (Row Level Security) bảo mật mức dòng
ALTER TABLE public.profiles ENABLE ROW LEVEL SECURITY;

-- Tạo các chính sách bảo mật cho Profiles
DROP POLICY IF EXISTS "Cho phép xem hồ sơ công khai" ON public.profiles;
CREATE POLICY "Cho phép xem hồ sơ công khai" ON public.profiles FOR SELECT USING (true);

DROP POLICY IF EXISTS "Chỉ người dùng mới được sửa hồ sơ của mình" ON public.profiles;
CREATE POLICY "Chỉ người dùng mới được sửa hồ sơ của mình" ON public.profiles FOR UPDATE USING (auth.uid() = id);

-- Trigger tự động cập nhật updated_at
DROP TRIGGER IF EXISTS trigger_update_profiles_updated_at ON public.profiles;
CREATE TRIGGER trigger_update_profiles_updated_at 
    BEFORE UPDATE ON public.profiles 
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


-- ---------------------------------------------------------------------
-- 2. BẢNG VÍ TIỀN TỆ ĐỘC LẬP (wallets) - NGĂN CHẶN TRANH CHẤP TÀI NGUYÊN
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.wallets (
    user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE PRIMARY KEY,
    gold_balance INTEGER DEFAULT 0 NOT NULL,
    gem_balance INTEGER DEFAULT 0 NOT NULL,
    version BIGINT DEFAULT 1 NOT NULL,                            -- Dùng cho Optimistic Locking ở tầng code
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    CONSTRAINT chk_positive_gold CHECK (gold_balance >= 0),
    CONSTRAINT chk_positive_gem CHECK (gem_balance >= 0)
);

-- Kích hoạt RLS bảo mật ví
ALTER TABLE public.wallets ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Cho phép chủ sở hữu ví đọc số dư" ON public.wallets;
CREATE POLICY "Cho phép chủ sở hữu ví đọc số dư" ON public.wallets FOR SELECT TO authenticated USING (auth.uid() = user_id);

DROP POLICY IF EXISTS "Cấm người chơi cập nhật trực tiếp số dư ví" ON public.wallets;
CREATE POLICY "Cấm người chơi cập nhật trực tiếp số dư ví" ON public.wallets FOR UPDATE TO authenticated USING (false);

-- Trigger tự động tạo ví tiền khi hồ sơ người chơi mới được tạo thành công
CREATE OR REPLACE FUNCTION public.create_wallet_for_new_user()
RETURNS trigger AS $$
BEGIN
    INSERT INTO public.wallets (user_id, gold_balance, gem_balance)
    VALUES (new.id, 0, 0)
    ON CONFLICT (user_id) DO NOTHING;
    RETURN new;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

DROP TRIGGER IF EXISTS on_profile_created_create_wallet ON public.profiles;
CREATE TRIGGER on_profile_created_create_wallet
    AFTER INSERT ON public.profiles
    FOR EACH ROW EXECUTE FUNCTION public.create_wallet_for_new_user();

-- Trigger tự động cập nhật updated_at của wallets
DROP TRIGGER IF EXISTS trigger_update_wallets_updated_at ON public.wallets;
CREATE TRIGGER trigger_update_wallets_updated_at 
    BEFORE UPDATE ON public.wallets 
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


-- ---------------------------------------------------------------------
-- 3. BẢNG XẾP HẠNG TRỰC TUYẾN (leaderboard) - BẢO MẬT & ĐỒNG BỘ
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.leaderboard (
    id BIGSERIAL PRIMARY KEY,                                      -- Khóa chính tự tăng
    user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL, -- Liên kết đến hồ sơ người chơi
    username VARCHAR(50) NOT NULL,                                 -- Tên người chơi hiển thị trên bảng xếp hạng
    score INTEGER DEFAULT 0 NOT NULL,                             -- Điểm số cao nhất lập chiến công
    stage_reached VARCHAR(50) DEFAULT 'Ải 1' NOT NULL,             -- Ải cao nhất đã vượt qua
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    CONSTRAINT leaderboard_user_id_unique UNIQUE(user_id)
);

-- Index sắp xếp điểm số để truy vấn Top 10 siêu tốc
CREATE INDEX IF NOT EXISTS leaderboard_score_idx ON public.leaderboard (score DESC);

-- Kích hoạt RLS bảo mật cho bảng leaderboard (Chỉ xem, cấm ghi sửa trực tiếp)
ALTER TABLE public.leaderboard ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Cho phép tất cả mọi người xem Bảng Xếp Hạng" ON public.leaderboard;
CREATE POLICY "Cho phép tất cả mọi người xem Bảng Xếp Hạng" ON public.leaderboard FOR SELECT USING (true);

DROP POLICY IF EXISTS "Chỉ người chơi đã đăng nhập mới được thêm/sửa điểm của mình" ON public.leaderboard;

-- Trigger tự động cập nhật updated_at
DROP TRIGGER IF EXISTS trigger_update_leaderboard_updated_at ON public.leaderboard;
CREATE TRIGGER trigger_update_leaderboard_updated_at 
    BEFORE UPDATE ON public.leaderboard 
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

-- Trigger tự động đồng bộ display_name sang username của leaderboard khi đổi tên
CREATE OR REPLACE FUNCTION public.sync_leaderboard_username()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD.display_name IS DISTINCT FROM NEW.display_name THEN
        UPDATE public.leaderboard
        SET username = NEW.display_name, updated_at = timezone('utc'::text, now())
        WHERE user_id = NEW.id;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

DROP TRIGGER IF EXISTS trigger_sync_leaderboard_username ON public.profiles;
CREATE TRIGGER trigger_sync_leaderboard_username
    AFTER UPDATE ON public.profiles
    FOR EACH ROW EXECUTE FUNCTION public.sync_leaderboard_username();


-- ---------------------------------------------------------------------
-- 4. BẢNG CẤU HÌNH TỪ XA (game_config)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.game_config (
    config_key VARCHAR(100) PRIMARY KEY,                          -- Từ khóa cấu hình (Ví dụ: hue_damage, nhac_speed)
    config_value NUMERIC NOT NULL,                                -- Giá trị cấu hình
    description TEXT,                                             -- Mô tả chi tiết chỉ số
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- Kích hoạt RLS bảo mật cho bảng cấu hình game
ALTER TABLE public.game_config ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Cho phép mọi client đọc cấu hình game" ON public.game_config;
CREATE POLICY "Cho phép mọi client đọc cấu hình game" ON public.game_config FOR SELECT USING (true);

DROP POLICY IF EXISTS "Chỉ cho phép sửa đổi cấu hình từ hệ thống" ON public.game_config;
CREATE POLICY "Chỉ cho phép sửa đổi cấu hình từ hệ thống" ON public.game_config FOR ALL USING (false);

-- Chèn dữ liệu cân bằng game mặc định ban đầu cho 3 anh em Tây Sơn
INSERT INTO public.game_config (config_key, config_value, description)
VALUES 
    ('hue_damage', 25.0, 'Sát thương cơ bản của Nguyễn Huệ - Lối đánh uy lực, tầm trung'),
    ('nhac_damage', 18.0, 'Sát thương cơ bản của Nguyễn Nhạc - Lối đánh nhanh nhẹn, tầm gần'),
    ('lu_damage', 14.0, 'Sát thương cơ bản của Nguyễn Lữ - Lối đánh biến hóa, tầm xa'),
    ('hue_speed', 320.0, 'Tốc độ di chuyển của Nguyễn Huệ'),
    ('nhac_speed', 280.0, 'Tốc độ di chuyển của Nguyễn Nhạc'),
    ('lu_speed', 380.0, 'Tốc độ di chuyển của Nguyễn Lữ')
ON CONFLICT (config_key) DO UPDATE 
SET config_value = EXCLUDED.config_value, 
    description = EXCLUDED.description;


-- ---------------------------------------------------------------------
-- 5. BẢNG DANH MỤC VẬT PHẨM GAME (game_items)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.game_items (
    id VARCHAR(50) PRIMARY KEY,                                    -- Mã vật phẩm (Ví dụ: 'sword_001', 'hp_potion')
    name VARCHAR(100) NOT NULL,                                   -- Tên vật phẩm
    description TEXT,                                             -- Mô tả vật phẩm
    price_gold INTEGER DEFAULT 0 NOT NULL,                        -- Giá bằng Vàng (Gold)
    price_gem INTEGER DEFAULT 0 NOT NULL,                         -- Giá bằng Ngọc (Gem)
    price_vnd INTEGER DEFAULT 0 NOT NULL,                         -- Giá bằng Tiền mặt VND (cho vật phẩm nạp trực tiếp)
    item_type VARCHAR(30) DEFAULT 'Consumable' NOT NULL,           -- Loại: 'Equipment', 'Consumable', 'Skin', 'Cosmetic'
    attributes JSONB DEFAULT NULL,                                -- Thuộc tính động của vật phẩm (dùng GIN index)
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    CONSTRAINT chk_item_type CHECK (item_type IN ('Equipment', 'Consumable', 'Skin', 'Cosmetic'))
);

-- Kích hoạt RLS bảo mật danh mục vật phẩm
ALTER TABLE public.game_items ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Cho phép tất cả mọi người đọc danh mục vật phẩm" ON public.game_items;
CREATE POLICY "Cho phép tất cả mọi người đọc danh mục vật phẩm" ON public.game_items FOR SELECT USING (true);

DROP POLICY IF EXISTS "Chỉ hệ thống được quyền thao tác vật phẩm" ON public.game_items;
CREATE POLICY "Chỉ hệ thống được quyền thao tác vật phẩm" ON public.game_items FOR ALL USING (false);

-- Lập chỉ mục GIN cho thuộc tính động attributes trong game_items để hỗ trợ tìm kiếm theo key JSON siêu tốc
CREATE INDEX IF NOT EXISTS idx_items_attr_gin ON public.game_items USING GIN (attributes jsonb_path_ops);

-- Chèn dữ liệu vật phẩm mẫu ban đầu
INSERT INTO public.game_items (id, name, description, price_gold, price_gem, price_vnd, item_type, attributes)
VALUES
    ('tran_thao_son_tra', 'Trân Thảo Sơn Trà', 'Hồi đầy 100% sinh lực cho nghĩa sĩ ngay tức khắc.', 80, 0, 0, 'Consumable', '{"slot": "none", "effect": "heal", "value": 100.0, "duration": 0.0}'),
    ('linh_dan_hoi_khi', 'Linh Đan Hồi Khí', 'Nạp đầy Nộ Khí để tung tuyệt kỹ liền tay.', 60, 0, 0, 'Consumable', '{"slot": "none", "effect": "rage", "value": 100.0, "duration": 0.0}'),
    ('ruou_de_quy_nhon', 'Rượu Đế Quy Nhơn', 'Tăng 30% sát thương trong 30 giây xung trận.', 200, 0, 0, 'Consumable', '{"slot": "none", "effect": "dmg_buff", "value": 0.30, "duration": 30.0}'),
    ('khien_dong_son', 'Khiên Đồng Đông Sơn', 'Lá chắn đồng bất hoại, miễn nhiễm sát thương 12 giây.', 250, 0, 0, 'Consumable', '{"slot": "none", "effect": "shield", "value": 0.0, "duration": 12.0}'),
    ('co_dao_phuc_sinh', 'Cờ Đào Phục Sinh', 'Hồi sinh tại trận một lần (50% máu) khi nghĩa sĩ gục ngã.', 500, 0, 0, 'Consumable', '{"slot": "none", "effect": "revive", "value": 0.0, "duration": 0.0}'),
    ('hoang_de_co_dao', 'Hoàng Đế Cổ Đao', 'Đại đao hoàng triều — vĩnh viễn +12% sát thương.', 640, 0, 0, 'Equipment', '{"slot": "weapon", "effect": "equip_dmg", "value": 0.12, "duration": 0.0}'),
    ('co_kiem_binh_dinh', 'Cổ Kiếm Bình Định', 'Bảo kiếm khai quốc — vĩnh viễn +18% sát thương.', 1200, 0, 0, 'Equipment', '{"slot": "weapon", "effect": "equip_dmg", "value": 0.18, "duration": 0.0}'),
    ('thiet_thuong_tayson', 'Thiết Thương Tây Sơn', 'Trường thương bọc sắt — vĩnh viễn +25% sát thương.', 2600, 0, 0, 'Equipment', '{"slot": "weapon", "effect": "equip_dmg", "value": 0.25, "duration": 0.0}'),
    ('song_thiet_con', 'Song Thiết Côn', 'Côn sắt song đầu — vĩnh viễn +32% sát thương.', 4200, 0, 0, 'Equipment', '{"slot": "weapon", "effect": "equip_dmg", "value": 0.32, "duration": 0.0}'),
    ('than_kinh_tayson', 'Tây Sơn Thần Kính', 'Thần khí tối thượng — vĩnh viễn +45% sát thương.', 0, 300, 0, 'Equipment', '{"slot": "weapon", "effect": "equip_dmg", "value": 0.45, "duration": 0.0}'),
    ('an_ngoc_hoang_de', 'Ấn Ngọc Hoàng Đế', 'Ấn ngọc danh giá — biểu tượng bậc đế vương (trang trí hồ sơ).', 0, 120, 0, 'Cosmetic', '{"slot": "none", "effect": "none", "value": 0.0, "duration": 0.0}'),
    ('giap_da_tayson', 'Tây Sơn Giáp Da', 'Áo giáp da dẻo dai — tăng 20% sinh lực tối đa.', 1000, 0, 0, 'Equipment', '{"slot": "armor", "effect": "equip_hp", "value": 0.20, "duration": 0.0}'),
    ('thiet_giap_tayson', 'Tây Sơn Thiết Giáp', 'Giáp sắt kiên cố của nghĩa quân — tăng 40% sinh lực tối đa.', 2500, 0, 0, 'Equipment', '{"slot": "armor", "effect": "equip_hp", "value": 0.40, "duration": 0.0}'),
    ('hoang_gia_chien_giap', 'Hoàng Gia Chiến Giáp', 'Chiến giáp hoàng triều đúc bằng đồng quý — tăng 70% sinh lực tối đa.', 5000, 0, 0, 'Equipment', '{"slot": "armor", "effect": "equip_hp", "value": 0.70, "duration": 0.0}'),
    ('bao_tinh_giap', 'Bảo Tinh Giáp', 'Thần giáp bảo thạch hộ thân — tăng 100% sinh lực tối đa.', 0, 400, 0, 'Equipment', '{"slot": "armor", "effect": "equip_hp", "value": 1.00, "duration": 0.0}'),
    ('equipment_weapon_long_tinh_dao', 'Long Tinh Đao', 'Đại đao khắc họa long hình tôn nghiêm — vĩnh viễn +35% sát thương.', 5000, 150, 0, 'Equipment', '{"slot": "weapon", "effect": "equip_dmg", "value": 0.35, "icon_path": "res://assets/sprites/items/long_tinh_dao.png"}'),
    ('equipment_armor_hac_ho_giap', 'Hắc Hổ Thiết Giáp', 'Thiết giáp khắc họa hình hổ đen dũng mãnh — tăng 55% sinh lực tối đa.', 3500, 100, 0, 'Equipment', '{"slot": "armor", "effect": "equip_hp", "value": 0.55, "icon_path": "res://assets/sprites/items/hac_ho_giap.png"}'),
    ('equipment_weapon_than_co_thuong', 'Thần Cơ Thương', 'Bảo khí súng hỏa mai Thần Cơ cải tiến — vĩnh viễn +40% sát thương.', 4500, 200, 0, 'Equipment', '{"slot": "weapon", "effect": "equip_dmg", "value": 0.40, "icon_path": "res://assets/sprites/items/than_co_thuong.png"}')
ON CONFLICT (id) DO NOTHING;


-- ---------------------------------------------------------------------
-- 6. BẢNG KHO ĐỒ SỞ HỮU NGƯỜI CHƠI (player_inventories) - HỖ TRỢ SOFT DELETE
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.player_inventories (
    id BIGSERIAL PRIMARY KEY,
    user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
    item_id VARCHAR(50) REFERENCES public.game_items(id) ON DELETE CASCADE NOT NULL,
    quantity INTEGER DEFAULT 1 NOT NULL,
    acquired_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    deleted_at TIMESTAMP WITH TIME ZONE DEFAULT NULL,
    CONSTRAINT chk_positive_qty CHECK (quantity >= 0)
);

-- Tối ưu hóa ràng buộc duy nhất rương đồ chỉ cho các bản ghi chưa bị xóa (Soft Delete safe)
CREATE UNIQUE INDEX IF NOT EXISTS unique_user_item_active_idx ON public.player_inventories (user_id, item_id) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_inventory_user ON public.player_inventories (user_id) WHERE deleted_at IS NULL;

-- Kích hoạt RLS bảo mật kho đồ
ALTER TABLE public.player_inventories ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Người dùng chỉ được xem kho đồ của mình" ON public.player_inventories;
CREATE POLICY "Người dùng chỉ được xem kho đồ của mình" 
    ON public.player_inventories FOR SELECT TO authenticated 
    USING (auth.uid() = user_id);

-- Trigger tự động cập nhật updated_at
DROP TRIGGER IF EXISTS trigger_update_player_inventories_updated_at ON public.player_inventories;
CREATE TRIGGER trigger_update_player_inventories_updated_at 
    BEFORE UPDATE ON public.player_inventories 
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

-- Trigger tự động soft-delete khi quantity giảm về 0
CREATE OR REPLACE FUNCTION public.auto_soft_delete_inventory()
RETURNS TRIGGER AS $$
BEGIN
    IF NEW.quantity = 0 THEN
        NEW.deleted_at := timezone('utc'::text, now());
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

DROP TRIGGER IF EXISTS trigger_auto_soft_delete_inventory ON public.player_inventories;
CREATE TRIGGER trigger_auto_soft_delete_inventory
    BEFORE UPDATE ON public.player_inventories 
    FOR EACH ROW EXECUTE FUNCTION public.auto_soft_delete_inventory();


-- ---------------------------------------------------------------------
-- 7. BẢNG TƯỚNG TÂY SƠN (player_heroes) & TRANG BỊ (hero_equipments) - SOFT DELETE
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.player_heroes (
    id BIGSERIAL PRIMARY KEY,
    user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
    hero_key VARCHAR(50) NOT NULL,                                 -- 'hue', 'nhac', 'lu'
    level INTEGER DEFAULT 1 NOT NULL,
    experience INTEGER DEFAULT 0 NOT NULL,
    skill_active_level INTEGER DEFAULT 1 NOT NULL,
    skill_passive_level INTEGER DEFAULT 1 NOT NULL,
    unlocked_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    CONSTRAINT unique_user_hero UNIQUE (user_id, hero_key),
    CONSTRAINT chk_hero_key CHECK (hero_key IN ('hue', 'nhac', 'lu'))
);

-- Kích hoạt RLS bảo mật tướng
ALTER TABLE public.player_heroes ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Chủ sở hữu xem tướng của mình" ON public.player_heroes;
CREATE POLICY "Chủ sở hữu xem tướng của mình" ON public.player_heroes FOR SELECT TO authenticated USING (auth.uid() = user_id);

-- Trigger tự động cập nhật updated_at
DROP TRIGGER IF EXISTS trigger_update_player_heroes_updated_at ON public.player_heroes;
CREATE TRIGGER trigger_update_player_heroes_updated_at 
    BEFORE UPDATE ON public.player_heroes 
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

-- Bảng trang bị mặc trên tướng (hero_equipments)
CREATE TABLE IF NOT EXISTS public.hero_equipments (
    id BIGSERIAL PRIMARY KEY,
    player_hero_id BIGINT REFERENCES public.player_heroes(id) ON DELETE CASCADE NOT NULL,
    inventory_item_id BIGINT REFERENCES public.player_inventories(id) ON DELETE CASCADE NOT NULL,
    slot_type VARCHAR(20) NOT NULL,                                -- 'Weapon', 'Armor', 'Accessory'
    equipped_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    deleted_at TIMESTAMP WITH TIME ZONE DEFAULT NULL,              -- Hỗ trợ soft-delete trang bị
    CONSTRAINT chk_slot_type CHECK (slot_type IN ('Weapon', 'Armor', 'Accessory'))
);

-- Ràng buộc độc quyền mặc trang bị: 1 món đồ trong rương chỉ được phép mặc trên 1 tướng tại 1 thời điểm
CREATE UNIQUE INDEX IF NOT EXISTS idx_hero_equipments_inventory_item_unique ON public.hero_equipments (inventory_item_id) WHERE deleted_at IS NULL;

-- Tối ưu hóa ràng buộc duy nhất trang bị theo slot (Soft Delete safe)
CREATE UNIQUE INDEX IF NOT EXISTS unique_hero_slot_active_idx ON public.hero_equipments (player_hero_id, slot_type) WHERE deleted_at IS NULL;

-- Kích hoạt RLS bảo mật trang bị tướng
ALTER TABLE public.hero_equipments ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Xem trang bị của tướng chính chủ" ON public.hero_equipments;
CREATE POLICY "Xem trang bị của tướng chính chủ" 
    ON public.hero_equipments FOR SELECT TO authenticated 
    USING (
        EXISTS (
            SELECT 1 FROM public.player_heroes ph 
            WHERE ph.id = player_hero_id AND ph.user_id = auth.uid()
        ) AND deleted_at IS NULL
    );


-- ---------------------------------------------------------------------
-- 8. BẢNG HÒM THƯ (mailbox) - PHÂN VÙNG THEO THÁNG
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.mailbox (
    id BIGSERIAL,
    receiver_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE, -- NULL đại diện cho thư Broadcast toàn server
    title VARCHAR(150) NOT NULL,
    content TEXT NOT NULL,
    attachments JSONB DEFAULT NULL,                                -- JSON đính kèm: {"gold": 1000, "gems": 50, "items": [{"id": "pot_hp_01", "qty": 2}]}
    is_read BOOLEAN DEFAULT false NOT NULL,
    is_claimed BOOLEAN DEFAULT false NOT NULL,                     -- Trạng thái nhận quà đính kèm
    expired_at TIMESTAMP WITH TIME ZONE,                           -- Thư tự hủy sau N ngày
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    deleted_at TIMESTAMP WITH TIME ZONE DEFAULT NULL,              -- Hỗ trợ soft-delete hòm thư
    PRIMARY KEY (id, created_at)
) PARTITION BY RANGE (created_at);

-- Kích hoạt RLS bảo mật cho hòm thư
ALTER TABLE public.mailbox ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Người dùng chỉ được xem thư của mình hoặc thư broadcast" ON public.mailbox;
CREATE POLICY "Người dùng chỉ được xem thư của mình hoặc thư broadcast" 
    ON public.mailbox FOR SELECT TO authenticated 
    USING ((receiver_id = auth.uid() OR receiver_id IS NULL) AND deleted_at IS NULL);

-- Trigger tự động cập nhật updated_at
DROP TRIGGER IF EXISTS trigger_update_mailbox_updated_at ON public.mailbox;
CREATE TRIGGER trigger_update_mailbox_updated_at 
    BEFORE UPDATE ON public.mailbox 
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

-- Chỉ mục tối ưu cho hòm thư (quét thư chưa đọc của người chơi nhanh chóng)
CREATE INDEX IF NOT EXISTS idx_mailbox_unread ON public.mailbox (receiver_id) WHERE is_read = false AND deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_mailbox_receiver_deleted_created ON public.mailbox (receiver_id, deleted_at, created_at DESC);

-- Bảng trạng thái nhận thư Broadcast của từng cá nhân (không có FK khóa ngoại cứng để tối ưu hiệu năng phân vùng)
CREATE TABLE IF NOT EXISTS public.player_broadcast_claims (
    id BIGSERIAL PRIMARY KEY,
    user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
    mail_id BIGINT NOT NULL,
    is_read BOOLEAN DEFAULT true NOT NULL,
    is_claimed BOOLEAN DEFAULT false NOT NULL,
    claimed_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    CONSTRAINT unique_user_broadcast_claim UNIQUE (user_id, mail_id)
);

-- Trigger tự động xóa sạch các claim thư broadcast khi hòm thư tương ứng bị xóa vĩnh viễn
CREATE OR REPLACE FUNCTION public.cleanup_broadcast_claims_on_mail_delete()
RETURNS TRIGGER AS $$
BEGIN
    DELETE FROM public.player_broadcast_claims WHERE mail_id = OLD.id;
    RETURN OLD;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

DROP TRIGGER IF EXISTS trigger_mailbox_delete_cleanup ON public.mailbox;
CREATE TRIGGER trigger_mailbox_delete_cleanup
    AFTER DELETE ON public.mailbox
    FOR EACH ROW EXECUTE FUNCTION public.cleanup_broadcast_claims_on_mail_delete();

-- Kích hoạt RLS cho broadcast claims
ALTER TABLE public.player_broadcast_claims ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Chủ sở hữu xem broadcast claims" ON public.player_broadcast_claims;
CREATE POLICY "Chủ sở hữu xem broadcast claims" ON public.player_broadcast_claims FOR SELECT TO authenticated USING (auth.uid() = user_id);

DROP POLICY IF EXISTS "Cho phép chèn dòng claims" ON public.player_broadcast_claims;
CREATE POLICY "Cho phép chèn dòng claims" ON public.player_broadcast_claims FOR INSERT TO authenticated WITH CHECK (auth.uid() = user_id);


-- ---------------------------------------------------------------------
-- 9. BẢNG CHỢ GIAO DỊCH (marketplace_listings) & ĐẶT CHỖ (marketplace_reservations)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.marketplace_listings (
    id BIGSERIAL PRIMARY KEY,
    seller_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
    buyer_id UUID REFERENCES public.profiles(id) ON DELETE SET NULL NULL, -- Lưu vết người mua để kiểm toán
    inventory_item_id BIGINT REFERENCES public.player_inventories(id) ON DELETE CASCADE NOT NULL,
    price_gold INTEGER DEFAULT 0 NOT NULL,
    price_gem INTEGER DEFAULT 0 NOT NULL,
    listing_fee INTEGER DEFAULT 0 NOT NULL,                        -- Phí đăng bài (chống spam)
    status VARCHAR(20) DEFAULT 'Active' NOT NULL,                  -- 'Active', 'Reserved', 'Sold', 'Cancelled'
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    deleted_at TIMESTAMP WITH TIME ZONE DEFAULT NULL,
    CONSTRAINT chk_listings_status CHECK (status IN ('Active', 'Reserved', 'Sold', 'Cancelled')),
    CONSTRAINT chk_listings_price_gold CHECK (price_gold >= 0),
    CONSTRAINT chk_listings_price_gem CHECK (price_gem >= 0)
);

-- Kích hoạt RLS bảo mật chợ giao dịch
ALTER TABLE public.marketplace_listings ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Ai cũng được xem tin rao bán chợ còn active" ON public.marketplace_listings;
CREATE POLICY "Ai cũng được xem tin rao bán chợ còn active" 
    ON public.marketplace_listings FOR SELECT 
    USING (status = 'Active' OR seller_id = auth.uid() OR buyer_id = auth.uid());

DROP POLICY IF EXISTS "Chỉ người bán mới cập nhật được tin của mình" ON public.marketplace_listings;
CREATE POLICY "Chỉ người bán mới cập nhật được tin của mình" 
    ON public.marketplace_listings FOR UPDATE TO authenticated 
    USING (auth.uid() = seller_id);

-- Trigger tự động cập nhật updated_at
DROP TRIGGER IF EXISTS trigger_update_marketplace_listings_updated_at ON public.marketplace_listings;
CREATE TRIGGER trigger_update_marketplace_listings_updated_at 
    BEFORE UPDATE ON public.marketplace_listings 
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

-- Chỉ mục tối ưu tìm kiếm vật phẩm đang active trên chợ theo thời gian đăng gần nhất
CREATE INDEX IF NOT EXISTS idx_market_active_created ON public.marketplace_listings (status, created_at DESC) WHERE status = 'Active' AND deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_market_seller_status ON public.marketplace_listings (seller_id, status) WHERE deleted_at IS NULL;

-- Bảng giữ chỗ đặt chợ (marketplace_reservations)
CREATE TABLE IF NOT EXISTS public.marketplace_reservations (
    id BIGSERIAL PRIMARY KEY,
    listing_id BIGINT REFERENCES public.marketplace_listings(id) ON DELETE CASCADE NOT NULL,
    user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
    reserved_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    CONSTRAINT unique_listing_reservation UNIQUE (listing_id)
);

-- Lập chỉ mục siêu tốc cho bảng đặt chỗ để cleanup nhanh dưới 1ms
CREATE INDEX IF NOT EXISTS idx_reservations_listing_user ON public.marketplace_reservations (listing_id, user_id);

-- Kích hoạt RLS cho đặt chỗ chợ
ALTER TABLE public.marketplace_reservations ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Chủ nhân xem đặt chỗ của mình" ON public.marketplace_reservations;
CREATE POLICY "Chủ nhân xem đặt chỗ của mình" ON public.marketplace_reservations FOR SELECT TO authenticated USING (auth.uid() = user_id);


-- ---------------------------------------------------------------------
-- 10. BẢNG GAMEPLAY: PHIÊN CHƠI, NHIỆM VỤ, THÀNH TỰU & BATTLE LOGS
-- ---------------------------------------------------------------------

-- Master Catalog: Bảng danh mục Nhiệm vụ (game_quests)
CREATE TABLE IF NOT EXISTS public.game_quests (
    id VARCHAR(100) PRIMARY KEY,
    title VARCHAR(150) NOT NULL,
    description TEXT,
    target_count INTEGER DEFAULT 1 NOT NULL,
    gold_reward INTEGER DEFAULT 0 NOT NULL,
    gem_reward INTEGER DEFAULT 0 NOT NULL
);

-- Bật RLS và chính sách bảo mật cho game_quests
ALTER TABLE public.game_quests ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "Cho phép xem danh mục nhiệm vụ" ON public.game_quests;
CREATE POLICY "Cho phép xem danh mục nhiệm vụ" ON public.game_quests FOR SELECT USING (true);

-- Điền dữ liệu nhiệm vụ mẫu ban đầu
INSERT INTO public.game_quests (id, title, description, target_count, gold_reward, gem_reward)
VALUES
    ('quest_rach_gam_01', 'Chiến Thắng Rạch Gầm', 'Đánh bại quân Xiêm tại Rạch Gầm - Xoài Mút', 1, 1000, 50),
    ('quest_ngoc_hoi_01', 'Đại Phá Ngọc Hồi', 'Đại phá đồn Ngọc Hồi quân Thanh', 1, 2000, 100)
ON CONFLICT (id) DO NOTHING;

-- Master Catalog: Bảng danh mục Thành tựu (game_achievements)
CREATE TABLE IF NOT EXISTS public.game_achievements (
    id VARCHAR(100) PRIMARY KEY,
    title VARCHAR(150) NOT NULL,
    description TEXT,
    gold_reward INTEGER DEFAULT 0 NOT NULL,
    gem_reward INTEGER DEFAULT 0 NOT NULL
);

-- Bật RLS và chính sách bảo mật cho game_achievements
ALTER TABLE public.game_achievements ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "Cho phép xem danh mục thành tựu" ON public.game_achievements;
CREATE POLICY "Cho phép xem danh mục thành tựu" ON public.game_achievements FOR SELECT USING (true);

-- Điền dữ liệu thành tựu mẫu ban đầu
INSERT INTO public.game_achievements (id, title, description, gold_reward, gem_reward)
VALUES
    ('ach_first_victory', 'Chiến Công Đầu', 'Giành chiến thắng trận đầu tiên', 100, 10),
    ('ach_max_level', 'Vạn Nhân Địch', 'Đạt cấp độ tối đa', 5000, 500)
ON CONFLICT (id) DO NOTHING;

-- A. Bảng Phiên Chơi (game_sessions)
CREATE TABLE IF NOT EXISTS public.game_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
    ip_address VARCHAR(45) NULL,
    client_version VARCHAR(20) NOT NULL,
    started_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    ended_at TIMESTAMP WITH TIME ZONE DEFAULT NULL,
    duration_seconds INTEGER DEFAULT NULL
);
CREATE INDEX IF NOT EXISTS idx_sessions_active ON public.game_sessions (user_id) WHERE ended_at IS NULL;

-- Kích hoạt RLS game_sessions
ALTER TABLE public.game_sessions ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Chính chủ quản trị session của mình" ON public.game_sessions;
CREATE POLICY "Chính chủ quản trị session của mình" ON public.game_sessions FOR ALL TO authenticated USING (auth.uid() = user_id);

-- B. Bảng Tiến trình Nhiệm vụ (quest_progress)
CREATE TABLE IF NOT EXISTS public.quest_progress (
    id BIGSERIAL PRIMARY KEY,
    user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
    quest_id VARCHAR(100) REFERENCES public.game_quests(id) ON DELETE CASCADE NOT NULL,
    status VARCHAR(20) DEFAULT 'Accepted' NOT NULL,
    progress_count INTEGER DEFAULT 0 NOT NULL,
    target_count INTEGER DEFAULT 1 NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    CONSTRAINT unique_user_quest UNIQUE (user_id, quest_id),
    CONSTRAINT chk_quest_status CHECK (status IN ('Accepted', 'Completed', 'Rewarded'))
);
CREATE INDEX IF NOT EXISTS idx_quest_user_updated ON public.quest_progress (user_id, updated_at DESC);

-- Kích hoạt RLS quest_progress
ALTER TABLE public.quest_progress ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Chủ nhiệm vụ quản trị quest progress" ON public.quest_progress;
CREATE POLICY "Chủ nhiệm vụ quản trị quest progress" ON public.quest_progress FOR ALL TO authenticated USING (auth.uid() = user_id);

-- Trigger tự động cập nhật updated_at
DROP TRIGGER IF EXISTS trigger_update_quest_progress_updated_at ON public.quest_progress;
CREATE TRIGGER trigger_update_quest_progress_updated_at 
    BEFORE UPDATE ON public.quest_progress 
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

-- C. Bảng Thành tựu (achievements)
CREATE TABLE IF NOT EXISTS public.achievements (
    user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
    achievement_id VARCHAR(100) REFERENCES public.game_achievements(id) ON DELETE CASCADE NOT NULL,
    unlocked_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY (user_id, achievement_id)
);

-- Kích hoạt RLS achievements
ALTER TABLE public.achievements ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Xem thành tựu của mình" ON public.achievements;
CREATE POLICY "Xem thành tựu của mình" ON public.achievements FOR SELECT TO authenticated USING (auth.uid() = user_id);

-- D. Bảng Nhật ký trận chiến (battle_logs) - PHÂN VÙNG THEO THÁNG
CREATE TABLE IF NOT EXISTS public.battle_logs (
    id BIGSERIAL,
    user_id UUID NOT NULL,
    stage_id VARCHAR(50) NOT NULL,                                 -- 'rach_gam_01', 'ngoc_hoi_01'
    battle_status VARCHAR(20) NOT NULL,                           -- 'Victory', 'Defeat', 'Aborted'
    duration_seconds INTEGER NOT NULL,
    score_earned INTEGER DEFAULT 0 NOT NULL,
    gold_earned INTEGER DEFAULT 0 NOT NULL,
    enemies_killed INTEGER DEFAULT 0 NOT NULL,
    battle_metadata JSONB DEFAULT NULL,                            -- Ghi nhận chỉ số sát thương, máu để quét cheat
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    CONSTRAINT chk_battle_status CHECK (battle_status IN ('Victory', 'Defeat', 'Aborted'))
) PARTITION BY RANGE (created_at);

-- Kích hoạt RLS cho battle_logs
ALTER TABLE public.battle_logs ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Xem trận chiến chính chủ" ON public.battle_logs;
CREATE POLICY "Xem trận chiến chính chủ" ON public.battle_logs FOR SELECT TO authenticated USING (auth.uid() = user_id);

-- Chỉ mục tối ưu hóa phân vùng battle_logs trên user_id
CREATE INDEX IF NOT EXISTS idx_battle_logs_user ON public.battle_logs (user_id, created_at DESC);


-- ---------------------------------------------------------------------
-- 11. BẢNG NHẬT KÝ KIỂM TOÁN HOẠT ĐỘNG (admin_logs & player_audit_logs)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.admin_logs (
    id BIGSERIAL PRIMARY KEY,
    admin_username VARCHAR(100) NOT NULL,                           -- Tên quản trị viên thực hiện hành động
    action_name VARCHAR(100) NOT NULL,                             -- Loại hành động (Ví dụ: "Ban Player", "Update Config")
    action_details TEXT NOT NULL,                                  -- Chi tiết thao tác cụ thể
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- Kích hoạt RLS cho admin_logs
ALTER TABLE public.admin_logs ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "Chỉ hệ thống được quyền thao tác với logs" ON public.admin_logs;
CREATE POLICY "Chỉ hệ thống được quyền thao tác với logs" ON public.admin_logs FOR ALL USING (false);

-- Player Audit Logs - CẤU TRÚC PHÂN VÙNG (PARTITIONED)
CREATE TABLE IF NOT EXISTS public.player_audit_logs (
    id BIGSERIAL,
    user_id UUID NOT NULL,
    action_type VARCHAR(50) NOT NULL,                             -- 'Login', 'Logout', 'CraftItem', 'TradeMatch'
    details TEXT NOT NULL,
    ip_address VARCHAR(45) NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
) PARTITION BY RANGE (created_at);

-- Kích hoạt RLS cho player_audit_logs
ALTER TABLE public.player_audit_logs ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "Chỉ hệ thống thao tác với audit logs" ON public.player_audit_logs;
CREATE POLICY "Chỉ hệ thống thao tác với audit logs" ON public.player_audit_logs FOR ALL USING (false);

-- Chỉ mục tối ưu hóa phân vùng player_audit_logs trên user_id
CREATE INDEX IF NOT EXISTS idx_player_audit_logs_user ON public.player_audit_logs (user_id, created_at DESC);


-- ---------------------------------------------------------------------
-- 12. BẢNG GIAO DỊCH TÀI CHÍNH (transactions) - CẤU TRÚC PHÂN VÙNG (PARTITIONED)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.transactions (
    id BIGSERIAL,
    user_id UUID NOT NULL,
    transaction_type VARCHAR(30) NOT NULL,                        -- 'Topup', 'Purchase'
    amount_vnd INTEGER DEFAULT 0 NOT NULL,                        -- Số tiền VND (Nếu nạp tiền)
    amount_gold INTEGER DEFAULT 0 NOT NULL,                       -- Biến động số lượng Vàng (Gold)
    amount_gem INTEGER DEFAULT 0 NOT NULL,                        -- Biến động số lượng Ngọc (Gem)
    payment_method VARCHAR(50) NULL,                              -- 'Momo', 'Card', 'Banking'
    reference_id VARCHAR(100) NULL,                               -- Mã giao dịch đối chiếu
    status VARCHAR(20) DEFAULT 'Pending' NOT NULL,                 -- 'Pending', 'Completed', 'Failed', 'Refunded'
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    CONSTRAINT chk_partition_tx_status CHECK (status IN ('Pending', 'Completed', 'Failed', 'Refunded'))
) PARTITION BY RANGE (created_at);

-- Kích hoạt RLS bảo mật giao dịch
ALTER TABLE public.transactions ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Người dùng chỉ được xem lịch sử giao dịch của mình" ON public.transactions;
CREATE POLICY "Người dùng chỉ được xem lịch sử giao dịch của mình" 
    ON public.transactions FOR SELECT TO authenticated 
    USING (auth.uid() = user_id);

-- Trigger tự động cập nhật updated_at
DROP TRIGGER IF EXISTS trigger_update_transactions_updated_at ON public.transactions;
CREATE TRIGGER trigger_update_transactions_updated_at 
    BEFORE UPDATE ON public.transactions 
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

-- Chỉ mục tối ưu cho giao dịch
CREATE INDEX IF NOT EXISTS idx_transactions_user_created ON public.transactions (user_id, created_at DESC);


-- ---------------------------------------------------------------------
-- 13. TỰ ĐỘNG PHÂN VÙNG ĐỘNG HÀNG THÁNG (Automated Monthly Partition Maintenance)
-- ---------------------------------------------------------------------

-- Hàm tự tạo phân vùng động để chống lỗi chèn mới khi qua tháng mới
CREATE OR REPLACE FUNCTION public.ensure_monthly_partition(p_table TEXT, p_date TIMESTAMP WITH TIME ZONE)
RETURNS VOID AS $$
DECLARE
    v_start_date TIMESTAMP WITH TIME ZONE := date_trunc('month', p_date);
    v_end_date TIMESTAMP WITH TIME ZONE := v_start_date + INTERVAL '1 month';
    v_partition_name TEXT := p_table || '_y' || to_char(v_start_date, 'YYYY') || 'm' || to_char(v_start_date, 'MM');
BEGIN
    -- Kiểm tra trong pg_catalog để tránh lỗi khóa DDL khi đang có active query
    IF EXISTS (
        SELECT 1 
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = LOWER(v_partition_name) 
          AND n.nspname = 'public'
    ) THEN
        RETURN;
    END IF;

    -- 1. Tự động tạo phân vùng nếu chưa có
    EXECUTE format(
        'CREATE TABLE IF NOT EXISTS %I PARTITION OF %I FOR VALUES FROM (%L) TO (%L)',
        v_partition_name, p_table, v_start_date, v_end_date
    );

    -- 2. Tự động kích hoạt RLS cho phân vùng con để bảo mật tuyệt đối chống bypass
    EXECUTE format(
        'ALTER TABLE %I ENABLE ROW LEVEL SECURITY',
        v_partition_name
    );
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

-- Dọn dẹp dọn dẹp các phân vùng tương lai quá xa để làm gọn Supabase lập tức (Chỉ giữ từ tháng 4/2026 tới 7/2026 làm đệm)
DO $$
DECLARE
    v_date TIMESTAMP WITH TIME ZONE;
    v_end TIMESTAMP WITH TIME ZONE := '2027-12-01 00:00:00+00';
    v_tables TEXT[] := ARRAY['mailbox', 'battle_logs', 'player_audit_logs', 'transactions'];
    v_tbl TEXT;
    v_partition_name TEXT;
BEGIN
    -- 1. Xóa sạch các phân vùng quá xa để giảm tải hiển thị cho Supabase
    FOREACH v_tbl IN ARRAY v_tables LOOP
        v_date := '2026-08-01 00:00:00+00';
        WHILE v_date <= v_end LOOP
            v_partition_name := v_tbl || '_y' || to_char(v_date, 'YYYY') || 'm' || to_char(v_date, 'MM');
            EXECUTE format('DROP TABLE IF EXISTS public.%I CASCADE', v_partition_name);
            v_date := v_date + INTERVAL '1 month';
        END LOOP;
    END LOOP;
END;
$$;

-- Tự động pre-create phân vùng thiết yếu (từ tháng 4/2026 đến hết tháng 7/2026 làm đệm an toàn)
DO $$
DECLARE
    v_date TIMESTAMP WITH TIME ZONE := '2026-04-01 00:00:00+00';
    v_end TIMESTAMP WITH TIME ZONE := '2026-07-01 00:00:00+00';
BEGIN
    WHILE v_date <= v_end LOOP
        PERFORM public.ensure_monthly_partition('mailbox', v_date);
        PERFORM public.ensure_monthly_partition('battle_logs', v_date);
        PERFORM public.ensure_monthly_partition('player_audit_logs', v_date);
        PERFORM public.ensure_monthly_partition('transactions', v_date);
        v_date := v_date + INTERVAL '1 month';
    END LOOP;
END;
$$;


-- ---------------------------------------------------------------------
-- 14. TRIGGER TỰ ĐỘNG TẠO PHÂN VÙNG DỰ PHÒNG KHI CÓ INSERT (On-Demand Partition Creation)
-- Giúp hệ thống tự sinh phân vùng trong tương lai mà không cần tạo trước hàng trăm bảng con
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION public.route_and_create_partition_trigger()
RETURNS TRIGGER AS $$
BEGIN
    -- Chỉ chạy đối với các bảng cha (Parent tables), tránh chạy lặp trên các bảng con phân vùng (Partition tables)
    IF TG_TABLE_NAME::text NOT IN ('mailbox', 'battle_logs', 'player_audit_logs', 'transactions') THEN
        RETURN NEW;
    END IF;

    PERFORM public.ensure_monthly_partition(TG_TABLE_NAME::text, NEW.created_at);
    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

-- Áp dụng trigger tự động tạo phân vùng cho cả 4 bảng cha trước khi chèn dòng mới
DROP TRIGGER IF EXISTS trigger_mailbox_partition_on_demand ON public.mailbox;
CREATE TRIGGER trigger_mailbox_partition_on_demand
    BEFORE INSERT ON public.mailbox
    FOR EACH ROW EXECUTE FUNCTION public.route_and_create_partition_trigger();

DROP TRIGGER IF EXISTS trigger_battle_logs_partition_on_demand ON public.battle_logs;
CREATE TRIGGER trigger_battle_logs_partition_on_demand
    BEFORE INSERT ON public.battle_logs
    FOR EACH ROW EXECUTE FUNCTION public.route_and_create_partition_trigger();

DROP TRIGGER IF EXISTS trigger_player_audit_logs_partition_on_demand ON public.player_audit_logs;
CREATE TRIGGER trigger_player_audit_logs_partition_on_demand
    BEFORE INSERT ON public.player_audit_logs
    FOR EACH ROW EXECUTE FUNCTION public.route_and_create_partition_trigger();

DROP TRIGGER IF EXISTS trigger_transactions_partition_on_demand ON public.transactions;
CREATE TRIGGER trigger_transactions_partition_on_demand
    BEFORE INSERT ON public.transactions
    FOR EACH ROW EXECUTE FUNCTION public.route_and_create_partition_trigger();


-- =====================================================================
-- STORED PROCEDURES (PL/pgSQL Stored Functions) - GIAO DỊCH NGUYÊN TỬ TẦNG DB
-- Đảm bảo an toàn tuyệt đối, phòng chống Schema Hijack qua SET search_path = public
-- =====================================================================

-- a) Mua hàng trong Shop (buy_shop_item)
CREATE OR REPLACE FUNCTION public.buy_shop_item(
    p_user_id UUID,
    p_item_id VARCHAR,
    p_currency VARCHAR
)
RETURNS VARCHAR AS $$
DECLARE
    v_gold_price INT;
    v_gem_price INT;
    v_gold_balance INT;
    v_gem_balance INT;
    v_is_banned BOOLEAN;
BEGIN
    -- Khóa ví người chơi lập tức để thực hiện giao dịch nguyên tử
    SELECT gold_balance, gem_balance INTO v_gold_balance, v_gem_balance
    FROM public.wallets
    WHERE user_id = p_user_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN 'ERROR: Không tìm thấy ví của người chơi.';
    END IF;

    -- Kiểm tra trạng thái tài khoản
    SELECT is_banned INTO v_is_banned FROM public.profiles WHERE id = p_user_id;
    IF v_is_banned THEN
        RETURN 'ERROR: Tài khoản của nghĩa sĩ đã bị khóa.';
    END IF;

    -- Lấy giá vật phẩm từ game_items
    SELECT price_gold, price_gem INTO v_gold_price, v_gem_price
    FROM public.game_items
    WHERE id = p_item_id;

    IF NOT FOUND THEN
        RETURN 'ERROR: Không tìm thấy vật phẩm.';
    END IF;

    -- Xử lý trừ tiền theo đơn vị yêu cầu
    IF LOWER(p_currency) = 'gold' THEN
        IF v_gold_price <= 0 THEN
            RETURN 'ERROR: Vật phẩm không bán bằng Vàng.';
        END IF;
        IF v_gold_balance < v_gold_price THEN
            RETURN 'ERROR: Số dư Vàng không đủ.';
        END IF;
        UPDATE public.wallets 
        SET gold_balance = gold_balance - v_gold_price, version = version + 1, updated_at = now()
        WHERE user_id = p_user_id;
    ELSIF LOWER(p_currency) = 'gem' THEN
        IF v_gem_price <= 0 THEN
            RETURN 'ERROR: Vật phẩm không bán bằng Ngọc.';
        END IF;
        IF v_gem_balance < v_gem_price THEN
            RETURN 'ERROR: Số dư Ngọc không đủ.';
        END IF;
        UPDATE public.wallets 
        SET gem_balance = gem_balance - v_gem_price, version = version + 1, updated_at = now()
        WHERE user_id = p_user_id;
    ELSE
        RETURN 'ERROR: Đơn vị thanh toán không hợp lệ.';
    END IF;

    -- UPSERT rương đồ và loại bỏ xóa mềm
    INSERT INTO public.player_inventories (user_id, item_id, quantity, acquired_at, updated_at)
    VALUES (p_user_id, p_item_id, 1, now(), now())
    ON CONFLICT (user_id, item_id) WHERE deleted_at IS NULL DO UPDATE
    SET quantity = public.player_inventories.quantity + 1, acquired_at = now(), updated_at = now(), deleted_at = NULL;

    -- Ghi nhận lịch sử giao dịch tài chính (transactions) hoàn hảo
    INSERT INTO public.transactions (user_id, transaction_type, amount_gold, amount_gem, status, created_at, updated_at)
    VALUES (
        p_user_id, 
        'ShopPurchase', 
        CASE WHEN LOWER(p_currency) = 'gold' THEN -v_gold_price ELSE 0 END,
        CASE WHEN LOWER(p_currency) = 'gem' THEN -v_gem_price ELSE 0 END,
        'Completed',
        now(),
        now()
    );

    RETURN 'SUCCESS';
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;


-- Hàm dọn dẹp đặt chỗ hết hạn trên chợ (marketplace_reservations)
CREATE OR REPLACE FUNCTION public.cleanup_expired_reservations()
RETURNS VOID AS $$
DECLARE
    v_expired_listing_id BIGINT;
BEGIN
    FOR v_expired_listing_id IN
        SELECT listing_id FROM public.marketplace_reservations
        WHERE (now() - reserved_at) >= INTERVAL '5 minutes'
    LOOP
        DELETE FROM public.marketplace_reservations WHERE listing_id = v_expired_listing_id;
        
        UPDATE public.marketplace_listings
        SET status = 'Active', updated_at = now()
        WHERE id = v_expired_listing_id AND status = 'Reserved';
    END LOOP;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;


-- b) Mua hàng trên chợ P2P (buy_marketplace_item)
CREATE OR REPLACE FUNCTION public.buy_marketplace_item(
    p_buyer_id UUID,
    p_listing_id BIGINT
)
RETURNS VARCHAR AS $$
DECLARE
    v_seller_id UUID;
    v_item_id VARCHAR;
    v_price_gold INT;
    v_price_gem INT;
    v_inv_id BIGINT;
    v_status VARCHAR;
    v_gold_balance INT;
    v_gem_balance INT;
    v_gold_tax INT;
    v_gem_tax INT;
    v_gold_payout INT;
    v_gem_payout INT;
    v_first_id UUID;
    v_second_id UUID;
    v_reserved_user UUID;
    v_reserved_at TIMESTAMP WITH TIME ZONE;
BEGIN
    -- Dọn dẹp lười biếng các đặt chỗ đã hết hạn trước khi xử lý
    PERFORM public.cleanup_expired_reservations();

    -- 1. Khóa bài đăng trên chợ
    SELECT seller_id, inventory_item_id, price_gold, price_gem, status
    INTO v_seller_id, v_inv_id, v_price_gold, v_price_gem, v_status
    FROM public.marketplace_listings
    WHERE id = p_listing_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN 'ERROR: Bài đăng bán không tồn tại.';
    END IF;

    IF v_status != 'Active' AND v_status != 'Reserved' THEN
        RETURN 'ERROR: Bài đăng đã hoàn thành hoặc đã bị hủy.';
    END IF;

    IF v_seller_id = p_buyer_id THEN
        RETURN 'ERROR: Không thể tự mua hàng của chính mình.';
    END IF;

    -- 2. Kiểm tra đặt chỗ (Reservation Check với 5 phút timeout)
    SELECT user_id, reserved_at INTO v_reserved_user, v_reserved_at
    FROM public.marketplace_reservations
    WHERE listing_id = p_listing_id;

    IF FOUND THEN
        IF v_reserved_user != p_buyer_id AND (now() - v_reserved_at) < INTERVAL '5 minutes' THEN
            RETURN 'ERROR: Vật phẩm này đã được đặt chỗ giữ hàng bởi nghĩa sĩ khác.';
        END IF;
        
        DELETE FROM public.marketplace_reservations WHERE listing_id = p_listing_id;
    END IF;

    -- 3. Sắp xếp Guid tránh deadlock khi khóa ví
    IF p_buyer_id < v_seller_id THEN
        v_first_id := p_buyer_id;
        v_second_id := v_seller_id;
    ELSE
        v_first_id := v_seller_id;
        v_second_id := p_buyer_id;
    END IF;

    -- Khóa ví hai người chơi theo thứ tự tăng dần
    PERFORM 1 FROM public.wallets WHERE user_id = v_first_id FOR UPDATE;
    PERFORM 1 FROM public.wallets WHERE user_id = v_second_id FOR UPDATE;

    -- Lấy số dư ví người mua
    SELECT gold_balance, gem_balance INTO v_gold_balance, v_gem_balance
    FROM public.wallets WHERE user_id = p_buyer_id;

    -- Kiểm tra số dư người mua
    IF v_price_gold > 0 AND v_gold_balance < v_price_gold THEN
        RETURN 'ERROR: Số dư Vàng người mua không đủ.';
    END IF;
    IF v_price_gem > 0 AND v_gem_balance < v_price_gem THEN
        RETURN 'ERROR: Số dư Ngọc người mua không đủ.';
    END IF;

    -- Khấu trừ người mua
    UPDATE public.wallets
    SET gold_balance = gold_balance - v_price_gold,
        gem_balance = gem_balance - v_price_gem,
        version = version + 1,
        updated_at = now()
    WHERE user_id = p_buyer_id;

    -- Thuế 5% và cộng tiền cho người bán
    v_gold_tax := CEIL(v_price_gold * 0.05);
    v_gem_tax := CEIL(v_price_gem * 0.05);
    v_gold_payout := v_price_gold - v_gold_tax;
    v_gem_payout := v_price_gem - v_gem_tax;

    UPDATE public.wallets
    SET gold_balance = gold_balance + v_gold_payout,
        gem_balance = gem_balance + v_gem_payout,
        version = version + 1,
        updated_at = now()
    WHERE user_id = v_seller_id;

    -- Lấy item_id từ rương của người bán
    SELECT item_id INTO v_item_id FROM public.player_inventories WHERE id = v_inv_id;

    -- Trao vật phẩm cho người mua (UPSERT)
    INSERT INTO public.player_inventories (user_id, item_id, quantity, acquired_at, updated_at)
    VALUES (p_buyer_id, v_item_id, 1, now(), now())
    ON CONFLICT (user_id, item_id) WHERE deleted_at IS NULL DO UPDATE
    SET quantity = public.player_inventories.quantity + 1, acquired_at = now(), updated_at = now(), deleted_at = NULL;

    -- Cập nhật bài đăng chợ sang Sold
    UPDATE public.marketplace_listings
    SET status = 'Sold', buyer_id = p_buyer_id, updated_at = now()
    WHERE id = p_listing_id;

    -- Ghi log tài chính cho người mua (giảm)
    INSERT INTO public.transactions (user_id, transaction_type, amount_gold, amount_gem, reference_id, status, created_at, updated_at)
    VALUES (p_buyer_id, 'MarketplacePurchase', -v_price_gold, -v_price_gem, 'LISTING-' || p_listing_id, 'Completed', now(), now());

    -- Ghi log tài chính cho người bán (tăng)
    INSERT INTO public.transactions (user_id, transaction_type, amount_gold, amount_gem, reference_id, status, created_at, updated_at)
    VALUES (v_seller_id, 'MarketplaceSale', v_gold_payout, v_gem_payout, 'LISTING-' || p_listing_id, 'Completed', now(), now());

    RETURN 'SUCCESS';
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;


-- c) Nhận quà từ hòm thư (claim_mailbox_attachments)
CREATE OR REPLACE FUNCTION public.claim_mailbox_attachments(
    p_user_id UUID,
    p_mail_id BIGINT
)
RETURNS VARCHAR AS $$
DECLARE
    v_receiver_id UUID;
    v_attachments JSONB;
    v_is_claimed BOOLEAN;
    v_expired_at TIMESTAMP WITH TIME ZONE;
    v_gold_reward INT := 0;
    v_gem_reward INT := 0;
    v_item_record RECORD;
    v_affected INT;
BEGIN
    -- 1. Khóa dòng thư để claim an toàn
    SELECT receiver_id, attachments, is_claimed, expired_at
    INTO v_receiver_id, v_attachments, v_is_claimed, v_expired_at
    FROM public.mailbox
    WHERE id = p_mail_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN 'ERROR: Thư không tồn tại.';
    END IF;

    IF v_expired_at IS NOT NULL AND v_expired_at < now() THEN
        RETURN 'ERROR: Thư này đã hết hạn.';
    END IF;

    IF v_attachments IS NULL THEN
        RETURN 'ERROR: Thư này không có quà đính kèm.';
    END IF;

    -- 2. Kiểm tra quyền sở hữu & đã nhận quà chưa
    IF v_receiver_id IS NOT NULL THEN
        IF v_receiver_id != p_user_id THEN
            RETURN 'ERROR: Thư này không thuộc sở hữu của bạn.';
        END IF;
        IF v_is_claimed THEN
            RETURN 'ERROR: Quà đã được nhận trước đó.';
        END IF;
        -- Cập nhật trạng thái nhận quà thư cá nhân
        UPDATE public.mailbox SET is_claimed = true, is_read = true, updated_at = now() WHERE id = p_mail_id;
    ELSE
        -- Thư Broadcast: Chèn dòng claims nguyên tử chống nhận trùng (khi nhận quà nghĩa là đã đọc và đã nhận)
        INSERT INTO public.player_broadcast_claims (user_id, mail_id, is_read, is_claimed, claimed_at)
        VALUES (p_user_id, p_mail_id, true, true, now())
        ON CONFLICT (user_id, mail_id) DO NOTHING;
        
        GET DIAGNOSTICS v_affected = ROW_COUNT;
        IF v_affected = 0 THEN
            RETURN 'ERROR: Nghĩa sĩ đã nhận quà từ thư broadcast này rồi.';
        END IF;
    END IF;

    -- Khóa ví người chơi để cộng tiền an toàn
    PERFORM 1 FROM public.wallets WHERE user_id = p_user_id FOR UPDATE;

    -- 3. Phân tích quà tặng đính kèm từ JSON
    IF v_attachments ? 'gold' THEN
        v_gold_reward := (v_attachments->>'gold')::INT;
    END IF;
    IF v_attachments ? 'gems' THEN
        v_gem_reward := (v_attachments->>'gems')::INT;
    END IF;

    -- Cộng vàng/ngọc
    IF v_gold_reward > 0 OR v_gem_reward > 0 THEN
        UPDATE public.wallets
        SET gold_balance = gold_balance + v_gold_reward,
            gem_balance = gem_balance + v_gem_reward,
            version = version + 1,
            updated_at = now()
        WHERE user_id = p_user_id;

        -- Ghi log tài chính cho giao dịch nhận thưởng hòm thư công khai/cá nhân
        INSERT INTO public.transactions (user_id, transaction_type, amount_gold, amount_gem, reference_id, status, created_at, updated_at)
        VALUES (p_user_id, 'MailboxClaim', v_gold_reward, v_gem_reward, 'MAIL-' || p_mail_id, 'Completed', now(), now());
    END IF;

    -- Cộng vật phẩm nếu có
    IF v_attachments ? 'items' AND jsonb_typeof(v_attachments->'items') = 'array' THEN
        FOR v_item_record IN 
            SELECT (value->>'id')::VARCHAR AS item_id, (value->>'qty')::INT AS qty
            FROM jsonb_array_elements(v_attachments->'items')
        LOOP
            IF v_item_record.item_id IS NOT NULL AND v_item_record.qty > 0 THEN
                INSERT INTO public.player_inventories (user_id, item_id, quantity, acquired_at, updated_at)
                VALUES (p_user_id, v_item_record.item_id, v_item_record.qty, now(), now())
                ON CONFLICT (user_id, item_id) WHERE deleted_at IS NULL DO UPDATE
                SET quantity = public.player_inventories.quantity + v_item_record.qty, acquired_at = now(), updated_at = now(), deleted_at = NULL;
            END IF;
        END LOOP;
    END IF;

    RETURN 'SUCCESS';
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;
