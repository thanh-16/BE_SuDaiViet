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

## 🛒 7. HỆ THỐNG CỬA HÀNG, KHO ĐỒ, SỐ DƯ & NẠP TIỀN (NEW)

Hệ thống cửa hàng và kinh tế trong game sử dụng mô hình đồng bộ số dư trên Supabase (`profiles`) kết hợp kiểm tra xác thực JWT đầu cuối để chống hack.

### 7.1 Game Client (Dành cho Người chơi)

#### A. Xem Danh sách Vật phẩm đang bán (Không cần xác thực)
*   **Endpoint:** `GET /api/Shop/items`
*   **Định dạng phản hồi:** Mảng danh sách vật phẩm:
    ```json
    [
      {
        "id": "pot_hp_01",
        "name": "Thần Dược Trị Thương",
        "description": "Hồi phục 100% sinh lực tức thì cho nghĩa sĩ Tây Sơn.",
        "priceGold": 100,
        "priceGem": 10,
        "priceVnd": 0,
        "itemType": "Consumable",
        "createdAt": "2026-05-24T17:00:00Z"
      }
    ]
    ```

```javascript
async function getShopItems() {
  try {
    const response = await api.get('/api/Shop/items');
    console.log("Danh sách vật phẩm trong Shop:", response.data);
    return response.data;
  } catch (error) {
    console.error("Lỗi lấy danh mục shop:", error);
  }
}
```

#### B. Xem Kho đồ Cá nhân (Yêu cầu JWT Token)
*   **Endpoint:** `GET /api/Shop/inventory`
*   **Định dạng phản hồi:** Mảng danh sách kho đồ kèm thông tin chi tiết vật phẩm liên kết:
    ```json
    [
      {
        "id": 1,
        "userId": "uuid-nguoi-choi",
        "itemId": "pot_hp_01",
        "quantity": 5,
        "acquiredAt": "2026-05-24T17:15:30Z",
        "itemDetails": {
          "id": "pot_hp_01",
          "name": "Thần Dược Trị Thương",
          "itemType": "Consumable"
          // ...
        }
      }
    ]
    ```

```javascript
async function getMyInventory() {
  const { data: { session } } = await supabase.auth.getSession();
  if (!session) return alert("Vui lòng đăng nhập!");

  try {
    const response = await api.get('/api/Shop/inventory', {
      headers: { 'Authorization': `Bearer ${session.access_token}` }
    });
    console.log("Kho đồ của tôi:", response.data);
    return response.data;
  } catch (error) {
    console.error("Lỗi lấy kho đồ:", error);
  }
}
```

#### C. Mua Vật phẩm bằng Vàng hoặc Ngọc (Yêu cầu JWT Token)
*   **Endpoint:** `POST /api/Shop/buy`
*   **Tham số truyền lên (Body):**
    *   `itemId` (string): Mã vật phẩm (ví dụ: `pot_hp_01`).
    *   `currency` (string): Loại tiền tệ thanh toán, chỉ nhận `"Gold"` hoặc `"Gem"`.
*   **Mô tả:** Back-End thực hiện trừ số dư người chơi và tăng số lượng trong kho đồ dưới một Transaction ACID duy nhất.

```javascript
async function purchaseItem(itemId, currencyType) {
  const { data: { session } } = await supabase.auth.getSession();
  if (!session) return alert("Vui lòng đăng nhập!");

  try {
    const response = await api.post('/api/Shop/buy', 
      { itemId: itemId, currency: currencyType },
      { headers: { 'Authorization': `Bearer ${session.access_token}` } }
    );
    alert(response.data.message); // "Mua vật phẩm thành công!"
    return response.data.transaction;
  } catch (error) {
    alert("Mua đồ thất bại: " + (error.response?.data?.message || "Lỗi kết nối"));
  }
}
```

#### D. Giả lập Nạp tiền Mặt (VND) quy đổi số dư (Yêu cầu JWT Token)
*   **Endpoint:** `POST /api/Shop/topup`
*   **Tỷ lệ quy đổi:** `10,000 VND = 1,000 Vàng & 100 Ngọc` (Quy đổi tự động liên tục theo tỷ lệ `Vàng = VND/10`, `Ngọc = VND/100`).
*   **Tham số truyền lên (Body):**
    *   `amountVnd` (number): Số tiền nạp mặt VND (Tối thiểu 1,000).
    *   `paymentMethod` (string): Cổng thanh toán (ví dụ: `"Momo"`, `"Banking"`, `"Card"`).
    *   `referenceId` (string, optional): Mã giao dịch đối chiếu ngoài.

