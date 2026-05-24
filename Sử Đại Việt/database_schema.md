# Hướng Dẫn Thiết Lập & Tài Liệu Database - Sử Đại Việt Backend

Tài liệu này được tạo ra để lưu trữ trực tiếp trong thư mục dự án Backend, giúp lập trình viên Backend hoặc các công cụ AI hỗ trợ hiểu rõ cấu trúc cơ sở dữ liệu PostgreSQL (chạy trên Supabase BaaS) và định hướng tích hợp.

---

## 🛠️ 1. SCRIPT SQL KHỞI TẠO HOÀN CHỈNH (database_setup.sql)

Hãy copy toàn bộ đoạn mã SQL dưới đây và chạy trực tiếp trong trang **SQL Editor** trên Supabase để triển khai hạ tầng dữ liệu:

```sql
-- =====================================================================
-- SCRIPT SQL TOÀN DIỆN: HỆ THỐNG DATABASE "SỬ ĐẠI VIỆT" 
-- Hỗ trợ: Email, Số điện thoại (Phone OTP), Đăng nhập MXH (Google, Facebook, Apple)
-- =====================================================================

-- 1. BẢNG HỒ SƠ NGƯỜI CHƠI (profiles)
-- Cho phép 'email' và 'phone' nhận giá trị NULL để hỗ trợ linh hoạt:
-- Đăng nhập bằng số điện thoại sẽ không có email, và đăng nhập bằng email sẽ không có số điện thoại.
CREATE TABLE IF NOT EXISTS public.profiles (
    id UUID REFERENCES auth.users ON DELETE CASCADE PRIMARY KEY,  -- Liên kết trực tiếp sang bảng tài khoản hệ thống của Supabase Auth
    email VARCHAR(255) NULL,                                      -- Địa chỉ Email (Cho phép NULL để hỗ trợ Phone Login / Social Login)
    phone VARCHAR(50) NULL,                                       -- Hỗ trợ đăng nhập qua Số Điện Thoại
    display_name VARCHAR(50) DEFAULT 'Nghĩa Sĩ' NOT NULL,         -- Tên hiển thị trong game
    avatar_url VARCHAR(500) NULL,                                 -- Ảnh đại diện từ Google/Facebook/Apple
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- Kích hoạt RLS (Row Level Security) bảo mật
ALTER TABLE public.profiles ENABLE ROW LEVEL SECURITY;

-- Tạo các quyền truy cập (Policy) bảo mật:
-- Ai cũng có thể xem hồ sơ công khai (để hiện tên/ảnh đại diện trên Leaderboard)
DROP POLICY IF EXISTS "Cho phép xem hồ sơ công khai" ON public.profiles;
CREATE POLICY "Cho phép xem hồ sơ công khai" 
    ON public.profiles FOR SELECT 
    USING (true);

-- Chỉ chính chủ sở hữu tài khoản mới được cập nhật tên hiển thị/ảnh đại diện của mình
DROP POLICY IF EXISTS "Chỉ người dùng mới được sửa hồ sơ của mình" ON public.profiles;
CREATE POLICY "Chỉ người dùng mới được sửa hồ sơ của mình" 
    ON public.profiles FOR UPDATE 
    USING (auth.uid() = id);


-- 2. BẢNG XẾP HẠNG TRỰC TUYẾN (leaderboard)
CREATE TABLE IF NOT EXISTS public.leaderboard (
    id BIGSERIAL PRIMARY KEY,                                      -- Khóa chính tự tăng
    user_id UUID REFERENCES public.profiles(id) ON DELETE CASCADE NOT NULL, -- Liên kết đến bảng profiles công khai
    username VARCHAR(50) NOT NULL,                                 -- Tên người chơi hiển thị trên bảng xếp hạng
    score INTEGER DEFAULT 0 NOT NULL,                             -- Điểm số cao nhất
    stage_reached VARCHAR(50) DEFAULT 'Ải 1' NOT NULL,             -- Ải cao nhất đã vượt qua
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- Index sắp xếp điểm số để truy vấn Top 10 siêu tốc
CREATE INDEX IF NOT EXISTS leaderboard_score_idx ON public.leaderboard (score DESC);

-- Kích hoạt RLS bảo mật cho bảng leaderboard
ALTER TABLE public.leaderboard ENABLE ROW LEVEL SECURITY;

-- Ai cũng được xem bảng xếp hạng vinh danh
DROP POLICY IF EXISTS "Cho phép tất cả mọi người xem Bảng Xếp Hạng" ON public.leaderboard;
CREATE POLICY "Cho phép tất cả mọi người xem Bảng Xếp Hạng" 
    ON public.leaderboard FOR SELECT 
    USING (true);

-- Chỉ người chơi đã đăng nhập (được xác thực qua JWT token) mới được đẩy điểm của mình lên
DROP POLICY IF EXISTS "Chỉ người chơi đã đăng nhập mới được thêm/sửa điểm của mình" ON public.leaderboard;
CREATE POLICY "Chỉ người chơi đã đăng nhập mới được thêm/sửa điểm của mình" 
    ON public.leaderboard FOR ALL 
    TO authenticated
    USING (auth.uid() = user_id)
    WITH CHECK (auth.uid() = user_id);


-- 3. BẢNG CẤU HÌNH TỪ XA (game_config)
-- Quản lý các thông số sức mạnh của 3 nhân vật Nguyễn Huệ, Nguyễn Nhạc, Nguyễn Lữ
CREATE TABLE IF NOT EXISTS public.game_config (
    config_key VARCHAR(100) PRIMARY KEY,                          -- Từ khóa cấu hình (Ví dụ: hue_damage, nhac_speed)
    config_value NUMERIC NOT NULL,                                -- Giá trị cấu hình
    description TEXT,                                             -- Mô tả chi tiết chỉ số
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

ALTER TABLE public.game_config ENABLE ROW LEVEL SECURITY;

-- Cho phép tất cả mọi người chơi (Game client) đọc cấu hình chỉ số nhân vật từ xa
DROP POLICY IF EXISTS "Cho phép mọi client đọc cấu hình game" ON public.game_config;
CREATE POLICY "Cho phép mọi client đọc cấu hình game" 
    ON public.game_config FOR SELECT 
    USING (true);

-- Chỉ cho phép hệ thống chỉnh sửa thông qua backend bảo mật (PUT api/Config có mã AdminKey)
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


-- 4. TRIGGER THẦN KỲ TỰ ĐỘNG ĐỒNG BỘ MỌI PHƯƠNG THỨC ĐĂNG NHẬP
-- Hàm này phân tích Metadata của tài khoản đăng ký ở Supabase Auth để lấy thông tin:
-- - Nếu qua Google/Facebook: Trích xuất 'full_name', 'name' và 'avatar_url' từ MXH.
-- - Nếu qua Số điện thoại: Trích xuất số điện thoại làm tên đại diện tạm thời.
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
  
  -- Cắt ngắn chuỗi nếu tên từ MXH vượt quá giới hạn 50 ký tự
  extracted_name := SUBSTRING(extracted_name FROM 1 FOR 50);

  -- B. Trích xuất các trường thông tin cơ bản khác
  extracted_email := new.email;
  extracted_phone := new.phone;
  extracted_avatar := new.raw_user_meta_data->>'avatar_url';

  -- C. Chèn hoặc cập nhật hồ sơ đồng bộ (UPSERT tránh lỗi xung đột)
  INSERT INTO public.profiles (id, email, phone, display_name, avatar_url)
  VALUES (
    new.id,
    extracted_email,
    extracted_phone,
    extracted_name,
    extracted_avatar
  )
  ON CONFLICT (id) DO UPDATE
  SET email = EXCLUDED.email,
      phone = EXCLUDED.phone,
      display_name = EXCLUDED.display_name,
      avatar_url = EXCLUDED.avatar_url;

  RETURN new;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- Đăng ký trigger tự động kích hoạt sau khi tạo tài khoản Auth thành công
CREATE OR REPLACE TRIGGER on_auth_user_created
  AFTER INSERT ON auth.users
  FOR EACH ROW EXECUTE FUNCTION public.handle_new_user();
```

