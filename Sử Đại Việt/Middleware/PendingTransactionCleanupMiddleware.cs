using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sử_Đại_Việt.Services;

namespace Sử_Đại_Việt.Middleware
{
    /// <summary>
    /// Middleware tự động dọn dẹp các giao dịch Pending quá hạn (> 10 phút) mỗi khi có request đến.
    /// Khắc phục vấn đề Render Free Tier ngủ khiến BackgroundService không chạy được.
    /// Chỉ chạy cleanup tối đa mỗi 60 giây một lần để tránh làm chậm request.
    /// </summary>
    public class PendingTransactionCleanupMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<PendingTransactionCleanupMiddleware> _logger;
        private static DateTime _lastCleanupTime = DateTime.MinValue;
        private static readonly object _lock = new();
        private const int CleanupIntervalSeconds = 60; // Tối đa chạy mỗi 60 giây
        private const int ExpirationMinutes = 10;

        public PendingTransactionCleanupMiddleware(
            RequestDelegate next,
            ILogger<PendingTransactionCleanupMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Chạy cleanup bất đồng bộ (fire-and-forget) để không chặn request chính
            bool shouldCleanup = false;
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastCleanupTime).TotalSeconds >= CleanupIntervalSeconds)
                {
                    _lastCleanupTime = now;
                    shouldCleanup = true;
                }
            }

            if (shouldCleanup)
            {
                // Fire-and-forget: dọn dẹp nền, không chặn response
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
                        var shopService = scope.ServiceProvider.GetRequiredService<IShopService>();
                        int failedCount = await shopService.FailExpiredPendingTransactionsAsync(ExpirationMinutes);
                        if (failedCount > 0)
                        {
                            _logger.LogWarning(
                                "[PendingCleanupMiddleware] Đã tự động chuyển {Count} giao dịch Pending quá hạn (>{Expiry} phút) thành Failed.",
                                failedCount, ExpirationMinutes);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[PendingCleanupMiddleware] Lỗi khi dọn dẹp giao dịch Pending quá hạn.");
                    }
                });
            }

            await _next(context);
        }
    }
}