```javascript
async function simulateTopup(amount, method = "Momo") {
  const { data: { session } } = await supabase.auth.getSession();
  if (!session) return alert("Vui lòng đăng nhập!");

  try {
    const response = await api.post('/api/Shop/topup', 
      {
        amountVnd: amount,
        paymentMethod: method,
        referenceId: `MOMO-${Date.now()}`
      },
      { headers: { 'Authorization': `Bearer ${session.access_token}` } }
    );
    alert(response.data.message); // Hiển thị số dư quy đổi cộng thêm hào hùng
    return response.data.transaction;
  } catch (error) {
    alert("Nạp tiền thất bại: " + (error.response?.data?.message || "Lỗi kết nối"));
  }
}
```

---

### 7.2 Web Admin Dashboard (Dành cho Quản trị viên)

#### A. Xem, Tìm kiếm & Phân trang Lịch sử Giao dịch (Transactions Management)
*   **Endpoint:** `GET /api/Admin/transactions`
*   **Tham số truy vấn (Query Parameters):**
    *   `search`: Tìm kiếm tương đối theo mã giao dịch (`referenceId`), loại giao dịch (`transactionType`), phương thức thanh toán (`paymentMethod`), hoặc tên/email người chơi.
    *   `pageIndex`: Chỉ số trang hiện tại (mặc định `1`).
    *   `pageSize`: Số lượng bản ghi mỗi trang (mặc định `20`).

```javascript
async function fetchTransactionsHistory(keyword = "", page = 1, size = 20) {
  try {
    const response = await adminApi.get('/api/Admin/transactions', {
      params: { search: keyword, pageIndex: page, pageSize: size }
    });
    // Trả về { items: [...], totalItems: 1450, pageIndex: 1, pageSize: 20, totalPages: 73 }
    return response.data;
  } catch (error) {
    console.error("Lỗi lấy lịch sử giao dịch:", error);
  }
}
```

#### B. Cân bằng Giá bán Vật phẩm trong Shop
*   **Endpoint:** `PUT /api/Admin/items/{id}/price`
*   **Tham số truyền lên (Body):**
    *   `priceGold` (number): Giá Vàng mới.
    *   `priceGem` (number): Giá Ngọc mới.
    *   `priceVnd` (number): Giá mua trực tiếp VND mới.
*   **Mô tả:** Thay đổi giá vật phẩm lập tức trong game và tự động ghi nhật ký kiểm toán hành động Admin.

```javascript
async function updateItemShopPrice(itemId, goldPrice, gemPrice, vndPrice) {
  try {
    const response = await adminApi.put(`/api/Admin/items/${itemId}/price`, {
      priceGold: goldPrice,
      priceGem: gemPrice,
      priceVnd: vndPrice
    });
    alert(response.data.message); // "Cập nhật giá vật phẩm thành công!"
    return response.data.item;
  } catch (error) {
    alert("Cân bằng giá thất bại: " + (error.response?.data?.message || "Lỗi hệ thống"));
  }
}
```

#### C. Tăng/Giảm trực tiếp số dư người chơi (Có kiểm soát)
*   **Endpoint:** `PUT /api/Admin/players/{id}/balance`
*   **Tham số truyền lên (Body):**
    *   `goldAmount` (number): Số Vàng cộng thêm (dùng số âm để trừ bớt, ví dụ: `-500`).
    *   `gemAmount` (number): Số Ngọc cộng thêm (dùng số âm để trừ bớt, ví dụ: `-50`).
    *   `reason` (string): Lý do điều chỉnh số dư bắt buộc (phục vụ audit log).

```javascript
async function adjustPlayerCurrency(playerId, goldChange, gemChange, reasonText) {
  try {
    const response = await adminApi.put(`/api/Admin/players/${playerId}/balance`, {
      goldAmount: goldChange,
      gemAmount: gemChange,
      reason: reasonText
    });
    alert(response.data.message); // "Điều chỉnh số dư của người chơi thành công!"
    return response.data.profile;
  } catch (error) {
    alert("Điều chỉnh thất bại: " + (error.response?.data?.message || "Lỗi hệ thống"));
  }
}
```

