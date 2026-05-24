# HƯỚNG DẪN TÍCH HỢP FRONT-END ⇄ BACK-END (SỬ ĐẠI VIỆT)
*Tài liệu hướng dẫn chi tiết dành cho lập trình viên Front-End (Game Client & Web Admin Dashboard)*

---

## 📖 1. TỔNG QUAN KIẾN TRÚC GIAO TIẾP

Hệ thống Back-End **Sử Đại Việt** được xây dựng trên nền tảng **ASP.NET Core 8.0** kết hợp **Supabase (PostgreSQL & Auth)**. Để bảo vệ tài nguyên máy chủ trước tải lớn (10k concurrent users) và ngăn ngừa gian lận điểm số, hệ thống áp dụng các lớp bảo mật nghiêm ngặt sau:

1.  **CORS động:** Chỉ cho phép các địa chỉ Front-End (Origins) được cấu hình trước truy cập vào tài nguyên hệ thống.
2.  **Xác thực qua Supabase JWT:** Sử dụng mã Token JWT của Supabase phát hành (khi đăng nhập qua Email, OTP hoặc Mạng xã hội như Google, Facebook) để xác thực người chơi gửi điểm số.
3.  **Xác thực qua Admin Key:** Sử dụng mã khóa riêng tư truyền trong Header `X-Admin-Key` đối với toàn bộ các tác vụ của Web Admin.
4.  **Bộ lọc Tần suất (Rate Limiting Middleware):** Giới hạn số lượng yêu cầu của từng người chơi/IP để chống Spam và Brute-Force.

---

## ⚙️ 2. CẤU HÌNH MÔI TRƯỜNG & CORS

### 2.1 Cấu hình phía Front-End
Khai báo địa chỉ máy chủ Back-End trong cấu hình môi trường của bạn (ví dụ: `.env` hoặc `.env.local`):

```env
# Địa chỉ URL máy chủ Back-End (Cloud Production)
VITE_API_BASE_URL=https://be-sudaiviet.onrender.com
```

### 2.2 Cấu hình phía Back-End (CORS Allowed Origins)
Đảm bảo rằng cổng chạy dự án Front-End của bạn (ví dụ: React Vite chạy cổng `5173`) đã được khai báo trong file `appsettings.json` của Back-End:

```json
"CorsSettings": {
  "AllowedOrigins": [
    "http://localhost:5173",
    "http://localhost:3000",
    "https://admin.sudaiviet.com"
  ]
}
```

---

## 🔒 3. PHƯƠNG THỨC XÁC THỰC (AUTHENTICATION)

### 3.1 Luồng gửi điểm của Người chơi (Supabase JWT)
*   **Mô tả:** Khi người chơi đăng nhập thành công ở Front-End bằng Supabase Client, thư viện Supabase sẽ lưu trữ một Session.
*   **Cách lấy Token:** Lấy trường `access_token` từ session hiện tại.
*   **Header đính kèm:** `Authorization: Bearer <mã_access_token>`

> [!IMPORTANT]
> **Quy tắc Bảo mật:** Front-End **KHÔNG** tự ý gửi `UserId` hay `Username` lên trong request body gửi điểm số. Back-End sẽ tự động giải mã cấu trúc mã hóa JWT để trích xuất `UserId` (trường `sub`) và đồng bộ thông tin nhằm chống giả mạo điểm.

### 3.2 Luồng tác vụ của Web Admin (X-Admin-Key)
*   **Mô tả:** Toàn bộ API quản trị hệ thống yêu cầu đính kèm mã bảo mật nội bộ trong Header.
*   **Header đính kèm:** `X-Admin-Key: sudaivietfptu` (Giá trị này phải khớp với cấu hình `AdminSettings:AdminKey` trên máy chủ BE).

---

## 🔌 4. CHI TIẾT TÍCH HỢP API & ĐOẠN MÃ MẪU (CODE EXAMPLES)

Dưới đây là mã mẫu chi tiết viết bằng **JavaScript/TypeScript** sử dụng thư viện **Axios** (hoặc `fetch` tích hợp sẵn).

