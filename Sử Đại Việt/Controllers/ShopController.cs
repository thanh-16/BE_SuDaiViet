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
    public class ShopController : ControllerBase
    {
        private readonly IShopService _shopService;

        public ShopController(IShopService shopService)
        {
            _shopService = shopService;
        }

        /// <summary>
        /// Lấy toàn bộ danh sách vật phẩm đang bán trong cửa hàng.
        /// </summary>
        [HttpGet("items")]
        public async Task<ActionResult<IEnumerable<GameItem>>> GetItems()
        {
            var items = await _shopService.GetShopItemsAsync();
            return Ok(items);
        }

        /// <summary>
        /// Lấy danh sách kho đồ cá nhân của người chơi (Yêu cầu JWT Token).
        /// </summary>
        [HttpGet("inventory")]
        [Authorize]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<ActionResult<IEnumerable<PlayerInventory>>> GetInventory()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                             ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            var inventory = await _shopService.GetPlayerInventoryAsync(userId);
            return Ok(inventory);
        }

        /// <summary>
        /// Thực hiện mua vật phẩm trong cửa hàng bằng Gold hoặc Gem (Yêu cầu JWT Token).
        /// </summary>
        [HttpPost("buy")]
        [Authorize]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<IActionResult> BuyItem([FromBody] PurchaseItemDto model)
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
                var transaction = await _shopService.BuyItemAsync(userId, model.ItemId, model.Currency);
                return Ok(new
                {
                    message = "Mua vật phẩm thành công!",
                    transaction = transaction
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
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
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống trong quá trình giao dịch.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Giả lập nạp tiền VND quy đổi ra Vàng và Ngọc trong game (Yêu cầu JWT Token).
        /// </summary>
        [HttpPost("topup")]
        [Authorize]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<IActionResult> MockTopup([FromBody] MockTopupDto model)
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
                var transaction = await _shopService.ProcessTopupAsync(
                    userId, 
                    model.AmountVnd, 
                    model.PaymentMethod, 
                    model.ReferenceId
                );

                return Ok(new
                {
                    message = $"Nạp tiền thành công! Đã quy đổi {model.AmountVnd:N0} VND thành {transaction.AmountGold:N0} Vàng và {transaction.AmountGem:N0} Ngọc.",
                    transaction = transaction
                });
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
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống trong quá trình nạp tiền.", detail = ex.Message });
            }
        }
    }
}
