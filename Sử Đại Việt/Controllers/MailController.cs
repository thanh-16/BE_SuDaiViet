using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sử_Đại_Việt.Models;
using Sử_Đại_Việt.Services;

namespace Sử_Đại_Việt.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [EnableRateLimiting("ScoreSubmitPolicy")]
    public class MailController : ControllerBase
    {
        private readonly IMailService _mailService;

        public MailController(IMailService mailService)
        {
            _mailService = mailService;
        }

        /// <summary>
        /// Xem toàn bộ hòm thư cá nhân & hệ thống.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<MailboxItem>>> GetMyMailbox()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                             ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            var mailbox = await _mailService.GetMailboxAsync(userId);
            return Ok(mailbox);
        }

        /// <summary>
        /// Đọc nội dung thư và chuyển trạng thái đã đọc (is_read = true).
        /// </summary>
        [HttpPut("{id}/read")]
        public async Task<IActionResult> ReadMail(long id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                             ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            try
            {
                var mail = await _mailService.ReadMailAsync(userId, id);
                return Ok(mail);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi đọc thư.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Nhận toàn bộ quà đính kèm trong thư (Vàng, Ngọc, Trang bị).
        /// </summary>
        [HttpPost("{id}/claim")]
        public async Task<IActionResult> ClaimMailAttachments(long id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                             ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            try
            {
                var mail = await _mailService.ClaimMailAttachmentsAsync(userId, id);
                return Ok(new { message = "Nhận quà từ thư thành công!", mail = mail });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi nhận quà từ thư.", detail = ex.Message });
            }
        }
    }
}
