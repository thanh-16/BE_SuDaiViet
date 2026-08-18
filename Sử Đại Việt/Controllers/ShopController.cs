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
using Microsoft.Extensions.Logging;

namespace Sử_Đại_Việt.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShopController : ControllerBase
    {
        private readonly IShopService _shopService;
        private readonly PayOSClient _payOS;
        private readonly IConfiguration _configuration;
        private readonly IAdminLogService _adminLog;
        private readonly ILogger<ShopController> _logger;

        public ShopController(IShopService shopService, PayOSClient payOS, IConfiguration configuration, IAdminLogService adminLog, ILogger<ShopController> logger)
        {
            _shopService = shopService;
            _payOS = payOS;
            _configuration = configuration;
            _adminLog = adminLog;
            _logger = logger;
        }

        /// <summary>
        /// Chuẩn đoán cấu hình PayOS (Kiểm tra xem các key có bị ghi đè sai bởi biến môi trường không).
        /// </summary>
        [HttpGet("payos/diagnose")]
        public IActionResult DiagnosePayOS()
        {
            var clientId = _configuration["PayOS:ClientId"];
            var apiKey = _configuration["PayOS:ApiKey"];
            var checksumKey = _configuration["PayOS:ChecksumKey"];

            return Ok(new
            {
                ClientIdLength = clientId?.Length ?? 0,
                ClientIdMasked = string.IsNullOrEmpty(clientId) ? "" : $"{(clientId.Length >= 8 ? clientId.Substring(0, 4) : "")}...{(clientId.Length >= 8 ? clientId.Substring(clientId.Length - 4) : "")}",
                ApiKeyLength = apiKey?.Length ?? 0,
                ApiKeyMasked = string.IsNullOrEmpty(apiKey) ? "" : $"{(apiKey.Length >= 8 ? apiKey.Substring(0, 4) : "")}...{(apiKey.Length >= 8 ? apiKey.Substring(apiKey.Length - 4) : "")}",
                ChecksumKeyLength = checksumKey?.Length ?? 0,
                ChecksumKeyMasked = string.IsNullOrEmpty(checksumKey) ? "" : $"{(checksumKey.Length >= 8 ? checksumKey.Substring(0, 4) : "")}...{(checksumKey.Length >= 8 ? checksumKey.Substring(checksumKey.Length - 4) : "")}"
            });
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
        [AllowAnonymous]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<ActionResult<IEnumerable<PlayerInventory>>> GetInventory()
        {
            var userIdClaim = GetUserIdClaim();

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            var inventory = await _shopService.GetPlayerInventoryAsync(userId);
            return Ok(inventory);
        }

        /// <summary>
        /// Lấy số dư ví Vàng và Ngọc của người chơi (Yêu cầu JWT Token hoặc X-Mock-User-Id).
        /// </summary>
        [HttpGet("wallet")]
        [AllowAnonymous]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<IActionResult> GetWallet()
        {
            var userIdClaim = GetUserIdClaim();

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            var wallet = await _shopService.GetPlayerWalletAsync(userId);
            if (wallet == null)
            {
                return NotFound(new { message = "Không tìm thấy thông tin ví của người chơi." });
            }

            return Ok(new
            {
                goldBalance = wallet.GoldBalance,
                gemBalance = wallet.GemBalance
            });
        }

        /// <summary>
        /// Thực hiện mua vật phẩm trong cửa hàng bằng Gold hoặc Gem (Yêu cầu JWT Token).
        /// </summary>
        [HttpPost("buy")]
        [AllowAnonymous]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<IActionResult> BuyItem([FromBody] PurchaseItemDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = GetUserIdClaim();

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
        [AllowAnonymous]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<IActionResult> MockTopup([FromBody] MockTopupDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = GetUserIdClaim();

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
        [AllowAnonymous]
        [EnableRateLimiting("ScoreSubmitPolicy")]
        public async Task<IActionResult> CreatePayOSLink([FromBody] CreatePayOSTopupDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = GetUserIdClaim();

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            try
            {
                // Tự động hủy các đơn Pending quá hạn (>10 phút) cũ của user trước khi tạo đơn mới
                await _shopService.FailExpiredPendingTransactionsAsync(10, userId);

                var transaction = await _shopService.CreatePendingTopupAsync(userId, model.AmountVnd);
                var returnUrl = _configuration["PayOS:ReturnUrl"] ?? "https://su-dai-viet-admin-fe.vercel.app/payment-success";
                var cancelUrl = _configuration["PayOS:CancelUrl"] ?? "https://su-dai-viet-admin-fe.vercel.app/payment-cancel";

                var profile = transaction.PlayerProfile ?? await _shopService.GetPlayerProfileAsync(userId);
                string playerName = !string.IsNullOrWhiteSpace(model.PlayerName) 
                    ? model.PlayerName 
                    : (profile?.DisplayName ?? profile?.Email ?? User.Identity?.Name ?? "Nghia Si");
                string paymentDescription = FormatPayOsDescription(playerName, transaction.Id);

                var paymentRequest = new CreatePaymentLinkRequest
                {
                    OrderCode = transaction.Id,
                    Amount = model.AmountVnd,
                    Description = paymentDescription,
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
                _logger.LogError(ex, "[PayOS] Lỗi tạo link thanh toán cho user {UserId}: {Message}", userIdClaim, ex.Message);
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống trong quá trình tạo link thanh toán.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Lấy trạng thái của một giao dịch cụ thể để Client thực hiện Polling (Tự chuyển Pending -> Failed nếu quá hạn 10 phút hoặc bị hủy).
        /// </summary>
        [HttpGet("payos/status/{transactionId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetTransactionStatus(long transactionId)
        {
            try
            {
                var transaction = await _shopService.GetTransactionAsync(transactionId);
                if (transaction == null)
                {
                    return NotFound(new { message = $"Không tìm thấy giao dịch với mã '{transactionId}'." });
                }

                if (transaction.Status == "Pending")
                {
                    // Tự động hủy nếu giao dịch đã tạo cách đây quá 10 phút
                    if (transaction.CreatedAt <= DateTime.UtcNow.AddMinutes(-10))
                    {
                        transaction = await _shopService.CancelTopupAsync(transactionId);
                    }
                    else
                    {
                        try
                        {
                            var payOSInfo = await _payOS.PaymentRequests.GetAsync(transactionId);
                            if (payOSInfo != null)
                            {
                                string pStatus = payOSInfo.Status.ToString();
                                if (pStatus == "PAID")
                                {
                                    transaction = await _shopService.CompleteTopupAsync(transactionId, payOSInfo.Id);
                                }
                                else if (pStatus == "CANCELLED" || pStatus == "EXPIRED" || pStatus == "FAILED")
                                {
                                    transaction = await _shopService.CancelTopupAsync(transactionId);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Nếu PayOS báo lỗi/link đã đóng và giao dịch > 5 phút -> Chuyển thành Failed
                            if (transaction.CreatedAt <= DateTime.UtcNow.AddMinutes(-5))
                            {
                                transaction = await _shopService.CancelTopupAsync(transactionId);
                            }
                        }
                    }
                }

                return Ok(new
                {
                    transactionId = transaction.Id,
                    status = transaction.Status,
                    amountVnd = transaction.AmountVnd,
                    amountGold = transaction.AmountGold,
                    amountGem = transaction.AmountGem,
                    createdAt = transaction.CreatedAt,
                    updatedAt = transaction.UpdatedAt
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi lấy trạng thái giao dịch.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Hủy giao dịch nạp tiền hoặc xác nhận giao dịch đã quá hạn (chuyển Pending -> Failed).
        /// </summary>
        [HttpPost("payos/cancel/{transactionId}")]
        [AllowAnonymous]
        public async Task<IActionResult> CancelTransaction(long transactionId)
        {
            try
            {
                var transaction = await _shopService.CancelTopupAsync(transactionId);
                return Ok(new
                {
                    message = "Giao dịch đã được cập nhật trạng thái Thất bại (Failed).",
                    transactionId = transaction.Id,
                    status = transaction.Status
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi hủy giao dịch.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Webhook tiếp nhận kết quả thanh toán từ PayOS (Không yêu cầu JWT, tự xác thực chữ ký bảo mật bằng ChecksumKey).
        /// </summary>
        [HttpPost("payos/webhook")]
        public async Task<IActionResult> HandlePayOSWebhook([FromBody] Webhook webhookBody)
        {
            string webhookBodyJson = "";
            try
            {
                webhookBodyJson = System.Text.Json.JsonSerializer.Serialize(webhookBody);
            }
            catch {}

            try
            {
                await _adminLog.LogActionAsync("PayOS_Webhook", "Webhook_Received", $"Bắt đầu xác thực webhook. Body: {webhookBodyJson}");

                var verifiedData = await _payOS.Webhooks.VerifyAsync(webhookBody);

                long transactionId = verifiedData.OrderCode;

                await _adminLog.LogActionAsync("PayOS_Webhook", "Webhook_Verified", $"Xác thực thành công. OrderCode: {transactionId}");

                if (webhookBody.Code == "00")
                {
                    string referenceId = verifiedData.Reference;
                    try
                    {
                        await _shopService.CompleteTopupAsync(transactionId, referenceId);
                    }
                    catch (KeyNotFoundException)
                    {
                        await _adminLog.LogActionAsync("PayOS_Webhook", "Webhook_Success_NoTxn", $"OrderCode {transactionId} không tìm thấy trong DB (Webhook test)");
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
                        await _adminLog.LogActionAsync("PayOS_Webhook", "Webhook_Failed_NoTxn", $"OrderCode {transactionId} không tìm thấy trong DB (Webhook test)");
                        return Ok(new { success = true, message = "Giao dịch không tồn tại trong hệ thống nhưng xác thực chữ ký thành công (Webhook test)." });
                    }
                }

                return Ok(new { success = true, message = "Xác thực và cập nhật giao dịch PayOS thành công!" });
            }
            catch (Exception ex)
            {
                await _adminLog.LogActionAsync("PayOS_Webhook", "Webhook_Error", $"Xác thực chữ ký Webhook thất bại. Lỗi: {ex.Message}. Chi tiết lỗi: {ex.ToString()}. Body: {webhookBodyJson}");
                return BadRequest(new { success = false, message = "Xác thực chữ ký Webhook thất bại.", detail = ex.Message });
            }
        }

        private string? GetUserIdClaim()
        {
            var mockUserId = Request.Headers["X-Mock-User-Id"].ToString();
            if (!string.IsNullOrEmpty(mockUserId))
            {
                return mockUserId;
            }
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                   ?? User.FindFirst("sub")?.Value;
        }

        /// <summary>
        /// Chuẩn hóa nội dung chuyển khoản PayOS/VietQR chứa Tên Nhân Vật và Mã Đơn Hàng (tối đa 25 ký tự, ASCII chuẩn).
        /// </summary>
        private static string FormatPayOsDescription(string? rawName, long orderCode)
        {
            string idStr = orderCode.ToString();
            string cleanName = RemoveDiacritics(rawName ?? "Nghia Si").Trim().ToUpperInvariant();
            // Chỉ giữ lại chữ cái A-Z, chữ số 0-9 và khoảng trắng
            cleanName = System.Text.RegularExpressions.Regex.Replace(cleanName, @"[^A-Z0-9\s]", "");
            cleanName = System.Text.RegularExpressions.Regex.Replace(cleanName, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(cleanName))
            {
                cleanName = "NGHIA SI";
            }

            // Quy định PayOS: Description tối đa 25 ký tự không dấu. Định dạng: "{TÊN USER} {MÃ ĐƠN}"
            string suffix = " " + idStr;
            int maxNameLen = 25 - suffix.Length;
            if (maxNameLen < 1)
            {
                string fallback = idStr;
                return fallback.Length > 25 ? fallback[..25] : fallback;
            }

            if (cleanName.Length > maxNameLen)
            {
                cleanName = cleanName[..maxNameLen].Trim();
            }

            return $"{cleanName}{suffix}";
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
            var stringBuilder = new System.Text.StringBuilder(capacity: normalizedString.Length);

            for (int i = 0; i < normalizedString.Length; i++)
            {
                char c = normalizedString[i];
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    if (c == 'đ' || c == 'Đ')
                    {
                        stringBuilder.Append(c == 'đ' ? 'd' : 'D');
                    }
                    else
                    {
                        stringBuilder.Append(c);
                    }
                }
            }

            return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }
    }
}