#### D. Đăng bán vật phẩm mới kèm chỉ số động JSON (Bảo vệ bằng X-Admin-Key)
*   **Endpoint:** `POST /api/Admin/items`
*   **Tham số truyền lên (Body):**
    *   `id` (string): Mã duy nhất của vật phẩm (ví dụ: `sword_hue_02`).
    *   `name` (string): Tên vật phẩm.
    *   `description` (string, optional): Mô tả chi tiết.
    *   `priceGold` (number): Giá mua bằng Vàng.
    *   `priceGem` (number): Giá mua bằng Ngọc.
    *   `priceVnd` (number): Giá mua trực tiếp VND.
    *   `itemType` (string): Loại vật phẩm (`"Equipment"`, `"Consumable"`, `"Skin"`).
    *   `attributes` (string, optional): Chuỗi JSON chứa thuộc tính động. Ví dụ: `{"attack_boost": 25, "level_requirement": 5}`.

```javascript
async function createNewShopItem(itemData) {
  try {
    const response = await adminApi.post('/api/Admin/items', itemData);
    alert(response.data.message); // "Tạo mới vật phẩm thành công!"
    return response.data.item;
  } catch (error) {
    alert("Không thể đăng bán vật phẩm: " + (error.response?.data?.message || "Lỗi hệ thống"));
  }
}
```

#### E. Ngừng bán và xóa vật phẩm khỏi hệ thống (Bảo vệ bằng X-Admin-Key)
*   **Endpoint:** `DELETE /api/Admin/items/{id}`

```javascript
async function deleteShopItem(itemId) {
  try {
    const response = await adminApi.delete(`/api/Admin/items/${itemId}`);
    alert(response.data.message); // "Đã xóa vật phẩm thành công khỏi shop."
    return response.data;
  } catch (error) {
    alert("Không thể xóa vật phẩm: " + (error.response?.data?.message || "Lỗi hệ thống"));
  }
}
```

#### F. Tặng điểm kinh nghiệm (XP) cho người chơi & Tự động thăng cấp (Bảo vệ bằng X-Admin-Key)
*   **Endpoint:** `PUT /api/Admin/players/{id}/xp`
*   **Công thức thăng cấp tự động:** `XP_Yêu_Cầu = Level * 1000`. Khi vượt ngưỡng, người chơi thăng cấp và số dư XP cộng dồn tiếp tục.
*   **Tham số truyền lên (Body):**
    *   `xpAmount` (number): Số lượng XP tặng (phải lớn hơn 0).

```javascript
async function awardPlayerXp(playerId, xpToAdd) {
  try {
    const response = await adminApi.put(`/api/Admin/players/${playerId}/xp`, {
      xpAmount: xpToAdd
    });
    
    const profile = response.data.profile;
    alert(`Đã cộng ${xpToAdd} XP! Người chơi hiện đạt Cấp ${profile.level} (XP: ${profile.experience}).`);
    return profile;
  } catch (error) {
    alert("Thao tác thất bại: " + (error.response?.data?.message || "Lỗi hệ thống"));
  }
}
```

---

## ⚔️ 8. HỆ THỐNG TƯỚNG & KỸ NĂNG (HEROES & SKILLS)

Hệ thống quản lý chỉ số tướng và nâng cấp kỹ năng của nghĩa sĩ Tây Sơn (Nguyễn Huệ, Nguyễn Nhạc, Nguyễn Lữ) được xác thực chặt chẽ qua JWT của Supabase.

### 8.1 Xem danh sách tướng đã mở khóa
*   **Endpoint:** `GET /api/Hero`
*   **Xác thực:** Yêu cầu Supabase JWT trong Header `Authorization: Bearer <token>`
*   **Phản hồi thành công (200 OK):** Mảng danh sách tướng đã sở hữu kèm cấp độ kỹ năng chủ động/bị động:
    ```json
    [
      {
        "id": 1,
        "userId": "uuid-nguoi-choi",
        "heroKey": "hue",
        "level": 1,
        "experience": 0,
        "skillActiveLevel": 1,
        "skillPassiveLevel": 1,
        "unlockedAt": "2026-05-24T17:00:00Z"
      }
    ]
    ```

### 8.2 Mở khóa vị tướng mới (hue, nhac, lu)
*   **Endpoint:** `POST /api/Hero/unlock/{heroKey}`
*   **Tham số đường dẫn:** `heroKey` chỉ chấp nhận một trong ba giá trị `"hue"`, `"nhac"`, hoặc `"lu"`.
*   **Phản hồi mẫu:**
    ```json
    {
      "message": "Đã mở khóa tướng HUE thành công!",
      "hero": {
        "id": 1,
        "heroKey": "hue",
        "skillActiveLevel": 1,
        "skillPassiveLevel": 1
      }
    }
    ```

