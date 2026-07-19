using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sử_Đại_Việt.Data;
using Sử_Đại_Việt.Dtos;
using Sử_Đại_Việt.Models;
using Sử_Đại_Việt.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace Sử_Đại_Việt.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LeaderboardController : ControllerBase
    {
        private readonly ILeaderboardService _leaderboardService;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;
        private readonly IAdminLogService _adminLogService;

        public LeaderboardController(
            ILeaderboardService leaderboardService, 
            IConfiguration configuration, 
            ApplicationDbContext context,
            IAdminLogService adminLogService)
        {
            _leaderboardService = leaderboardService;
            _configuration = configuration;
            _context = context;
            _adminLogService = adminLogService;
        }

        /// <summary>
        /// Tải danh sách Top Bảng Vinh Danh.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Leaderboard>>> GetTopScores([FromQuery] int limit = 10, CancellationToken cancellationToken = default)
        {
            // Tối ưu bảo mật: Giới hạn số dòng tối thiểu và tối đa để tránh lỗi cạn kiệt tài nguyên máy chủ
            if (limit <= 0) limit = 10;
            if (limit > 100) limit = 100;

            var scores = await _leaderboardService.GetTopScoresAsync(limit, cancellationToken);
            return Ok(scores);
        }

        /// <summary>
        /// Gửi điểm số mới khi nghĩa sĩ lập chiến công trên chiến trường Tây Sơn (Yêu cầu JWT Token).
        /// </summary>
        [HttpPost]
        [Authorize]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<ActionResult<Leaderboard>> SubmitScore([FromBody] SubmitScoreDto model, CancellationToken cancellationToken)
        {
            // Tối ưu bảo mật tối đa: Trích xuất trực tiếp UserId (UUID) từ JWT Claim "sub" của Supabase để chống giả mạo
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                             ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var authUserId))
            {
                return Unauthorized("Mã định danh người dùng trong Token xác thực không hợp lệ.");
            }

            // Kiểm tra trạng thái tài khoản: Nếu bị khóa (Banned), chặn hoàn toàn việc gửi điểm số
            var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Id == authUserId, cancellationToken);
            if (profile != null && profile.IsBanned)
            {
                return StatusCode(403, "Tài khoản của bạn đã bị khóa do vi phạm điều khoản và chính sách của Sử Đại Việt. Không thể gửi điểm số.");
            }

            // Tối ưu & Bảo mật DB: Giới hạn độ dài chuỗi để khớp với database schema (VARCHAR(50))
            var sanitizedUsername = model.Username.Length > 50 ? model.Username.Substring(0, 50) : model.Username;
            var sanitizedStage = model.StageReached.Length > 50 ? model.StageReached.Substring(0, 50) : model.StageReached;

            // Sử dụng authUserId đã được giải mã an toàn từ JWT thay vì tin tưởng UserId từ request body của Client
            var record = await _leaderboardService.SubmitScoreAsync(authUserId, sanitizedUsername, model.Score, sanitizedStage, cancellationToken);
            if (record == null)
            {
                return StatusCode(500, "Lỗi vinh danh điểm số lên cơ sở dữ liệu đền đài.");
            }

            return Ok(record);
        }

        /// <summary>
        /// Xóa điểm số trên bảng xếp hạng (Dành cho Web Admin - Yêu cầu X-Admin-Key).
        /// </summary>
        [HttpDelete("{id}")]
        [EnableRateLimiting("AdminApiPolicy")]
        public async Task<IActionResult> DeleteScore(long id, CancellationToken cancellationToken)
        {
            // Kiểm tra bảo mật: Yêu cầu Header X-Admin-Key để xác thực quyền quản trị
            if (!Request.Headers.TryGetValue("X-Admin-Key", out var extractedKey) || 
                extractedKey != _configuration["AdminSettings:AdminKey"])
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            var record = await _context.Leaderboards.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
            if (record == null)
            {
                return NotFound($"Không tìm thấy điểm số có ID = {id} trên bảng xếp hạng.");
            }

            var success = await _leaderboardService.DeleteScoreAsync(id, cancellationToken);
            if (!success)
            {
                return StatusCode(500, "Không thể xóa điểm số khỏi bảng xếp hạng.");
            }

            // Ghi nhật ký kiểm toán hành động quản trị thông qua Service chuyên biệt
            await _adminLogService.LogActionAsync(
                "Admin_System",
                "Xóa điểm số gian lận",
                $"Đã xóa điểm số {record.Score} điểm, ải đạt được '{record.StageReached}' của người chơi '{record.Username}' (UserId = {record.UserId})."
            );

            return Ok(new { message = $"Đã xóa thành công điểm số có ID = {id}." });
        }

        /// <summary>
        /// Chỉnh sửa điểm số trực tiếp trên bảng xếp hạng (Dành cho Web Admin - Yêu cầu X-Admin-Key).
        /// </summary>
        [HttpPut("{id}")]
        [EnableRateLimiting("AdminApiPolicy")]
        public async Task<ActionResult<Leaderboard>> UpdateScore(long id, [FromBody] AdminUpdateScoreDto model, CancellationToken cancellationToken)
        {
            // Kiểm tra bảo mật: Yêu cầu Header X-Admin-Key để xác thực quyền quản trị
            if (!Request.Headers.TryGetValue("X-Admin-Key", out var extractedKey) || 
                extractedKey != _configuration["AdminSettings:AdminKey"])
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            var oldRecord = await _context.Leaderboards.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
            if (oldRecord == null)
            {
                return NotFound($"Không tìm thấy điểm số có ID = {id} trên bảng xếp hạng.");
            }

            string detail = $"Thay đổi điểm của người chơi '{oldRecord.Username}' (UserId = {oldRecord.UserId}) từ (Score={oldRecord.Score}, Stage='{oldRecord.StageReached}') sang (Score={model.Score}, Stage='{model.StageReached}').";

            var updated = await _leaderboardService.UpdateScoreAsync(id, model.Score, model.StageReached, cancellationToken);
            if (updated == null)
            {
                return StatusCode(500, "Lỗi cập nhật điểm số.");
            }

            // Ghi nhật ký kiểm toán hành động quản trị thông qua Service chuyên biệt
            await _adminLogService.LogActionAsync("Admin_System", "Điều chỉnh điểm số", detail);

            return Ok(updated);
        }
    }
}
