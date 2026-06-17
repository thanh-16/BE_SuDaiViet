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
using PayOS;
using PayOS.Models.V2.PaymentRequests;
using PayOS.Models.Webhooks;
using Microsoft.Extensions.Configuration;

namespace Sử_Đại_Việt.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShopController : ControllerBase
    {
        private readonly IShopService _shopService;
        private readonly PayOSClient _payOS;
        private readonly IConfiguration _configuration;

        public ShopController(IShopService shopService, PayOSClient payOS, IConfiguration configuration)
        {
            _shopService = shopService;
            _payOS = payOS;
            _configuration = configuration;
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

        /// <summary>
        /// Tạo link thanh toán nạp tiền bằng PayOS (Yêu cầu JWT Token).
        /// </summary>
        [HttpPost("payos/create-link")]
        [Authorize]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<IActionResult> CreatePayOSLink([FromBody] CreatePayOSTopupDto model)
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
                var transaction = await _shopService.CreatePendingTopupAsync(userId, model.AmountVnd);
                var returnUrl = _configuration["PayOS:ReturnUrl"] ?? "http://localhost:5173/payment-success";
                var cancelUrl = _configuration["PayOS:CancelUrl"] ?? "http://localhost:5173/payment-cancel";

                var paymentRequest = new CreatePaymentLinkRequest
                {
                    OrderCode = transaction.Id,
                    Amount = model.AmountVnd,
                    Description = $"Nap Vang Ngoc {transaction.Id}",
                    ReturnUrl = returnUrl,
                    CancelUrl = cancelUrl,
                    Items = new List<PaymentLinkItem>
                    {
                        new PaymentLinkItem
                        {
                            Name = "Nạp Vàng Ngọc",
                            Quantity = 1,
                            Price = model.AmountVnd
                        }
                    }
                };

                var paymentResult = await _payOS.PaymentRequests.CreateAsync(paymentRequest);

                return Ok(new
                {
                    message = "Tạo link thanh toán thành công!",
                    checkoutUrl = paymentResult.CheckoutUrl,
                    transactionId = transaction.Id,
                    amountVnd = transaction.AmountVnd,
                    status = transaction.Status
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
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống trong quá trình tạo link thanh toán.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Webhook tiếp nhận kết quả thanh toán từ PayOS (Không yêu cầu JWT, tự xác thực chữ ký bảo mật bằng ChecksumKey).
        /// </summary>
        [HttpPost("payos/webhook")]
        public async Task<IActionResult> HandlePayOSWebhook([FromBody] Webhook webhookBody)
        {
            try
            {
                var verifiedData = await _payOS.Webhooks.VerifyAsync(webhookBody);

                long transactionId = verifiedData.OrderCode;

                if (webhookBody.Code == "00")
                {
                    string referenceId = verifiedData.Reference;
                    try
                    {
                        await _shopService.CompleteTopupAsync(transactionId, referenceId);
                    }
                    catch (KeyNotFoundException)
                    {
                        return Ok(new { success = true, message = "Giao dịch không tồn tại trong hệ thống nhưng xác thực chữ ký thành công (Webhook test)." });
                    }
                }
                else
                {
                    try
                    {
                        await _shopService.CancelTopupAsync(transactionId);
                    }
                    catch (KeyNotFoundException)
                    {
                        return Ok(new { success = true, message = "Giao dịch không tồn tại trong hệ thống nhưng xác thực chữ ký thành công (Webhook test)." });
                    }
                }

                return Ok(new { success = true, message = "Xác thực và cập nhật giao dịch PayOS thành công!" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = "Xác thực chữ ký Webhook thất bại.", detail = ex.Message });
            }
        }
    }
}
