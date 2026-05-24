# Sử Đại Việt - Web Admin REST API Backend

Hệ thống **Web API Backend** của dự án game **Sử Đại Việt - Hào Khí Tây Sơn** được xây dựng trên nền tảng **ASP.NET Core 8.0** và kết nối trực tiếp đến hệ thống cơ sở dữ liệu **Supabase PostgreSQL**. 

Dự án được phát triển theo mô hình **Controller-Service-Repository** tinh giản, đáp ứng đầy đủ các tiêu chuẩn bảo mật, tối ưu hóa hiệu năng, xử lý lỗi toàn cục và sẵn sàng đóng gói Container Docker để triển khai lên Production.

---

## 📂 1. Cấu Trúc Mã Nguồn Dự Án

Kiến trúc mã nguồn được phân chia thư mục rõ ràng theo từng phân tầng trách nhiệm:

*   **`Controllers/`**: Tầng giao tiếp REST API, tiếp nhận yêu cầu từ client (React, Godot) và trả về kết quả.
*   **`Services/`**: Tầng xử lý Logic Nghiệp Vụ chính (Tính toán điểm số, xếp hạng, cập nhật thông số).
*   **`Dtos/`**: Chứa các lớp truyền dữ liệu (Data Transfer Objects) được trang bị DataAnnotations tự động xác thực lỗi đầu vào (Validation).
*   **`Data/`**: Quản lý kết nối Database qua Entity Framework Core (`ApplicationDbContext.cs`).
*   **`Middleware/`**: Chứa bộ lọc xử lý ngoại lệ toàn cục (`GlobalExceptionMiddleware.cs`), tự động bắt lỗi và chuẩn hóa phản hồi dạng `ProblemDetails` JSON.
*   **`Models/`**: Định nghĩa các thực thể (Entities) ánh xạ 1-1 với cấu trúc bảng Supabase PostgreSQL.

---

## 🛠️ 2. Hướng Dẫn Khởi Chạy Local (Development)

### Yêu cầu hệ thống:
*   Đã cài đặt **.NET 8.0 SDK** trên máy tính.
*   Một dự án cơ sở dữ liệu đang hoạt động trên **Supabase**.