### 4.1 Tích hợp cho Game Client (Dành cho Người chơi)

#### A. Lấy danh sách cấu hình chỉ số nhân vật (Không cần xác thực)
*   **Endpoint:** `GET /api/Config`
*   **Chính sách Rate Limit:** `AdminApiPolicy` (Giới hạn theo IP).

```javascript
import axios from 'axios';

const api = axios.create({
  baseURL: 'https://be-sudaiviet.onrender.com'
});

async function getHeroAttributes() {
  try {
    const response = await api.get('/api/Config');
    console.log("Danh sách chỉ số 3 anh em Tây Sơn:", response.data);
    return response.data;
  } catch (error) {
    console.error("Không thể tải chỉ số game từ xa:", error);
  }
}
```

#### B. Xem Top 10 Bảng Xếp Hạng công khai (Không cần xác thực)
*   **Endpoint:** `GET /api/Leaderboard`

```javascript
async function getTopLeaderboard() {
  try {
    const response = await api.get('/api/Leaderboard');
    console.log("Top vinh danh nghĩa sĩ:", response.data);
    return response.data; // Trả về mảng danh sách [{ username, score, stageReached }]
  } catch (error) {
    console.error("Không thể tải bảng xếp hạng:", error);
  }
}
```

#### C. Gửi điểm số lập chiến công (Yêu cầu JWT Token & Chặn tài khoản bị Ban)
*   **Endpoint:** `POST /api/Leaderboard`
*   **Chính sách Rate Limit:** `ScoreSubmitPolicy` (Tối đa **15 lượt gửi điểm / 1 phút / 1 tài khoản**).

```javascript
import { createClient } from '@supabase/supabase-js';

const supabase = createClient('SUPABASE_URL', 'SUPABASE_ANON_KEY');

async function submitHighscore(scoreValue, stageReachedName) {
  // 1. Lấy thông tin phiên đăng nhập hoạt động từ Supabase
  const { data: { session } } = await supabase.auth.getSession();
  if (!session) {
    alert("Nghĩa sĩ vui lòng đăng nhập để được ghi nhận công lao vào Bảng Xếp Hạng!");
    return;
  }

  const token = session.access_token;

  // 2. Gửi điểm số kèm JWT Token lên Back-End
  try {
    const response = await api.post('/api/Leaderboard', 
      {
        score: scoreValue,
        stageReached: stageReachedName
      },
      {
        headers: {
          'Authorization': `Bearer ${token}` // Truyền Token dạng Bearer
        }
      }
    );

    alert("Chiến công của nghĩa sĩ đã được ghi vào Bảng Vàng!");
    return response.data;
  } catch (error) {
    if (error.response) {
      const status = error.response.status;
      if (status === 429) {
        // Lỗi gửi điểm quá nhanh (Rate Limited)
        alert(error.response.data.message); 
      } else if (status === 403) {
        // Tài khoản đã bị khóa (Banned)
        alert("Tài khoản của bạn đã bị tước quyền ghi danh bảng vàng do phát hiện gian lận!");
      } else if (status === 401) {
        alert("Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại!");
      } else {
        alert("Không thể ghi danh: " + (error.response.data.title || "Có lỗi xảy ra"));
      }
    } else {
      alert("Lỗi kết nối đến máy chủ vinh danh!");
    }
  }
}
```

---

### 4.2 Tích hợp cho Web Admin Dashboard (Dành cho Quản trị viên)

Đối với các API quản trị, Front-End cần gửi kèm Header xác thực và thực hiện phân trang, tìm kiếm.

#### A. Cấu hình Axios Instance chuyên biệt cho Admin
Để tránh phải viết đi viết lại Header xác thực, hãy khởi tạo một thực thể Axios dùng riêng cho Dashboard:

```javascript
const adminApi = axios.create({
  baseURL: 'https://be-sudaiviet.onrender.com',
  headers: {
    'Content-Type': 'application/json',
    'X-Admin-Key': 'sudaivietfptu' // Mã khóa bảo mật Admin
  }
});
```

