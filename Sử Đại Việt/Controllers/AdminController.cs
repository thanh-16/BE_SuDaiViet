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

        /// <summary>
        /// Seed dữ liệu mẫu thật của Bảng xếp hạng và Nhật ký chiến đấu trực tiếp vào Database Supabase PostgreSQL.
        /// (Bảo vệ bằng X-Admin-Key).
        /// </summary>
        [HttpPost("seed-database")]
        public async Task<IActionResult> SeedDatabase()
        {
            if (!IsAuthorizedAdmin())
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            try
            {
                var random = new Random();

                // 1. Kiểm tra danh sách profiles, nếu trống thì tự tạo sẵn các anh kiệt để seed
                var profiles = await _context.Profiles.ToListAsync();
                if (profiles == null || profiles.Count < 5)
                {
                    // Tạo sẵn danh sách các vị anh kiệt
                    var heroProfiles = new List<(string Name, string Email)>
                    {
                        ("Nguyễn Huệ (Quang Trung Hoàng Đế)", "nguyenhue@sudaiviet.com"),
                        ("Nguyễn Nhạc (Tây Sơn Vương)", "nguyennhac@sudaiviet.com"),
                        ("Bùi Thị Xuân (Đô Đốc Tượng Binh)", "buithixuan@sudaiviet.com"),
                        ("Trần Quang Diệu (Thiếu Phó Đô Đốc)", "tranquangdieu@sudaiviet.com"),
                        ("Vũ Văn Dũng (Đại Đô Đốc)", "vuvandung@sudaiviet.com"),
                        ("Nguyễn Lữ (Đông Định Vương)", "nguyenlu@sudaiviet.com")
                    };

                    foreach (var hp in heroProfiles)
                    {
                        // Kiểm tra xem đã tồn tại chưa
                        var existing = await _context.Profiles.FirstOrDefaultAsync(p => p.Email == hp.Email);
                        if (existing == null)
                        {
                            var newProfile = new Profile
                            {
                                Id = Guid.NewGuid(),
                                DisplayName = hp.Name,
                                Email = hp.Email,
                                Role = "player",
                                CreatedAt = DateTime.UtcNow,
                                IsBanned = false,
                                Level = random.Next(15, 60),
                                Experience = random.Next(100, 5000),
                                AvatarUrl = ""
                            };
                            await _context.Profiles.AddAsync(newProfile);
                            
                            // Tạo ví tương ứng cho profile
                            var wallet = new Wallet
                            {
                                UserId = newProfile.Id,
                                GoldBalance = random.Next(1000, 50000),
                                GemBalance = random.Next(10, 500),
                                UpdatedAt = DateTime.UtcNow
                            };
                            await _context.Wallets.AddAsync(wallet);
                        }
                    }
                    await _context.SaveChangesAsync();
                    profiles = await _context.Profiles.ToListAsync();
                }

                // 2. Seed dữ liệu Bảng Xếp Hạng (Leaderboards)
                // Xóa sạch bảng xếp hạng cũ để seed mới tinh
                var oldLbs = await _context.Leaderboards.ToListAsync();
                _context.Leaderboards.RemoveRange(oldLbs);
                await _context.SaveChangesAsync();

                var seededLbs = new List<Leaderboard>();
                var namesAndScores = new List<(string Name, int Score, string Stage)>
                {
                    ("Nguyễn Huệ (Quang Trung Hoàng Đế)", 9999, "Ải 15"),
                    ("Nguyễn Nhạc (Tây Sơn Vương)", 8500, "Ải 12"),
                    ("Bùi Thị Xuân (Đô Đốc Tượng Binh)", 7800, "Ải 11"),
                    ("Trần Quang Diệu (Thiếu Phó Đô Đốc)", 7600, "Ải 11"),
                    ("Vũ Văn Dũng (Đại Đô Đốc)", 7200, "Ải 10"),
                    ("Nguyễn Lữ (Đông Định Vương)", 6500, "Ải 9")
                };

                for (int i = 0; i < profiles.Count; i++)
                {
                    var profile = profiles[i];
                    string scoreName = profile.DisplayName ?? "Nghĩa Sĩ";
                    int scoreVal = random.Next(1000, 6000);
                    string stageReached = $"Ải {random.Next(3, 10)}";

                    // Nếu profile khớp với anh hùng thì lấy điểm cao cố định
                    var matchedHero = namesAndScores.FirstOrDefault(n => profile.DisplayName != null && profile.DisplayName.Contains(n.Name.Split(' ')[0]));
                    if (matchedHero.Name != null)
                    {
                        scoreName = matchedHero.Name;
                        scoreVal = matchedHero.Score;
                        stageReached = matchedHero.Stage;
                    }

                    var lb = new Leaderboard
                    {
                        UserId = profile.Id,
                        Username = scoreName,
                        Score = scoreVal,
                        StageReached = stageReached,
                        UpdatedAt = DateTime.UtcNow
                    };
                    seededLbs.Add(lb);
                }

                await _context.Leaderboards.AddRangeAsync(seededLbs);
                await _context.SaveChangesAsync();

                // 3. Seed dữ liệu Nhật ký trận chiến (battle_logs) dùng SQL Raw
                // Xóa sạch battle_logs cũ
                await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE battle_logs RESTART IDENTITY CASCADE");

                // Tạo danh sách trận đấu mẫu
                var stages = new[] { "rach_gam_01", "ngoc_hoi_01", "tay_son_01", "phu_xuan_03" };
                var statuses = new[] { "Victory", "Defeat", "Aborted" };
                
                // Trận sạch
                for (int i = 0; i < 15; i++)
                {
                    var profile = profiles[random.Next(profiles.Count)];
                    var stage = stages[random.Next(stages.Length)];
                    var status = statuses[random.Next(statuses.Length)];
                    var duration = random.Next(45, 200);
                    var score = status == "Victory" ? random.Next(100, 500) : random.Next(10, 50);
                    var gold = status == "Victory" ? random.Next(50, 250) : random.Next(5, 30);
                    var killed = random.Next(3, 20);
                    
                    var attackerDmg = random.Next(600, 1600);
                    var defenderDmg = random.Next(100, 800);
                    var metadata = $"{{\"attacker_damage\": {attackerDmg}, \"defender_damage\": {defenderDmg}, \"attacker_max_hp\": 120, \"enemies_killed\": {killed}}}";

                    await _context.Database.ExecuteSqlRawAsync(
                        "INSERT INTO battle_logs (user_id, stage_id, battle_status, duration_seconds, score_earned, gold_earned, enemies_killed, battle_metadata, created_at) " +
                        "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}::jsonb, {8})",
                        profile.Id, stage, status, duration, score, gold, killed, metadata, DateTime.UtcNow.AddMinutes(-random.Next(10, 1000))
                    );
                }

                // Trận gian lận (Cheat / Nghi vấn)
                for (int i = 0; i < 4; i++)
                {
                    var profile = profiles[random.Next(profiles.Count)];
                    var stage = stages[random.Next(stages.Length)];
                    var duration = random.Next(1, 4); // thời gian cực ngắn
                    var score = 2000;
                    var gold = 1000;
                    var killed = random.Next(25, 60);
                    
                    var attackerDmg = random.Next(30000, 150000); // sát thương cực lớn
                    var defenderDmg = random.Next(0, 30);
                    var metadata = $"{{\"attacker_damage\": {attackerDmg}, \"defender_damage\": {defenderDmg}, \"attacker_max_hp\": 120, \"enemies_killed\": {killed}}}";

                    await _context.Database.ExecuteSqlRawAsync(
                        "INSERT INTO battle_logs (user_id, stage_id, battle_status, duration_seconds, score_earned, gold_earned, enemies_killed, battle_metadata, created_at) " +
                        "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}::jsonb, {8})",
                        profile.Id, stage, "Victory", duration, score, gold, killed, metadata, DateTime.UtcNow.AddMinutes(-random.Next(5, 120))
                    );
                }

                // Ghi nhật ký hành động Admin
                await _adminLogService.LogActionAsync(
                    "Admin_System",
                    "Khởi tạo dữ liệu mẫu",
                    "Đã nạp thành công dữ liệu mẫu bảng xếp hạng và nhật ký chiến đấu thật vào Supabase Database."
                );

                return Ok(new { message = "Khởi tạo dữ liệu mẫu thật vào Supabase Database thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi nạp dữ liệu mẫu.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Xem lịch sử nhật ký trận chiến toàn hệ thống (Hỗ trợ phân trang, tìm kiếm theo tên người chơi/mã ải, lọc sát thương tối thiểu).
        /// (Bảo vệ bằng X-Admin-Key).
        /// </summary>
        [HttpGet("battle-logs")]
        public async Task<ActionResult<PaginatedResult<BattleLogAdminDto>>> GetBattleLogs(
            [FromQuery] string? search = null,
            [FromQuery] int? minDamage = null,
            [FromQuery] string? stageId = null,
            [FromQuery] int pageIndex = 1,
            [FromQuery] int pageSize = 15)
        {
            if (!IsAuthorizedAdmin())
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            if (pageIndex < 1) pageIndex = 1;
            if (pageSize < 1) pageSize = 15;
            if (pageSize > 100) pageSize = 100;

            try
            {
                var countSql = "SELECT COUNT(*) FROM battle_logs b LEFT JOIN profiles p ON b.user_id = p.id WHERE 1=1";
                var parameters = new List<object>();
                int paramIdx = 0;

                if (!string.IsNullOrWhiteSpace(search))
                {
                    countSql = countSql + $" AND (p.display_name ILIKE @p{paramIdx} OR CAST(b.id AS TEXT) ILIKE @p{paramIdx})";
                    parameters.Add($"%{search.Trim()}%");
                    paramIdx++;
                }

                if (minDamage.HasValue)
                {
                    countSql = countSql + $" AND CAST(b.battle_metadata->>'attacker_damage' AS INTEGER) >= @p{paramIdx}";
                    parameters.Add(minDamage.Value);
                    paramIdx++;
                }

                if (!string.IsNullOrWhiteSpace(stageId))
                {
                    countSql = countSql + $" AND b.stage_id = @p{paramIdx}";
                    parameters.Add(stageId);
                    paramIdx++;
                }

                var conn = _context.Database.GetDbConnection();
                var wasOpen = conn.State == System.Data.ConnectionState.Open;
                if (!wasOpen) await conn.OpenAsync();

                int totalItems = 0;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = countSql;
                    for (int i = 0; i < parameters.Count; i++)
                    {
                        var p = cmd.CreateParameter();
                        p.ParameterName = $"@p{i}";
                        p.Value = parameters[i];
                        cmd.Parameters.Add(p);
                    }
                    var countResult = await cmd.ExecuteScalarAsync();
                    totalItems = Convert.ToInt32(countResult);
                }

                var dataSql = @"
                    SELECT 
                        b.id as Id, 
                        b.user_id as UserId, 
                        COALESCE(p.display_name, 'Nghĩa Sĩ') as Username, 
                        b.stage_id as StageId, 
                        b.battle_status as BattleStatus, 
                        b.duration_seconds as DurationSeconds, 
                        b.score_earned as ScoreEarned, 
                        b.gold_earned as GoldEarned, 
                        b.enemies_killed as EnemiesKilled, 
                        b.battle_metadata::text as BattleMetadata, 
                        b.created_at as CreatedAt
                    FROM battle_logs b
                    LEFT JOIN profiles p ON b.user_id = p.id
                    WHERE 1=1";

                paramIdx = 0;
                if (!string.IsNullOrWhiteSpace(search))
                {
                    dataSql = dataSql + $" AND (p.display_name ILIKE @p{paramIdx} OR CAST(b.id AS TEXT) ILIKE @p{paramIdx})";
                    paramIdx++;
                }

                if (minDamage.HasValue)
                {
                    dataSql = dataSql + $" AND CAST(b.battle_metadata->>'attacker_damage' AS INTEGER) >= @p{paramIdx}";
                    paramIdx++;
                }

                if (!string.IsNullOrWhiteSpace(stageId))
                {
                    dataSql = dataSql + $" AND b.stage_id = @p{paramIdx}";
                    paramIdx++;
                }

                dataSql = dataSql + $" ORDER BY b.created_at DESC LIMIT {pageSize} OFFSET {(pageIndex - 1) * pageSize}";

                var items = new List<BattleLogAdminDto>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = dataSql;
                    for (int i = 0; i < parameters.Count; i++)
                    {
                        var p = cmd.CreateParameter();
                        p.ParameterName = $"@p{i}";
                        p.Value = parameters[i];
                        cmd.Parameters.Add(p);
                    }

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            items.Add(new BattleLogAdminDto
                            {
                                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                                UserId = reader.GetGuid(reader.GetOrdinal("UserId")),
                                Username = reader.GetString(reader.GetOrdinal("Username")),
                                StageId = reader.GetString(reader.GetOrdinal("StageId")),
                                BattleStatus = reader.GetString(reader.GetOrdinal("BattleStatus")),
                                DurationSeconds = reader.GetInt32(reader.GetOrdinal("DurationSeconds")),
                                ScoreEarned = reader.GetInt32(reader.GetOrdinal("ScoreEarned")),
                                GoldEarned = reader.GetInt32(reader.GetOrdinal("GoldEarned")),
                                EnemiesKilled = reader.GetInt32(reader.GetOrdinal("EnemiesKilled")),
                                BattleMetadata = reader.IsDBNull(reader.GetOrdinal("BattleMetadata")) ? null : reader.GetString(reader.GetOrdinal("BattleMetadata")),
                                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                            });
                        }
                    }
                }

                if (!wasOpen) await conn.CloseAsync();

                var result = new PaginatedResult<BattleLogAdminDto>
                {
                    Items = items,
                    TotalItems = totalItems,
                    PageIndex = pageIndex,
                    PageSize = pageSize
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lấy danh sách nhật ký chiến đấu.", detail = ex.Message });
            }
        }
    }

    public class BattleLogAdminDto
    {
        public long Id { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = "Nghĩa Sĩ";
        public string StageId { get; set; } = "";
        public string BattleStatus { get; set; } = "";
        public int DurationSeconds { get; set; }
        public int ScoreEarned { get; set; }
        public int GoldEarned { get; set; }
        public int EnemiesKilled { get; set; }
        public string? BattleMetadata { get; set; }
        public DateTime CreatedAt { get; set; }
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
