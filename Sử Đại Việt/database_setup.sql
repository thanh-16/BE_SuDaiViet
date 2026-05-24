-- =====================================================================
-- SCRIPT SQL TOÀN DIỆN & BẢO MẬT: HỆ THỐNG DATABASE "SỬ ĐẠI VIỆT" 
-- Hỗ trợ: Email, Số điện thoại (Phone OTP), Đăng nhập MXH (Google, Facebook, Apple)
-- Tương thích hoàn hảo với các tính năng tối cao của Web Admin
-- =====================================================================

-- ---------------------------------------------------------------------
-- 1. BẢNG HỒ SƠ NGƯỜI CHƠI (profiles)
-- ---------------------------------------------------------------------
-- Cho phép 'email' và 'phone' nhận giá trị NULL để hỗ trợ đăng nhập đa phương thức linh hoạt
CREATE TABLE IF NOT EXISTS public.profiles (
    id UUID REFERENCES auth.users ON DELETE CASCADE PRIMARY KEY,  -- Liên kết trực tiếp sang tài khoản của Supabase Auth
    email VARCHAR(255) NULL,                                      -- Địa chỉ Email (Cho phép NULL)
    phone VARCHAR(50) NULL,                                       -- Hỗ trợ đăng nhập qua Số Điện Thoại (Cho phép NULL)
    display_name VARCHAR(50) DEFAULT 'Nghĩa Sĩ' NOT NULL,         -- Tên hiển thị trong game
    avatar_url VARCHAR(500) NULL,                                 -- Ảnh đại diện lấy từ Google/Facebook/Apple
    is_banned BOOLEAN DEFAULT false NOT NULL,                     -- Trạng thái khóa tài khoản người chơi
    role VARCHAR(20) DEFAULT 'player' NOT NULL,                   -- Vai trò trong game: 'player' (người chơi) hoặc 'admin' (quản trị viên)
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- Kích hoạt RLS (Row Level Security) bảo mật mức dòng
ALTER TABLE public.profiles ENABLE ROW LEVEL SECURITY;

-- Tạo các chính sách bảo mật:
-- Ai cũng có thể xem hồ sơ công khai (để hiện tên/ảnh đại diện trên Bảng Xếp Hạng)
DROP POLICY IF EXISTS "Cho phép xem hồ sơ công khai" ON public.profiles;
CREATE POLICY "Cho phép xem hồ sơ công khai" 
    ON public.profiles FOR SELECT 
    USING (true);

-- Chỉ chính chủ sở hữu tài khoản mới được cập nhật hồ sơ của mình
DROP POLICY IF EXISTS "Chỉ người dùng mới được sửa hồ sơ của mình" ON public.profiles;
CREATE POLICY "Chỉ người dùng mới được sửa hồ sơ của mình" 
    ON public.profiles FOR UPDATE 
    USING (auth.uid() = id);


-- ---------------------------------------------------------------------
-- 2. BẢNG XẾP HẠNG TRỰC TUYẾN (leaderboard)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.leaderboard (
    id BIGSERIAL PRIMARY KEY,                                      -- Khóa chính tự tăng
    user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL, -- Liên kết đến hồ sơ người chơi
    username VARCHAR(50) NOT NULL,                                 -- Tên người chơi hiển thị trên bảng xếp hạng
    score INTEGER DEFAULT 0 NOT NULL,                             -- Điểm số cao nhất lập chiến công
    stage_reached VARCHAR(50) DEFAULT 'Ải 1' NOT NULL,             -- Ải cao nhất đã vượt qua
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- Index sắp xếp điểm số để truy vấn Top 10 siêu tốc
CREATE INDEX IF NOT EXISTS leaderboard_score_idx ON public.leaderboard (score DESC);

-- RÀNG BUỘC DUY NHẤT: Mỗi người dùng chỉ được có tối đa 1 dòng trên bảng xếp hạng công khai (Đồng bộ với EF Core Unique)
CREATE UNIQUE INDEX IF NOT EXISTS leaderboard_user_id_unique_idx ON public.leaderboard(user_id);

-- Kích hoạt RLS bảo mật cho bảng leaderboard
ALTER TABLE public.leaderboard ENABLE ROW LEVEL SECURITY;

-- Ai cũng được quyền xem bảng xếp hạng vinh danh
DROP POLICY IF EXISTS "Cho phép tất cả mọi người xem Bảng Xếp Hạng" ON public.leaderboard;
CREATE POLICY "Cho phép tất cả mọi người xem Bảng Xếp Hạng" 
    ON public.leaderboard FOR SELECT 
    USING (true);

-- Chỉ người chơi đã đăng nhập (được xác thực qua JWT token của Supabase) mới được đẩy điểm của mình lên
DROP POLICY IF EXISTS "Chỉ người chơi đã đăng nhập mới được thêm/sửa điểm của mình" ON public.leaderboard;
CREATE POLICY "Chỉ người chơi đã đăng nhập mới được thêm/sửa điểm của mình" 
    ON public.leaderboard FOR ALL 
    TO authenticated
    USING (auth.uid() = user_id)
    WITH CHECK (auth.uid() = user_id);


-- ---------------------------------------------------------------------
-- 3. BẢNG CẤU HÌNH TỪ XA (game_config)
-- ---------------------------------------------------------------------
-- Quản lý các thông số sức mạnh cơ bản của Nguyễn Huệ, Nguyễn Nhạc, Nguyễn Lữ
CREATE TABLE IF NOT EXISTS public.game_config (
    config_key VARCHAR(100) PRIMARY KEY,                          -- Từ khóa cấu hình (Ví dụ: hue_damage, nhac_speed)
    config_value NUMERIC NOT NULL,                                -- Giá trị cấu hình
    description TEXT,                                             -- Mô tả chi tiết chỉ số
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- Kích hoạt RLS bảo mật cho bảng cấu hình game
ALTER TABLE public.game_config ENABLE ROW LEVEL SECURITY;

-- Cho phép tất cả mọi người chơi (Game client) đọc cấu hình chỉ số nhân vật từ xa để nạp lúc mở game
DROP POLICY IF EXISTS "Cho phép mọi client đọc cấu hình game" ON public.game_config;
CREATE POLICY "Cho phép mọi client đọc cấu hình game" 
    ON public.game_config FOR SELECT 
    USING (true);

-- Cấm tuyệt đối client sửa đổi trực tiếp cấu hình. Chỉ hệ thống thông qua Backend bảo mật (X-Admin-Key) mới được sửa đổi
DROP POLICY IF EXISTS "Chỉ cho phép sửa đổi cấu hình từ hệ thống" ON public.game_config;
CREATE POLICY "Chỉ cho phép sửa đổi cấu hình từ hệ thống" 
    ON public.game_config FOR ALL 
    USING (false);

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
-- 4. BẢNG NHẬT KÝ KIỂM TOÁN HOẠT ĐỘNG ADMIN (admin_logs)
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

-- Chỉ cho phép hệ thống đọc/ghi, cấm client can thiệp trực tiếp từ FE bằng token người chơi thường
DROP POLICY IF EXISTS "Chỉ hệ thống được quyền thao tác với logs" ON public.admin_logs;
CREATE POLICY "Chỉ hệ thống được quyền thao tác với logs" 
    ON public.admin_logs FOR ALL 
    USING (false);


-- ---------------------------------------------------------------------
-- 5. TRIGGER THẦN KỲ TỰ ĐỘNG ĐỒNG BỘ MỌI PHƯƠNG THỨC ĐĂNG NHẬP
-- ---------------------------------------------------------------------
-- Hàm này tự động phân tích Metadata của tài khoản đăng ký ở Supabase Auth:
-- - Nếu qua Google/Facebook: Trích xuất 'full_name', 'name' và 'avatar_url' từ tài khoản mạng xã hội.
-- - Nếu qua Số điện thoại: Trích xuất số điện thoại làm tên hiển thị tạm thời.
-- - Nếu qua Email: Trích xuất phần chữ trước dấu '@' làm tên.
CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS trigger AS $$
DECLARE
  extracted_name VARCHAR(50);
  extracted_email VARCHAR(255);
  extracted_phone VARCHAR(50);
  extracted_avatar VARCHAR(500);
BEGIN
  -- A. Lấy display_name từ các trường metadata khác nhau
  extracted_name := COALESCE(
    new.raw_user_meta_data->>'display_name',
    new.raw_user_meta_data->>'full_name',
    new.raw_user_meta_data->>'name',
    split_part(new.email, '@', 1),
    new.phone,
    'Nghĩa Sĩ Tây Sơn'
  );
  
  -- Cắt ngắn chuỗi nếu tên từ mạng xã hội vượt quá giới hạn 50 ký tự
  extracted_name := SUBSTRING(extracted_name FROM 1 FOR 50);

  -- B. Trích xuất các trường thông tin cơ bản khác
  extracted_email := new.email;
  extracted_phone := new.phone;
  extracted_avatar := new.raw_user_meta_data->>'avatar_url';

  -- C. Chèn hoặc cập nhật hồ sơ đồng bộ (UPSERT tránh lỗi xung đột)
  -- Trạng thái mặc định: is_banned = false, role = 'player'
  INSERT INTO public.profiles (id, email, phone, display_name, avatar_url, is_banned, role)
  VALUES (
    new.id,
    extracted_email,
    extracted_phone,
    extracted_name,
    extracted_avatar,
    false,
    'player'
  )
  ON CONFLICT (id) DO UPDATE
  SET email = EXCLUDED.email,
      phone = EXCLUDED.phone,
      display_name = EXCLUDED.display_name,
      avatar_url = EXCLUDED.avatar_url;

  RETURN new;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- Đăng ký trigger tự động kích hoạt sau khi tạo tài khoản Auth trên Supabase thành công
-- Đăng ký trigger tự động kích hoạt sau khi tạo tài khoản Auth trên Supabase thành công
CREATE OR REPLACE TRIGGER on_auth_user_created
  AFTER INSERT ON auth.users
  FOR EACH ROW EXECUTE FUNCTION public.handle_new_user();


-- =====================================================================
-- NÂNG CẤP BẢO MẬT & KINH TẾ GAME: CỬA HÀNG VẬT PHẨM & GIAO DỊCH
-- =====================================================================

-- 1. Bổ sung trường GoldBalance và GemBalance vào profiles
ALTER TABLE public.profiles 
ADD COLUMN IF NOT EXISTS gold_balance INTEGER DEFAULT 0 NOT NULL,
ADD COLUMN IF NOT EXISTS gem_balance INTEGER DEFAULT 0 NOT NULL;

-- 2. Bảng Danh mục Vật phẩm Game (game_items)
CREATE TABLE IF NOT EXISTS public.game_items (
    id VARCHAR(50) PRIMARY KEY,                                    -- Mã vật phẩm (Ví dụ: 'sword_001', 'hp_potion')
    name VARCHAR(100) NOT NULL,                                   -- Tên vật phẩm
    description TEXT,                                             -- Mô tả vật phẩm
    price_gold INTEGER DEFAULT 0 NOT NULL,                        -- Giá bằng Vàng (Gold)
    price_gem INTEGER DEFAULT 0 NOT NULL,                         -- Giá bằng Ngọc (Gem)
    price_vnd INTEGER DEFAULT 0 NOT NULL,                         -- Giá bằng Tiền mặt VND (cho vật phẩm nạp trực tiếp)
    item_type VARCHAR(30) DEFAULT 'Consumable' NOT NULL,           -- Loại: 'Equipment', 'Consumable', 'Skin'
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- Kích hoạt RLS bảo mật danh mục vật phẩm
ALTER TABLE public.game_items ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Cho phép tất cả mọi người đọc danh mục vật phẩm" ON public.game_items;
CREATE POLICY "Cho phép tất cả mọi người đọc danh mục vật phẩm" 
    ON public.game_items FOR SELECT 
    USING (true);

DROP POLICY IF EXISTS "Chỉ hệ thống được quyền thao tác vật phẩm" ON public.game_items;
CREATE POLICY "Chỉ hệ thống được quyền thao tác vật phẩm" 
    ON public.game_items FOR ALL 
    USING (false);

-- Chèn dữ liệu vật phẩm mẫu ban đầu
INSERT INTO public.game_items (id, name, description, price_gold, price_gem, price_vnd, item_type)
VALUES
    ('pot_hp_01', 'Bình Trị Thương Lớn', 'Hồi phục 50% sinh lực trong trận chiến trận Rạch Gầm', 500, 0, 0, 'Consumable'),
    ('sword_hue_01', 'Thuận Thiên Kiếm (Nguyễn Huệ Skin)', 'Diện mạo cực kỳ uy nghi của Quang Trung hoàng đế', 0, 500, 50000, 'Skin'),
    ('armor_nhac_01', 'Long Lân Giáp', 'Tăng 20% khả năng chống đỡ sát thương', 2000, 50, 0, 'Equipment')
ON CONFLICT (id) DO NOTHING;

-- 3. Bảng Kho đồ sở hữu người chơi (player_inventories)
CREATE TABLE IF NOT EXISTS public.player_inventories (
    id BIGSERIAL PRIMARY KEY,
    user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
    item_id VARCHAR(50) REFERENCES public.game_items(id) ON DELETE CASCADE NOT NULL,
    quantity INTEGER DEFAULT 1 NOT NULL,
    acquired_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    CONSTRAINT unique_user_item UNIQUE (user_id, item_id)
);

-- Kích hoạt RLS bảo mật kho đồ
ALTER TABLE public.player_inventories ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Người dùng chỉ được xem kho đồ của mình" ON public.player_inventories;
CREATE POLICY "Người dùng chỉ được xem kho đồ của mình" 
    ON public.player_inventories FOR SELECT 
    TO authenticated 
    USING (auth.uid() = user_id);

-- 4. Bảng Nhật ký Giao dịch & Nạp tiền (transactions)
CREATE TABLE IF NOT EXISTS public.transactions (
    id BIGSERIAL PRIMARY KEY,
    user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL,
    transaction_type VARCHAR(30) NOT NULL,                        -- 'Topup', 'Purchase'
    amount_vnd INTEGER DEFAULT 0 NOT NULL,                        -- Số tiền VND (Nếu nạp tiền)
    amount_gold INTEGER DEFAULT 0 NOT NULL,                       -- Biến động số lượng Vàng (Gold)
    amount_gem INTEGER DEFAULT 0 NOT NULL,                        -- Biến động số lượng Ngọc (Gem)
    payment_method VARCHAR(50) NULL,                              -- 'Momo', 'Card', 'Banking'
    reference_id VARCHAR(100) NULL,                               -- Mã giao dịch đối chiếu
    status VARCHAR(20) DEFAULT 'Completed' NOT NULL,              -- 'Pending', 'Completed', 'Failed'
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- Kích hoạt RLS bảo mật giao dịch
ALTER TABLE public.transactions ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Người dùng chỉ được xem lịch sử giao dịch của mình" ON public.transactions;
CREATE POLICY "Người dùng chỉ được xem lịch sử giao dịch của mình" 
    ON public.transactions FOR SELECT 
    TO authenticated 
    USING (auth.uid() = user_id);