#### B. Xem, Tìm kiếm & Phân trang người chơi (Players Management)
*   **Endpoint:** `GET /api/Admin/players`
*   **Tham số truy vấn (Query Parameters):**
    *   `search`: Từ khóa tìm kiếm (Tên, Email hoặc Số điện thoại).
    *   `isBanned`: Bộ lọc theo trạng thái khóa (`true` / `false` / bỏ trống để xem tất cả).
    *   `pageIndex`: Chỉ số trang hiện tại (bắt đầu từ `1`).
    *   `pageSize`: Số lượng người chơi trên mỗi trang (mặc định `10`).

```javascript
async function fetchPlayersList(keyword = "", statusFilter = null, page = 1, size = 10) {
  try {
    const response = await adminApi.get('/api/Admin/players', {
      params: {
        search: keyword,
        isBanned: statusFilter,
        pageIndex: page,
        pageSize: size
      }
    });
    
    // Dữ liệu trả về chuẩn phân trang:
    // { items: [...], totalItems: 120, pageIndex: 1, pageSize: 10, totalPages: 12 }
    return response.data;
  } catch (error) {
    console.error("Lỗi lấy danh sách người chơi:", error);
  }
}
```

#### C. Khóa/Mở khóa tài khoản (Ban/Unban) & Tự động dọn dẹp điểm số
*   **Endpoint:** `PUT /api/Admin/players/{id}/ban`
*   **Mô tả:** Khi tài khoản bị khóa (`isBanned: true`), Back-End sẽ tự động xóa sạch điểm số của người này khỏi bảng xếp hạng vinh danh và ghi Audit Log.

```javascript
async function toggleBanStatus(playerId, setBanned, reasonText) {
  try {
    const response = await adminApi.put(`/api/Admin/players/${playerId}/ban`, {
      isBanned: setBanned,
      reason: reasonText || "Vi phạm điều khoản game Sử Đại Việt"
    });
    
    alert(setBanned ? "Đã khóa vĩnh viễn tài khoản người chơi!" : "Đã mở khóa tài khoản!");
    return response.data;
  } catch (error) {
    alert("Thao tác thất bại: " + error.response?.data?.title);
  }
}
```

#### D. Thay đổi vai trò người chơi (Thăng quyền Admin / Hạ cấp)
*   **Endpoint:** `PUT /api/Admin/players/{id}/role`

```javascript
async function changePlayerRole(playerId, targetRole) {
  // targetRole chỉ chấp nhận 'admin' hoặc 'player'
  try {
    const response = await adminApi.put(`/api/Admin/players/${playerId}/role`, {
      role: targetRole
    });
    
    alert(`Đã chuyển đổi vai trò thành công sang: ${targetRole.toUpperCase()}`);
    return response.data;
  } catch (error) {
    alert("Thao tác thất bại: " + error.response?.data?.title);
  }
}
```

#### E. Xem lịch sử tác vụ kiểm toán của Admin (Audit Logs)
*   **Endpoint:** `GET /api/Admin/logs`
*   **Mô tả:** Truy xuất nhật ký kiểm toán ghi lại mọi hoạt động chỉnh sửa thông số game, ban/unban, thăng quyền...

```javascript
async function getAuditLogs(keyword = "", page = 1, size = 20) {
  try {
    const response = await adminApi.get('/api/Admin/logs', {
      params: {
        search: keyword,
        pageIndex: page,
        pageSize: size
      }
    });
    return response.data; // { items: [...], totalItems: 250, ... }
  } catch (error) {
    console.error("Không thể tải nhật ký audit:", error);
  }
}
```

#### F. Thêm/Cập nhật chỉ số Game từ xa (Remote Config)
*   **Endpoint:** `PUT /api/Config`
*   **Mô tả:** Thay đổi hoặc tạo mới chỉ số cân bằng tướng (Nguyễn Huệ, Nguyễn Nhạc, Nguyễn Lữ).

