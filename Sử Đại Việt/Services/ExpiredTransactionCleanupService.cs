using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sử_Đại_Việt.Services
{
    /// <summary>
    /// Background Hosted Service tự động quét và chuyển các giao dịch Pending quá hạn (> 10 phút) thành Failed.
    /// Chạy mỗi 2 phút trên server, không phụ thuộc vào việc Client có polling hay không.
    /// </summary>
    public class ExpiredTransactionCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ExpiredTransactionCleanupService> _logger;

        /// <summary>
        /// Chu kỳ quét: mỗi 2 phút.
        /// </summary>
        private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Giao dịch Pending quá bao nhiêu phút sẽ bị tự động chuyển thành Failed.
        /// </summary>
        private const int ExpirationMinutes = 10;

        public ExpiredTransactionCleanupService(
            IServiceScopeFactory scopeFactory,
            ILogger<ExpiredTransactionCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "[ExpiredTxnCleanup] Background service khởi động. Quét mỗi {Interval} phút, hủy đơn Pending quá {Expiry} phút.",
                ScanInterval.TotalMinutes, ExpirationMinutes);

            // Đợi 30 giây sau khi server khởi động để DB sẵn sàng
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var shopService = scope.ServiceProvider.GetRequiredService<IShopService>();

                    int failedCount = await shopService.FailExpiredPendingTransactionsAsync(ExpirationMinutes);

                    if (failedCount > 0)
                    {
                        _logger.LogWarning(
                            "[ExpiredTxnCleanup] Đã tự động chuyển {Count} giao dịch Pending quá hạn (>{Expiry} phút) thành Failed.",
                            failedCount, ExpirationMinutes);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[ExpiredTxnCleanup] Lỗi khi quét giao dịch Pending quá hạn. Sẽ thử lại sau {Interval} phút.",
                        ScanInterval.TotalMinutes);
                }

                await Task.Delay(ScanInterval, stoppingToken);
            }

            _logger.LogInformation("[ExpiredTxnCleanup] Background service đã dừng.");
        }
    }
}
