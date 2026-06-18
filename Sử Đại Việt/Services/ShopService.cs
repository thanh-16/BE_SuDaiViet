using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sử_Đại_Việt.Data;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public class ShopService : IShopService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAdminLogService _adminLogService;

        public ShopService(ApplicationDbContext context, IAdminLogService adminLogService)
        {
            _context = context;
            _adminLogService = adminLogService;
        }

        public async Task<IEnumerable<GameItem>> GetShopItemsAsync()
        {
            return await _context.GameItems
                .AsNoTracking()
                .OrderBy(i => i.ItemType)
                .ThenBy(i => i.Id)
                .ToListAsync();
        }

        public async Task<IEnumerable<PlayerInventory>> GetPlayerInventoryAsync(Guid userId)
        {
            return await _context.PlayerInventories
                .AsNoTracking()
                .Include(pi => pi.ItemDetails)
                .Where(pi => pi.UserId == userId)
                .ToListAsync();
        }

        public async Task<Wallet?> GetPlayerWalletAsync(Guid userId)
        {
            return await _context.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.UserId == userId);
        }

        public async Task<Transaction> BuyItemAsync(Guid userId, string itemId, string currency)
        {
            // Kiểm tra yêu cầu cấp độ (Level Requirement) từ JSONB attributes của vật phẩm trước khi mua
            var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Id == userId);
            if (profile == null)
            {
                throw new KeyNotFoundException("Không tìm thấy thông tin tài khoản người chơi.");
            }

            if (profile.IsBanned)
            {
                throw new InvalidOperationException("Tài khoản của bạn đã bị khóa.");
            }

            var item = await _context.GameItems.FirstOrDefaultAsync(i => i.Id == itemId);
            if (item == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy vật phẩm có mã '{itemId}'.");
            }

            if (!string.IsNullOrEmpty(item.Attributes))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(item.Attributes);
                    if (doc.RootElement.TryGetProperty("level_requirement", out var levelReqElement) && levelReqElement.TryGetInt32(out var levelReq))
                    {
                        if (profile.Level < levelReq)
                        {
                            throw new InvalidOperationException($"Cấp độ của bạn (Cấp {profile.Level}) không đủ để mua vật phẩm này. Yêu cầu tối thiểu cấp {levelReq}.");
                        }
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // JSON không hợp lệ, bỏ qua
                }
            }

            // Gọi Stored Procedure nguyên tử ở cơ sở dữ liệu
            var resultList = await _context.Database
                .SqlQueryRaw<string>("SELECT public.buy_shop_item({0}, {1}, {2}) as \"Value\"", userId, itemId, currency)
                .ToListAsync();

            var result = resultList.FirstOrDefault();

            if (result != null && result.StartsWith("ERROR:"))
            {
                throw new InvalidOperationException(result.Substring(6).Trim());
            }

            // Stored Procedure tự động cập nhật rương và thêm dòng transaction, chúng ta tải lại transaction mới nhất để trả về
            var txn = await _context.Transactions
                .Where(t => t.UserId == userId && t.TransactionType == "Purchase")
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (txn == null)
            {
                // Thêm fallback transaction để trả về
                txn = new Transaction
                {
                    UserId = userId,
                    TransactionType = "Purchase",
                    AmountVnd = 0,
                    AmountGold = currency.Equals("Gold", StringComparison.OrdinalIgnoreCase) ? -item.PriceGold : 0,
                    AmountGem = currency.Equals("Gem", StringComparison.OrdinalIgnoreCase) ? -item.PriceGem : 0,
                    PaymentMethod = currency.ToUpper(),
                    ReferenceId = $"BUY-{itemId.ToUpper()}-{Guid.NewGuid().ToString()[..8].ToUpper()}",
                    Status = "Completed",
                    CreatedAt = DateTime.UtcNow
                };
            }

            return txn;
        }

        public async Task<Transaction> ProcessTopupAsync(Guid userId, int amountVnd, string paymentMethod, string? referenceId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Khóa dòng dữ liệu wallets tránh race condition ví tiền khi nạp tiền
                await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM public.wallets WHERE user_id = {userId} FOR UPDATE");

                // 1. Kiểm tra người chơi tồn tại
                var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Id == userId);
                if (profile == null)
                {
                    throw new KeyNotFoundException("Không tìm thấy thông tin tài khoản người chơi.");
                }

                if (profile.IsBanned)
                {
                    throw new InvalidOperationException("Tài khoản của bạn đã bị khóa.");
                }

                var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
                if (wallet == null)
                {
                    throw new KeyNotFoundException("Không tìm thấy thông tin ví của người chơi.");
                }

                // 2. Quy đổi VNĐ sang Vàng và Ngọc (10,000 VND = 1,000 Gold và 10 Gems)
                // Công thức: Gold = VND / 10, Gem = VND / 1000
                int creditGold = amountVnd / 10;
                int creditGem = amountVnd / 1000;

                wallet.GoldBalance += creditGold;
                wallet.GemBalance += creditGem;
                wallet.UpdatedAt = DateTime.UtcNow;

                // 3. Ghi nhận giao dịch
                var txn = new Transaction
                {
                    UserId = userId,
                    TransactionType = "Topup",
                    AmountVnd = amountVnd,
                    AmountGold = creditGold,
                    AmountGem = creditGem,
                    PaymentMethod = paymentMethod,
                    ReferenceId = referenceId ?? $"TOPUP-MOCK-{Guid.NewGuid().ToString()[..8].ToUpper()}",
                    Status = "Completed",
                    CreatedAt = DateTime.UtcNow
                };

                _context.Transactions.Add(txn);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return txn;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<Transaction> CreatePendingTopupAsync(Guid userId, int amountVnd)
        {
            var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Id == userId);
            if (profile == null)
            {
                throw new KeyNotFoundException("Không tìm thấy thông tin tài khoản người chơi.");
            }

            if (profile.IsBanned)
            {
                throw new InvalidOperationException("Tài khoản của bạn đã bị khóa.");
            }

            // Quy đổi VNĐ sang Vàng và Ngọc (10,000 VND = 1,000 Gold và 10 Gems)
            int creditGold = amountVnd / 10;
            int creditGem = amountVnd / 1000;

            var txn = new Transaction
            {
                UserId = userId,
                TransactionType = "Topup",
                AmountVnd = amountVnd,
                AmountGold = creditGold,
                AmountGem = creditGem,
                PaymentMethod = "PayOS",
                ReferenceId = null,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Transactions.Add(txn);
            await _context.SaveChangesAsync();

            return txn;
        }

        public async Task<Transaction> CompleteTopupAsync(long transactionId, string referenceId)
        {
            using var dbTransaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var txn = await _context.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId);
                if (txn == null)
                {
                    throw new KeyNotFoundException($"Không tìm thấy giao dịch có mã '{transactionId}'.");
                }

                if (txn.Status == "Completed")
                {
                    return txn;
                }

                if (txn.Status != "Pending")
                {
                    throw new InvalidOperationException($"Giao dịch không ở trạng thái chờ xử lý (Trạng thái hiện tại: {txn.Status}).");
                }

                // Khóa dòng dữ liệu wallets tránh race condition ví tiền khi nạp tiền
                await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM public.wallets WHERE user_id = {txn.UserId} FOR UPDATE");

                var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == txn.UserId);
                if (wallet == null)
                {
                    throw new KeyNotFoundException("Không tìm thấy thông tin ví của người chơi.");
                }

                wallet.GoldBalance += txn.AmountGold;
                wallet.GemBalance += txn.AmountGem;
                wallet.UpdatedAt = DateTime.UtcNow;

                txn.Status = "Completed";
                txn.ReferenceId = referenceId;
                txn.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                return txn;
            }
            catch (Exception)
            {
                await dbTransaction.RollbackAsync();
                throw;
            }
        }

        public async Task<Transaction> CancelTopupAsync(long transactionId)
        {
            var txn = await _context.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId);
            if (txn == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy giao dịch có mã '{transactionId}'.");
            }

            if (txn.Status == "Pending")
            {
                txn.Status = "Failed";
                txn.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return txn;
        }


        public async Task<IEnumerable<Transaction>> GetTransactionsAsync(string? search, int pageIndex, int pageSize)
        {
            var query = _context.Transactions
                .Include(t => t.PlayerProfile)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(t =>
                    t.TransactionType.ToLower().Contains(lowerSearch) ||
                    (t.PaymentMethod != null && t.PaymentMethod.ToLower().Contains(lowerSearch)) ||
                    (t.ReferenceId != null && t.ReferenceId.ToLower().Contains(lowerSearch)) ||
                    (t.PlayerProfile != null && t.PlayerProfile.DisplayName.ToLower().Contains(lowerSearch)) ||
                    (t.PlayerProfile != null && t.PlayerProfile.Email != null && t.PlayerProfile.Email.ToLower().Contains(lowerSearch))
                );
            }

            return await query
                .OrderByDescending(t => t.CreatedAt)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> GetTransactionsCountAsync(string? search)
        {
            var query = _context.Transactions.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(t =>
                    t.TransactionType.ToLower().Contains(lowerSearch) ||
                    (t.PaymentMethod != null && t.PaymentMethod.ToLower().Contains(lowerSearch)) ||
                    (t.ReferenceId != null && t.ReferenceId.ToLower().Contains(lowerSearch)) ||
                    _context.Profiles.Any(p => p.Id == t.UserId && 
                        (p.DisplayName.ToLower().Contains(lowerSearch) || 
                         (p.Email != null && p.Email.ToLower().Contains(lowerSearch)))
                    )
                );
            }

            return await query.CountAsync();
        }

        public async Task<GameItem> UpdateItemPriceAsync(string itemId, int priceGold, int priceGem, int priceVnd, string adminUsername)
        {
            var item = await _context.GameItems.FirstOrDefaultAsync(i => i.Id == itemId);
            if (item == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy vật phẩm có mã '{itemId}'.");
            }

            string oldPrices = $"Vàng: {item.PriceGold}, Ngọc: {item.PriceGem}, VND: {item.PriceVnd}";
            
            item.PriceGold = priceGold;
            item.PriceGem = priceGem;
            item.PriceVnd = priceVnd;

            await _context.SaveChangesAsync();

            string newPrices = $"Vàng: {priceGold}, Ngọc: {priceGem}, VND: {priceVnd}";
            await _adminLogService.LogActionAsync(
                adminUsername, 
                "UpdateItemPrice", 
                $"Cập nhật giá vật phẩm '{itemId}' ({item.Name}). Trước: [{oldPrices}] -> Sau: [{newPrices}]."
            );

            return item;
        }

        public async Task<Profile> AdjustPlayerBalanceAsync(Guid userId, int goldAmount, int gemAmount, string reason, string adminUsername)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Khóa dòng wallets tránh race condition
                await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM public.wallets WHERE user_id = {userId} FOR UPDATE");

                var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Id == userId);
                if (profile == null)
                {
                    throw new KeyNotFoundException("Không tìm thấy thông tin tài khoản người chơi.");
                }

                var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
                if (wallet == null)
                {
                    throw new KeyNotFoundException("Không tìm thấy thông tin ví của người chơi.");
                }

                if (wallet.GoldBalance + goldAmount < 0)
                {
                    throw new ArgumentException($"Số dư Vàng không thể âm sau khi trừ. Hiện tại: {wallet.GoldBalance}, yêu cầu trừ: {Math.Abs(goldAmount)}.");
                }

                if (wallet.GemBalance + gemAmount < 0)
                {
                    throw new ArgumentException($"Số dư Ngọc không thể âm sau khi trừ. Hiện tại: {wallet.GemBalance}, yêu cầu trừ: {Math.Abs(gemAmount)}.");
                }

                int oldGold = wallet.GoldBalance;
                int oldGem = wallet.GemBalance;

                wallet.GoldBalance += goldAmount;
                wallet.GemBalance += gemAmount;
                wallet.UpdatedAt = DateTime.UtcNow;

                var txn = new Transaction
                {
                    UserId = userId,
                    TransactionType = "AdminAdjustment",
                    AmountVnd = 0,
                    AmountGold = goldAmount,
                    AmountGem = gemAmount,
                    PaymentMethod = "ADMIN",
                    ReferenceId = $"ADJUST-{Guid.NewGuid().ToString()[..8].ToUpper()}",
                    Status = "Completed",
                    CreatedAt = DateTime.UtcNow
                };

                _context.Transactions.Add(txn);
                await _context.SaveChangesAsync();

                await _adminLogService.LogActionAsync(
                    adminUsername,
                    "AdjustPlayerBalance",
                    $"Điều chỉnh số dư của '{profile.DisplayName}' ({userId}). Vàng: {oldGold} -> {wallet.GoldBalance} ({goldAmount:+;-;0}), Ngọc: {oldGem} -> {wallet.GemBalance} ({gemAmount:+;-;0}). Lý do: {reason}."
                );

                await transaction.CommitAsync();
                return profile;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<Profile> AwardPlayerXpAsync(Guid userId, int xpAmount, string adminUsername)
        {
            var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Id == userId);
            if (profile == null)
            {
                throw new KeyNotFoundException("Không tìm thấy thông tin tài khoản người chơi.");
            }

            int oldLevel = profile.Level;
            int oldXp = profile.Experience;
            profile.Experience += xpAmount;

            // Auto level up loop
            while (profile.Experience >= profile.Level * 1000)
            {
                profile.Experience -= profile.Level * 1000;
                profile.Level += 1;
            }

            await _context.SaveChangesAsync();

            string detailMsg = $"Tặng {xpAmount} XP cho '{profile.DisplayName}' ({userId}). ";
            if (profile.Level > oldLevel)
            {
                detailMsg += $"Thăng cấp! Cấp độ: {oldLevel} -> {profile.Level}. XP còn lại: {profile.Experience}.";
            }
            else
            {
                detailMsg += $"Cấp độ giữ nguyên: {profile.Level}. XP: {oldXp} -> {profile.Experience}.";
            }

            await _adminLogService.LogActionAsync(
                adminUsername,
                "AwardPlayerXp",
                detailMsg
            );

            return profile;
        }

        public async Task<GameItem> CreateShopItemAsync(GameItem item, string adminUsername)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                throw new ArgumentException("Mã vật phẩm không được để trống.");
            }

            var existing = await _context.GameItems.AnyAsync(i => i.Id == item.Id);
            if (existing)
            {
                throw new InvalidOperationException($"Vật phẩm với mã '{item.Id}' đã tồn tại trong hệ thống.");
            }

            // Kiểm tra tính hợp lệ của chuỗi Attributes nếu có
            if (!string.IsNullOrEmpty(item.Attributes))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(item.Attributes);
                }
                catch (System.Text.Json.JsonException)
                {
                    throw new ArgumentException("Chuỗi Attributes không phải là JSON hợp lệ.");
                }
            }

            item.CreatedAt = DateTime.UtcNow;
            _context.GameItems.Add(item);
            await _context.SaveChangesAsync();

            await _adminLogService.LogActionAsync(
                adminUsername,
                "CreateShopItem",
                $"Tạo mới vật phẩm '{item.Id}' ({item.Name}). Loại: {item.ItemType}, Giá Gold: {item.PriceGold}, Giá Gem: {item.PriceGem}, Giá VND: {item.PriceVnd}, Thuộc tính: {item.Attributes ?? "N/A"}."
            );

            return item;
        }

        public async Task DeleteShopItemAsync(string itemId, string adminUsername)
        {
            var item = await _context.GameItems.FirstOrDefaultAsync(i => i.Id == itemId);
            if (item == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy vật phẩm có mã '{itemId}'.");
            }

            _context.GameItems.Remove(item);
            await _context.SaveChangesAsync();

            await _adminLogService.LogActionAsync(
                adminUsername,
                "DeleteShopItem",
                $"Xóa bỏ vật phẩm '{itemId}' ({item.Name}) khỏi hệ thống."
            );
        }
    }
}