```javascript
async function saveHeroConfig(configKey, configValue, description) {
  try {
    const response = await adminApi.put('/api/Config', {
      configKey: configKey,
      configValue: configValue,
      description: description
    });
    alert("Đã cập nhật cấu hình tướng từ xa thành công!");
    return response.data;
  } catch (error) {
    alert("Lỗi cấu hình: " + error.response?.data?.title);
  }
}
```

---

## 📈 5. XỬ LÝ LỖI TRỰC QUAN TRÊN GIAO DIỆN (UI/UX ERROR HANDLING)

Hệ thống Back-End đã tích hợp **Global Exception Middleware** và định dạng phản hồi lỗi chuẩn **Problem Details (RFC 7807)**. Cấu trúc phản hồi lỗi khi gặp sự cố hoặc validation thất bại luôn như sau:

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.6.1",
  "title": "Một lỗi nghiêm trọng đã xảy ra trong hệ thống.",
  "status": 500,
  "detail": "Chi tiết kỹ thuật về lỗi phát sinh (chỉ hiện ở môi trường Development để bảo mật)...",
  "instance": "/api/Leaderboard"
}
```

### 5.1 Quy chuẩn hiển thị lỗi trên Front-End
Lập trình viên Front-End cần sử dụng cấu trúc `try...catch` và bóc tách các trường thông tin lỗi như sau để hiển thị thông báo trực quan cho người dùng:

```javascript
try {
  // Gọi API...
} catch (error) {
  if (error.response) {
    // 1. Nhận phản hồi lỗi từ máy chủ
    const { status, data } = error.response;
    
    switch (status) {
      case 400:
        // Lỗi Validation dữ liệu đầu vào (DataAnnotations)
        const validationErrors = data.errors 
          ? Object.values(data.errors).flat().join("\n") 
          : "Dữ liệu gửi đi không hợp lệ!";
        showToast("Lỗi nhập liệu", validationErrors, "error");
        break;
        
      case 429:
        // Lỗi Rate Limit (Chặn tần suất gửi quá nhanh)
        // Hiển thị trực tiếp thông báo thuần Việt hào hùng được thiết lập từ BE:
        showToast("Tác vụ quá nhanh", data.message, "warning");
        break;
        
      case 403:
        showToast("Bị từ chối", "Tài khoản không có quyền thực hiện tác vụ này!", "error");
        break;
        
      case 401:
        showToast("Hết phiên", "Vui lòng đăng nhập lại để tiếp tục!", "info");
        break;
        
      default:
        // Các lỗi hệ thống khác (500, 503...)
        showToast("Lỗi hệ thống", data.title || "Vui lòng thử lại sau giây lát!", "error");
    }
  } else {
    // 2. Không kết nối được đến máy chủ (Network Error)
    showToast("Mất kết nối", "Không thể kết nối đến máy chủ Sử Đại Việt. Vui lòng kiểm tra mạng!", "error");
  }
}
```

---

## 🚀 6. PHƯƠNG ÁN PHÒNG NGỪA RATE LIMITING TỐI ƯU TRÊN UI

Để mang lại trải nghiệm người dùng mượt mà nhất và tránh kích hoạt nhầm cơ chế chặn bảo mật **HTTP 429** trên máy chủ:

1.  **Vô hiệu hóa nút bấm (Disable Button):** Khi người dùng nhấn nút "Gửi điểm" hoặc "Cập nhật cấu hình", ngay lập tức thiết lập nút bấm sang trạng thái `disabled` kèm biểu tượng loading. Chỉ kích hoạt lại nút bấm sau khi nhận được phản hồi thành công hoặc thất bại từ API.
2.  **Debounce/Throttle:** Sử dụng kỹ thuật Debounce hoặc Throttle đối với các ô tìm kiếm người chơi/audit logs trong Web Admin để tránh việc mỗi ký tự gõ phím đều kích hoạt một API request lên máy chủ. (Tối ưu nhất là gọi API sau khi người dùng ngừng gõ phím 500ms).

---
*Tài liệu được thiết kế đồng bộ và bảo mật tuyệt đối cho hệ sinh thái Sử Đại Việt. Chúc nghĩa sĩ tích hợp thành công mỹ mãn!*