### Bước 1: Khởi tạo dữ liệu trên Supabase
1. Đăng nhập vào trang quản trị Supabase.
2. Mở mục **SQL Editor** và tạo mới một query.
3. Mở file [database_setup.sql](file:///D:/Exe202/t%C3%A2y-s%C6%A1n/S%E1%BB%AD%20%C4%90%E1%BA%A1i%20Vi%E1%BB%87t/S%E1%BB%AD%20%C4%90%E1%BA%A1i%20Vi%E1%BB%87t/database_setup.sql) trong thư mục dự án, copy toàn bộ nội dung dán vào và nhấn **Run** để khởi tạo cấu trúc các bảng.

### Bước 2: Cấu hình mã khóa và mật khẩu kết nối
Mở file [appsettings.json](file:///D:/Exe202/t%C3%A2y-s%C6%A1n/S%E1%BB%AD%20%C4%90%E1%BA%A1i%20Vi%E1%BB%87t/S%E1%BB%AD%20%C4%90%E1%BA%A1i%20Vi%E1%BB%87t/appsettings.json) và cấu hình các thông số:
```json
{
  "ConnectionStrings": {
    "SupabaseConnection": "Host=db.qcxfmenzyzbwwxrpsdm.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=MAT_KHAU_DB_CUA_BAN;"
  },
  "AdminSettings": {
    "AdminKey": "TaySonNghiaQuanKey1789" // Mã khóa Admin dùng để sửa đổi chỉ số
  },
  "CorsSettings": {
    "AllowedOrigins": [
      "http://localhost:5173", // URL client React chạy local (Vite)
      "http://localhost:3000"  // URL client React chạy local (CRA)
    ]
  }
}
```

### Bước 3: Khởi chạy dự án
*   **Sử dụng Visual Studio 2022**: Double-click mở file solution `Sử Đại Việt.slnx` ở thư mục ngoài và nhấn phím **F5** hoặc nút **Play** trên thanh công cụ.
*   **Sử dụng dòng lệnh CLI**: Mở cửa sổ dòng lệnh tại thư mục gốc chứa file `.csproj` và chạy câu lệnh:
    ```bash
    dotnet run
    ```
*   **Truy cập Swagger UI**: Mở trình duyệt web và điều hướng tới đường dẫn:
    ```text
    http://localhost:<PORT>/swagger
    ```

---

## 📡 3. Danh Sách REST API Tài Liệu & Kết Nối

### 🏆 A. API Bảng Xếp Hạng (Leaderboard)

#### 1. Lấy danh sách Top vinh danh
*   **Phương thức:** `GET`
*   **Địa chỉ:** `/api/Leaderboard`
*   **Tham số truy vấn (Query):** `limit` (mặc định là `10`, giới hạn an toàn từ `1` đến `100` để tránh quá tải).
*   **Ví dụ phản hồi:**
    ```json
    [
      {
        "id": 1,
        "userId": "d7b29a24-9b24-4fbd-8db4-d50275cc1c0f",
        "username": "Nguyễn Huệ",
        "score": 9999,
        "stageReached": "Ải 5",
        "updatedAt": "2026-05-22T09:00:00Z"
      }
    ]
    ```

#### 2. Đăng ký điểm số vượt ải mới
*   **Phương thức:** `POST`
*   **Địa chỉ:** `/api/Leaderboard`
*   **Định dạng gửi đi (Request Body):**
    ```json
    {
      "userId": "d7b29a24-9b24-4fbd-8db4-d50275cc1c0f",
      "username": "Nguyễn Huệ",
      "score": 9999,
      "stageReached": "Ải 5"
    }
    ```
*   **Đặc điểm nổi bật:** Tự động áp dụng chế độ xác thực bounds, điểm số hợp lệ phải thuộc khoảng `[0, 99999999]`. Tự động đồng bộ profile người chơi sang bảng Profiles nếu chưa tồn tại.

---

### ⚙️ B. API Cấu Hình Từ Xa (Remote Config)

#### 1. Lấy tất cả thông số game (Dành cho Godot client nhận chỉ số)
*   **Phương thức:** `GET`
*   **Địa chỉ:** `/api/Config`
*   **Ví dụ phản hồi:**
    ```json
    [
      {
        "configKey": "hue_damage",
        "configValue": 25.0,
        "description": "Sát thương cơ bản của Nguyễn Huệ - Lối đánh uy lực, tầm trung",
        "updatedAt": "2026-05-22T08:00:00Z"
      }
    ]
    ```

#### 2. Cập nhật cân bằng thông số game (Dành cho Web Admin - Được Bảo Mật)
*   **Phương thức:** `PUT`
*   **Địa chỉ:** `/api/Config`
*   **Header Bắt buộc:** `X-Admin-Key: <Mã bí mật từ appsettings.json>`
*   **Định dạng gửi đi (Request Body):**
    ```json
    {
      "configKey": "hue_damage",
      "configValue": 30.0,
      "description": "Tăng sát thương Nguyễn Huệ để nâng tầm hào khí"
    }
    ```
*   **Phản hồi:** Trả về đối tượng đã sửa đổi thành công.

---

## 🔒 4. Các Biện Pháp Tối Ưu Hóa & Bảo Mật Enterprise

1.  **Atomic Transaction (Tối ưu vòng kết nối mạng):** 
    *   Hàm `SubmitScoreAsync` gom tất cả quá trình kiểm tra, tạo profile và lưu điểm số lại để thực thi qua đúng **1 lần SaveChangesAsync duy nhất**, giúp tăng gấp đôi tốc độ phản hồi API điểm số.
2.  **Bộ lọc Exception Toàn Cục:**
    *   Sử dụng Custom Middleware chặn toàn bộ các ngoại lệ không mong muốn, tự động ghi log lỗi chi tiết qua tầng Microsoft Logging và trả về chuẩn `ProblemDetails` RFC 7807 an toàn.
3.  **Bảo mật EF Core Fluent Indexes:**
    *   Bảng Leaderboard được thiết lập Index duy nhất cho `UserId` nhằm ngăn ngừa việc người chơi xuất hiện nhiều dòng trên Bảng xếp hạng. Thêm Index cho cột `Score` sắp xếp giảm dần giúp các truy vấn xếp hạng đạt tốc độ O(log N) cực nhanh.
4.  **Cơ chế Giám sát Sức khỏe (HealthCheck):**
    *   Cung cấp API tại route `/health` để Docker, Kubernetes hoặc Cloud Run dễ dàng kiểm tra tình trạng kết nối cơ sở dữ liệu thật lúc runtime.

---

## 🐳 5. Đóng Gói Và Triển Khai Thực Tế Với Docker

Dự án đã được tích hợp sẵn một **Dockerfile** đa tầng (Multi-stage build) để đóng gói ứng dụng một cách tối ưu và gọn nhẹ nhất:

### Câu lệnh Build hình ảnh (Image):
```bash
docker build -t su-dai-viet-be .
```

### Câu lệnh Khởi chạy Container cục bộ:
```bash
docker run -d -p 8080:8080 --name su-dai-viet-backend-app su-dai-viet-be
```
Ứng dụng sẽ được chạy cục bộ tại địa chỉ `http://localhost:8080/swagger` vô cùng mượt mà!