### 8.3 Nâng cấp kỹ năng tướng
*   **Endpoint:** `POST /api/Hero/upgrade-skill`
*   **Tham số truyền lên (Body):**
    *   `playerHeroId` (number): ID duy nhất của tướng sở hữu.
    *   `skillKey` (string): Loại kỹ năng nâng cấp, nhận `"skill_active"` hoặc `"skill_passive"`.
*   **Cơ chế bảo mật:** Phí nâng cấp kỹ năng (Vàng) tăng dần theo cấp độ kỹ năng hiện tại. Back-End áp dụng cơ chế **Pessimistic Row-Locking (`FOR UPDATE`)** lên dòng hồ sơ người chơi để đảm bảo không bị trừ tiền hai lần hoặc nâng cấp kỹ năng vượt quá giới hạn tiền tệ khi bấm nút nâng cấp liên tục.

```javascript
async function upgradeHeroSkill(heroId, skillType) {
  const { data: { session } } = await supabase.auth.getSession();
  if (!session) return alert("Vui lòng đăng nhập!");

  try {
    const response = await api.post('/api/Hero/upgrade-skill', 
      { playerHeroId: heroId, skillKey: skillType },
      { headers: { 'Authorization': `Bearer ${session.access_token}` } }
    );
    alert(response.data.message); // "Đã nâng cấp kỹ năng thành công!"
    return response.data.hero;
  } catch (error) {
    alert("Nâng cấp thất bại: " + (error.response?.data?.message || "Lỗi hệ thống"));
  }
}
```

---

## 🛡️ 9. HỆ THỐNG TRANG BỊ TƯỚNG (HERO EQUIPMENTS)

Mỗi nghĩa sĩ Tây Sơn sở hữu 3 slot trang bị: `"Weapon"` (Binh khí), `"Armor"` (Giáp trụ), và `"Accessory"` (Trang sức).

### 9.1 Mặc trang bị từ kho đồ vào tướng
*   **Endpoint:** `POST /api/Hero/equip`
*   **Tham số truyền lên (Body):**
    *   `playerHeroId` (number): ID tướng được trang bị.
    *   `inventoryItemId` (number): ID món đồ trong kho đồ cá nhân (`player_inventories.id`).
    *   `slotType` (string): Vị trí mặc, chỉ nhận `"Weapon"`, `"Armor"`, hoặc `"Accessory"`.

> [!WARNING]
> **Ràng buộc Độc quyền ở tầng Database:** Để ngăn chặn lỗi dupe trang bị (1 món đồ mặc đồng thời cho nhiều tướng), Database đã thiết lập một chỉ mục duy nhất:
> `CREATE UNIQUE INDEX unique_equipped_item ON hero_equipments(inventory_item_id);`
> Nếu Front-End cố tình gửi yêu cầu mặc một món đồ đang được mặc trên tướng khác, Back-End sẽ từ chối lập tức và trả về mã lỗi 400 Bad Request.

```javascript
async function equipHeroItem(heroId, invItemId, slotName) {
  const { data: { session } } = await supabase.auth.getSession();
  if (!session) return;

  try {
    const response = await api.post('/api/Hero/equip', 
      { playerHeroId: heroId, inventoryItemId: invItemId, slotType: slotName },
      { headers: { 'Authorization': `Bearer ${session.access_token}` } }
    );
    alert("Đã trang bị thành công!");
    return response.data.equipment;
  } catch (error) {
    alert("Không thể trang bị: " + (error.response?.data?.message || "Lỗi dữ liệu"));
  }
}
```

### 9.2 Tháo trang bị khỏi tướng
*   **Endpoint:** `POST /api/Hero/unequip`
*   **Tham số truyền lên (Body):**
    *   `playerHeroId` (number): ID tướng tháo trang bị.
    *   `slotType` (string): Vị trí tháo (`"Weapon"`, `"Armor"`, `"Accessory"`).

---

## 📬 10. HỆ THỐNG HÒM THƯ CÁ NHÂN & BROADCAST AN TOÀN (MAILBOX)

Hệ thống hòm thư của **Sử Đại Việt** hỗ trợ gửi thư kèm tệp đính kèm (Vàng, Ngọc, Trang bị mẫu). Back-End áp dụng cơ chế đặc thù để loại bỏ 100% rủi ro nhận trùng quà (dupe item).

