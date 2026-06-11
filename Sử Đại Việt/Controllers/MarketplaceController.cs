using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sử_Đại_Việt.Dtos;
using Sử_Đại_Việt.Models;
using Sử_Đại_Việt.Services;

namespace Sử_Đại_Việt.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MarketplaceController : ControllerBase
    {
        private readonly IMarketplaceService _marketplaceService;

        public MarketplaceController(IMarketplaceService marketplaceService)
        {
            _marketplaceService = marketplaceService;
        }

        /// <summary>
        /// Xem toàn bộ vật phẩm đang đăng bán Active trên chợ (Không cần đăng nhập).
        /// </summary>
        [HttpGet("listings")]
        public async Task<ActionResult<PaginatedResult<MarketplaceListing>>> GetListings(
            [FromQuery] string? search = null,
            [FromQuery] int pageIndex = 1,
            [FromQuery] int pageSize = 20)
        {
            if (pageIndex < 1) pageIndex = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var items = await _marketplaceService.GetActiveListingsAsync(search, pageIndex, pageSize);
            var total = await _marketplaceService.GetActiveListingsCountAsync(search);

            return Ok(new PaginatedResult<MarketplaceListing>
            {
                Items = items,
                TotalItems = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            });
        }

        /// <summary>
        /// Đăng bán một vật phẩm lên chợ (Yêu cầu JWT Token).
        /// </summary>
        [HttpPost("list")]
        [Authorize]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<IActionResult> ListItem([FromBody] ListMarketplaceItemDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                             ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            try
            {
                var listing = await _marketplaceService.ListItemAsync(userId, model.InventoryItemId, model.PriceGold, model.PriceGem);
                return Ok(new { message = "Đăng bán vật phẩm lên chợ thành công!", listing = listing });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi đăng bán vật phẩm.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Mua vật phẩm từ chợ (Yêu cầu JWT Token - Tự động trừ tiền và đổi chủ).
        /// </summary>
        [HttpPost("buy/{listingId}")]
        [Authorize]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<IActionResult> BuyItem(long listingId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                             ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            try
            {
                var transaction = await _marketplaceService.BuyMarketplaceItemAsync(userId, listingId);
                return Ok(new { message = "Giao dịch mua vật phẩm trên chợ thành công!", transaction = transaction });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi mua vật phẩm trên chợ.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Hủy bài đăng bán vật phẩm và nhận lại đồ vào rương (Yêu cầu JWT Token).
        /// </summary>
        [HttpPost("cancel/{listingId}")]
        [Authorize]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<IActionResult> CancelListing(long listingId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                             ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            try
            {
                await _marketplaceService.CancelListingAsync(userId, listingId);
                return Ok(new { message = "Đã hủy đăng bán vật phẩm và trả lại đồ vào rương thành công!" });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi hủy đăng bán.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Đặt chỗ giữ hàng vật phẩm trên chợ trong vòng 5 phút (Yêu cầu JWT Token).
        /// </summary>
        [HttpPost("reserve/{listingId}")]
        [Authorize]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<IActionResult> ReserveListing(long listingId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                             ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            try
            {
                var reservation = await _marketplaceService.ReserveListingAsync(userId, listingId);
                return Ok(new { message = "Đã đặt chỗ giữ hàng thành công trong 5 phút!", reservation = reservation });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi đặt chỗ vật phẩm.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Giải phóng đặt chỗ vật phẩm trên chợ (Yêu cầu JWT Token).
        /// </summary>
        [HttpPost("release/{listingId}")]
        [Authorize]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<IActionResult> ReleaseReservation(long listingId)
        {
            try
            {
                await _marketplaceService.ReleaseReservationAsync(listingId);
                return Ok(new { message = "Đã giải phóng đặt chỗ giữ hàng thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi giải phóng đặt chỗ.", detail = ex.Message });
            }
        }
    }
}
