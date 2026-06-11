using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sử_Đại_Việt.Data;
using Sử_Đại_Việt.Models;
using Sử_Đại_Việt.Services;
using Sử_Đại_Việt.Dtos;

using Microsoft.AspNetCore.RateLimiting;

namespace Sử_Đại_Việt.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("AdminApiPolicy")]
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IAdminLogService _adminLogService;
        private readonly IShopService _shopService;
        private readonly IMailService _mailService;

        public AdminController(ApplicationDbContext context, IConfiguration configuration, IAdminLogService adminLogService, IShopService shopService, IMailService mailService)
        {
            _context = context;
            _configuration = configuration;
            _adminLogService = adminLogService;
            _shopService = shopService;
            _mailService = mailService;
        }

        // Helper check X-Admin-Key
        private bool IsAuthorizedAdmin()
        {
            return Request.Headers.TryGetValue("X-Admin-Key", out var extractedKey) &&
                   extractedKey == _configuration["AdminSettings:AdminKey"];
        }

        /// <summary>
        /// Xem danh sách tất cả hồ sơ người chơi (Hỗ trợ phân trang, tìm kiếm theo Tên/Email/Phone, lọc theo trạng thái bị khóa).
        /// </summary>
        [HttpGet("players")]
        public async Task<ActionResult<PaginatedResult<Profile>>> GetPlayers(
            [FromQuery] string? search = null,
            [FromQuery] bool? isBanned = null,
            [FromQuery] int pageIndex = 1,
            [FromQuery] int pageSize = 10)
        {
            if (!IsAuthorizedAdmin())
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            if (pageIndex < 1) pageIndex = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100;

            var query = _context.Profiles.AsNoTracking().AsQueryable();

            // Tìm kiếm tương đối theo DisplayName, Email hoặc Phone
            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(p => 
                    (p.DisplayName != null && p.DisplayName.ToLower().Contains(lowerSearch)) ||
                    (p.Email != null && p.Email.ToLower().Contains(lowerSearch)) ||
                    (p.Phone != null && p.Phone.Contains(lowerSearch))
                );
            }

            // Lọc theo trạng thái bị khóa
            if (isBanned.HasValue)
            {
                query = query.Where(p => p.IsBanned == isBanned.Value);
            }

            var totalItems = await query.CountAsync();
            var items = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var result = new PaginatedResult<Profile>
            {
                Items = items,
                TotalItems = totalItems,
                PageIndex = pageIndex,
                PageSize = pageSize
            };

            return Ok(result);
        }

        /// <summary>
        /// Khóa hoặc mở khóa tài khoản người chơi (Bảo vệ bằng X-Admin-Key).
        /// </summary>
        [HttpPut("players/{id}/ban")]
        public async Task<IActionResult> ToggleBanPlayer(Guid id, [FromBody] BanPlayerDto model)
        {
            if (!IsAuthorizedAdmin())
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            var player = await _context.Profiles.FindAsync(id);
            if (player == null)
            {
                return NotFound($"Không tìm thấy tài khoản người chơi có ID = {id}.");
            }

            if (player.IsBanned == model.IsBanned)
            {
                return BadRequest($"Tài khoản người chơi đã ở trạng thái {(player.IsBanned ? "bị khóa" : "bình thường")} rồi.");
            }

            player.IsBanned = model.IsBanned;
            
            // Nếu người chơi bị ban, chúng ta có thể xóa điểm số của họ trên bảng xếp hạng leaderboard để đảm bảo vinh danh trong sạch
            if (player.IsBanned)
            {
                var scoreRecord = await _context.Leaderboards.FirstOrDefaultAsync(l => l.UserId == id);
                if (scoreRecord != null)
                {
                    _context.Leaderboards.Remove(scoreRecord);
                }
            }

            // Lưu các thay đổi về Profiles/Leaderboard trước
            await _context.SaveChangesAsync();

            // Ghi nhật ký hoạt động thông qua Service chuyên biệt
            await _adminLogService.LogActionAsync(
                "Admin_System",
                model.IsBanned ? "Khóa tài khoản" : "Mở khóa tài khoản",
                $"Đã {(model.IsBanned ? "khóa" : "mở khóa")} người chơi '{player.DisplayName}' (Id = {player.Id}, Email = {player.Email}). Lý do: {model.Reason ?? "Không có lý do cụ thể."}"
            );

            return Ok(new { message = $"Đã {(model.IsBanned ? "khóa" : "mở khóa")} thành công tài khoản của người chơi '{player.DisplayName}'." });
        }

        /// <summary>
        /// Thay đổi vai trò người chơi (Thăng quyền Admin hoặc Hạ xuống Player).
        /// </summary>
        [HttpPut("players/{id}/role")]
        public async Task<IActionResult> ChangePlayerRole(Guid id, [FromBody] ChangeRoleDto model)
        {
            if (!IsAuthorizedAdmin())
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            var player = await _context.Profiles.FindAsync(id);
            if (player == null)
            {
                return NotFound($"Không tìm thấy tài khoản người chơi có ID = {id}.");
            }

            var normalizedRole = model.Role.ToLower().Trim();
            if (normalizedRole != "player" && normalizedRole != "admin")
            {
                return BadRequest("Vai trò không hợp lệ. Chỉ chấp nhận vai trò 'player' hoặc 'admin'.");
            }

            if (player.Role == normalizedRole)
            {
                return BadRequest($"Tài khoản người chơi đã có vai trò '{player.Role}' rồi.");
            }

            var oldRole = player.Role;
            player.Role = normalizedRole;

            // Lưu thay đổi vai trò người dùng trước
            await _context.SaveChangesAsync();

            // Ghi nhật ký hoạt động thông qua Service chuyên biệt
            await _adminLogService.LogActionAsync(
                "Admin_System",
                "Thay đổi vai trò",
                $"Đã đổi vai trò của '{player.DisplayName}' (Id = {player.Id}) từ '{oldRole}' sang '{player.Role}'."
            );

            return Ok(new { message = $"Đã thăng/hạ vai trò thành công của người chơi '{player.DisplayName}' sang '{player.Role}'." });
        }

        /// <summary>
        /// Xem lịch sử nhật ký kiểm toán hành động quản trị của Admin (Bảo vệ bằng X-Admin-Key).
        /// </summary>
        [HttpGet("logs")]
        public async Task<ActionResult<PaginatedResult<AdminLog>>> GetAdminLogs(
            [FromQuery] string? search = null,
            [FromQuery] int pageIndex = 1,
            [FromQuery] int pageSize = 20)
        {
            if (!IsAuthorizedAdmin())
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            if (pageIndex < 1) pageIndex = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var totalItems = await _adminLogService.GetLogsCountAsync(search);
            var items = await _adminLogService.GetLogsAsync(search, pageIndex, pageSize);

            var result = new PaginatedResult<AdminLog>
            {
                Items = items,
                TotalItems = totalItems,
                PageIndex = pageIndex,
                PageSize = pageSize
            };

            return Ok(result);
        }

        /// <summary>
        /// Xem lịch sử giao dịch và nạp tiền của toàn hệ thống (Hỗ trợ phân trang và tìm kiếm theo loại giao dịch, mã giao dịch, tên người chơi).
        /// </summary>
        [HttpGet("transactions")]
        public async Task<ActionResult<PaginatedResult<Transaction>>> GetTransactions(
            [FromQuery] string? search = null,
            [FromQuery] int pageIndex = 1,
            [FromQuery] int pageSize = 20)
        {
            if (!IsAuthorizedAdmin())
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            if (pageIndex < 1) pageIndex = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var totalItems = await _shopService.GetTransactionsCountAsync(search);
            var items = await _shopService.GetTransactionsAsync(search, pageIndex, pageSize);

            var result = new PaginatedResult<Transaction>
            {
                Items = items,
                TotalItems = totalItems,
                PageIndex = pageIndex,
                PageSize = pageSize
            };

            return Ok(result);
        }

        /// <summary>
        /// Điều chỉnh giá của một vật phẩm trong shop (Vàng, Ngọc, VNĐ).
        /// </summary>
        [HttpPut("items/{id}/price")]
        public async Task<IActionResult> UpdateItemPrice(string id, [FromBody] UpdateItemPriceDto model)
        {
            if (!IsAuthorizedAdmin())
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var item = await _shopService.UpdateItemPriceAsync(id, model.PriceGold, model.PriceGem, model.PriceVnd, "Admin_System");
                return Ok(new { message = $"Cập nhật giá vật phẩm '{item.Name}' thành công!", item = item });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi cập nhật giá vật phẩm.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Tăng hoặc giảm trực tiếp số dư Vàng/Ngọc của người chơi (Bảo vệ bằng X-Admin-Key).
        /// </summary>
        [HttpPut("players/{id}/balance")]
        public async Task<IActionResult> AdjustPlayerBalance(Guid id, [FromBody] AdjustBalanceDto model)
        {
            if (!IsAuthorizedAdmin())
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var profile = await _shopService.AdjustPlayerBalanceAsync(id, model.GoldAmount, model.GemAmount, model.Reason, "Admin_System");
                return Ok(new
                {
                    message = $"Điều chỉnh số dư của người chơi '{profile.DisplayName}' thành công!",
                    profile = profile
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi điều chỉnh số dư người chơi.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Tạo mới vật phẩm bán trong shop (Bảo vệ bằng X-Admin-Key).
        /// </summary>
        [HttpPost("items")]
        public async Task<IActionResult> CreateShopItem([FromBody] CreateGameItemDto model)
        {
            if (!IsAuthorizedAdmin())
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var item = new GameItem
                {
                    Id = model.Id,
                    Name = model.Name,
                    Description = model.Description,
                    PriceGold = model.PriceGold,
                    PriceGem = model.PriceGem,
                    PriceVnd = model.PriceVnd,
                    ItemType = model.ItemType,
                    Attributes = model.Attributes
                };

                var created = await _shopService.CreateShopItemAsync(item, "Admin_System");
                return Ok(new { message = "Tạo mới vật phẩm thành công!", item = created });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi tạo mới vật phẩm.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Xóa bỏ vật phẩm khỏi shop game (Bảo vệ bằng X-Admin-Key).
        /// </summary>
        [HttpDelete("items/{id}")]
        public async Task<IActionResult> DeleteShopItem(string id)
        {
            if (!IsAuthorizedAdmin())
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            try
            {
                await _shopService.DeleteShopItemAsync(id, "Admin_System");
                return Ok(new { message = $"Đã xóa vật phẩm '{id}' thành công khỏi shop." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi xóa vật phẩm.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Cộng điểm kinh nghiệm (XP) cho người chơi và tự động thăng cấp (Bảo vệ bằng X-Admin-Key).
        /// </summary>
        [HttpPut("players/{id}/xp")]
        public async Task<IActionResult> AwardPlayerXp(Guid id, [FromBody] AwardXpDto model)
        {
            if (!IsAuthorizedAdmin())
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var profile = await _shopService.AwardPlayerXpAsync(id, model.XpAmount, "Admin_System");
                return Ok(new
                {
                    message = $"Cộng {model.XpAmount} XP thành công!",
                    profile = profile
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi cộng XP cho người chơi.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Gửi hòm thư đền bù/quà tặng cá nhân hoặc phát sóng toàn server (Bảo vệ bằng X-Admin-Key).
        /// </summary>
        [HttpPost("mail")]
        public async Task<IActionResult> SendMail([FromBody] CreateMailDto model)
        {
            if (!IsAuthorizedAdmin())
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var mail = new MailboxItem
                {
                    ReceiverId = model.ReceiverId,
                    Title = model.Title,
                    Content = model.Content,
                    Attachments = model.AttachmentsJson,
                    ExpiredAt = DateTime.UtcNow.AddDays(model.DurationDays)
                };

                var created = await _mailService.SendMailAsync(mail, "Admin_System");
                return Ok(new { message = "Gửi hòm thư thành công!", mail = created });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi gửi thư từ Admin.", detail = ex.Message });
            }
        }
    }

    public class PaginatedResult<T>
    {
        public IEnumerable<T> Items { get; set; } = Array.Empty<T>();
        public int TotalItems { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);
    }

    public class BanPlayerDto
    {
        public bool IsBanned { get; set; }
        public string? Reason { get; set; }
    }

    public class ChangeRoleDto
    {
        public string Role { get; set; } = "player";
    }
}