### 10.1 Xem hòm thư cá nhân
*   **Endpoint:** `GET /api/Mail`
*   **Phản hồi mẫu:**
    ```json
    [
      {
        "id": 12,
        "title": "Quà Vinh Danh Rạch Gầm",
        "content": "Cảm ơn nghĩa sĩ đã tham gia chiến dịch!",
        "goldAttachment": 1000,
        "gemAttachment": 50,
        "itemAttachmentId": "armor_nhac_01",
        "isRead": false,
        "isClaimed": false,
        "isBroadcast": false,
        "createdAt": "2026-05-24T17:00:00Z"
      }
    ]
    ```

### 10.2 Đọc thư (Đánh dấu đã đọc)
*   **Endpoint:** `PUT /api/Mail/{id}/read`

### 10.3 Nhận quà đính kèm an toàn
*   **Endpoint:** `POST /api/Mail/{id}/claim`
*   **Kiến trúc chống Spam click đồng thời (CCU cao):**
    1.  **Thư cá nhân (`isBroadcast = false`):** Hệ thống thực thi lệnh khóa dòng **`SELECT FOR UPDATE`** lên dòng thư trong bảng `mailbox`. Toàn bộ các yêu cầu gửi tiếp theo trong cùng mili-giây sẽ phải xếp hàng đợi. Khi yêu cầu đầu tiên hoàn thành và đánh dấu `is_claimed = true`, các yêu cầu sau sẽ bị từ chối ngay lập tức.
    2.  **Thư Broadcast (`isBroadcast = true` - Gửi quà toàn server):** Để tránh deadlock khi hàng vạn người chơi cùng nhận một thư chung, Back-End sử dụng chèn nguyên tử:
        ```sql
        INSERT INTO public.player_broadcast_claims (user_id, mail_id, is_read, is_claimed, claimed_at)
        VALUES (:uid, :mid, true, true, now())
        ON CONFLICT (user_id, mail_id) DO NOTHING
        ```
        Chỉ khi dòng dữ liệu được chèn thành công (số dòng bị ảnh hưởng > 0), quà tặng mới được trao cho ví và kho đồ của người chơi.

---

## 🏪 11. HỆ THỐNG CHỢ GIAO DỊCH VẬT PHẨM P2P (MARKETPLACE)

Hệ thống Chợ P2P cho phép người chơi trao đổi, mua bán các vật phẩm trang bị dư thừa trong rương đồ lấy Vàng hoặc Ngọc.

### 11.1 Xem danh sách chợ đang hoạt động (Công khai, phân trang)
*   **Endpoint:** `GET /api/Marketplace/listings`
*   **Tham số truy vấn (Query Params):** `search` (tên món đồ hoặc người bán), `pageIndex`, `pageSize`.
*   **Phản hồi mẫu:**
    ```json
    {
      "items": [
        {
          "id": 5,
          "sellerId": "uuid-nguoi-ban",
          "priceGold": 1500,
          "priceGem": 10,
          "listingFee": 10,
          "status": "Active",
          "inventoryItem": {
            "itemDetails": {
              "name": "Long Lân Giáp",
              "itemType": "Equipment"
            }
          },
          "createdAt": "2026-05-24T17:10:00Z"
        }
      ],
      "totalItems": 1
    }
    ```

### 11.2 Đăng bán vật phẩm từ rương đồ lên chợ
*   **Endpoint:** `POST /api/Marketplace/list`
*   **Tham số truyền lên (Body):**
    *   `inventoryItemId` (number): ID duy nhất của vật phẩm trong rương của bạn.
    *   `priceGold` (number): Giá bán bằng Vàng.
    *   `priceGem` (number): Giá bán bằng Ngọc.
*   **Cơ chế ràng buộc:**
    *   Món đồ đang mặc trên người tướng không thể đăng bán (Front-End cần nhắc người chơi tháo giáp/vũ khí trước khi niêm yết).
    *   Hệ thống tự động trừ phí niêm yết tượng trưng 10 Vàng từ ví người bán để phòng chống spam đăng bài rác.