---

## 🧭 2. HƯỚNG DẪN KẾT NỐI CHO LẬP TRÌNH VIÊN C# BACKEND (EF CORE)

1.  **Kết nối trực tiếp PostgreSQL:**
    *   Sử dụng NuGet Package `Npgsql.EntityFrameworkCore.PostgreSQL`.
    *   Chuỗi kết nối cấu hình trong file `appsettings.json` tại mục `ConnectionStrings:SupabaseConnection`.
2.  **Đồng bộ Model Entity:**
    *   Model `Profile` ánh xạ đến `profiles` trong database. Cần chú ý thuộc tính `Email`, `Phone` và `AvatarUrl` là dạng nullable (`string?`) để tránh gây crash hệ thống khi người chơi đăng nhập qua số điện thoại hoặc mạng xã hội.
3.  **Tối ưu hóa truy vấn:**
    *   Luôn sử dụng `.AsNoTracking()` đối với các API chỉ đọc (như API hiển thị bảng xếp hạng và API lấy chỉ số tướng) để tối ưu RAM và đẩy nhanh tốc độ phản hồi.
4.  **Bảo mật API:**
    *   Cấu hình chính sách CORS để chỉ cho phép các máy khách React Frontend (`http://localhost:3000` / `http://localhost:5173`) truy cập an toàn.
    *   Sử dụng xác thực API Key thông qua Header `X-Admin-Key` để bảo vệ các thay đổi cấu hình tướng, tránh trường hợp game thủ tự gửi request PUT để cheat game.