### 11.3 Mua vật phẩm từ chợ (Xử lý Race-Condition & Double Spending)
*   **Endpoint:** `POST /api/Marketplace/buy/{listingId}`
*   **Luồng xử lý nguyên tử của Back-End:**
    1.  Khóa bài đăng trên chợ: `SELECT 1 FROM public.marketplace_listings WHERE id = :id FOR UPDATE;`
    2.  Tải lại trạng thái mới nhất từ DB và kiểm tra nếu bài đăng đã đổi sang `"Sold"` hoặc `"Cancelled"` thì hủy giao dịch lập tức. (Ngăn chặn 2 người cùng mua 1 bài đăng đồng thời).
    3.  Khóa số dư ví của cả người mua và người bán theo thứ tự UUID tăng dần để triệt tiêu deadlock hoàn toàn.
    4.  Trừ số dư người mua ➔ Khấu trừ thuế giao dịch 5% ➔ Cộng số dư thực nhận vào ví người bán ➔ Chuyển quyền sở hữu vật phẩm sang túi người mua (UPSERT) ➔ Cập nhật trạng thái bài đăng sang `"Sold"` đồng thời ghi nhận **`buyer_id`** và thời gian **`updated_at`** để kiểm toán đầy đủ.
    5.  Nhật ký tài chính (`Transaction`) được chèn ở trạng thái mặc định **`Pending`** trước khi kết chuyển thành công để đảm bảo quy trình kiểm soát nhà nước an toàn.

```javascript
async function buyMarketplaceListing(listingId) {
  const { data: { session } } = await supabase.auth.getSession();
  if (!session) return alert("Vui lòng đăng nhập!");

  try {
    const response = await api.post(`/api/Marketplace/buy/${listingId}`, {}, {
      headers: { 'Authorization': `Bearer ${session.access_token}` }
    });
    
    // Phản hồi chứa Transaction ghi nhận chi tiết:
    alert("Giao dịch mua vật phẩm trên chợ thành công!");
    return response.data.transaction;
  } catch (error) {
    alert("Giao dịch thất bại: " + (error.response?.data?.message || "Lỗi hệ thống"));
  }
}
```

### 11.4 Hủy bài đăng bán trên chợ
*   **Endpoint:** `POST /api/Marketplace/cancel/{listingId}`
*   **Mô tả:** Bài đăng chuyển sang trạng thái `"Cancelled"`, vật phẩm được tự động hoàn trả lại rương đồ của người bán.

### 11.5 Đặt chỗ giữ hàng trên chợ (New)
*   **Endpoint:** `POST /api/Marketplace/reserve/{listingId}`
*   **Xác thực:** Yêu cầu Supabase JWT trong Header `Authorization: Bearer <token>`
*   **Mô tả:** Đặt chỗ giữ vật phẩm trong **5 phút** để tránh việc người khác mua mất trong khi đang đàm phán hoặc thanh toán. Trạng thái tin rao bán sẽ chuyển từ `"Active"` sang `"Reserved"`.
*   *Lưu ý:* Hệ thống áp dụng cơ chế tự động dọn dẹp lười biếng (lazy dynamic cleanup). Nếu quá 5 phút mà giao dịch mua chưa hoàn tất, đặt chỗ sẽ tự động bị hủy và đưa tin rao bán quay lại trạng thái `"Active"` ở lần giao dịch tiếp theo.

```javascript
async function reserveListing(listingId) {
  const { data: { session } } = await supabase.auth.getSession();
  if (!session) return alert("Vui lòng đăng nhập!");

  try {
    const response = await api.post(`/api/Marketplace/reserve/${listingId}`, {}, {
      headers: { 'Authorization': `Bearer ${session.access_token}` }
    });
    alert("Đã đặt chỗ giữ hàng thành công trong vòng 5 phút!");
    return response.data.reservation;
  } catch (error) {
    alert("Đặt chỗ thất bại: " + (error.response?.data?.message || "Lỗi hệ thống"));
  }
}
```

### 11.6 Giải phóng đặt chỗ giữ hàng (New)
*   **Endpoint:** `POST /api/Marketplace/release/{listingId}`
*   **Xác thực:** Yêu cầu Supabase JWT trong Header `Authorization: Bearer <token>`
*   **Mô tả:** Chủ động hủy đặt chỗ để trả tin rao bán về trạng thái `"Active"` cho người khác mua.

```javascript
async function releaseListingReservation(listingId) {
  const { data: { session } } = await supabase.auth.getSession();
  if (!session) return;

  try {
    await api.post(`/api/Marketplace/release/${listingId}`, {}, {
      headers: { 'Authorization': `Bearer ${session.access_token}` }
    });
    alert("Đã giải phóng đặt chỗ thành công!");
  } catch (error) {
    console.error("Giải phóng đặt chỗ thất bại:", error);
  }
}
```

---
*Tài liệu được thiết kế đồng bộ và bảo mật tuyệt đối cho hệ sinh thái Sử Đại Việt. Chúc nghĩa sĩ tích hợp thành công mỹ mãn!*

