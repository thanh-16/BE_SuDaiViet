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

        public async Task<Transaction> BuyItemAsync(Guid userId, string itemId, string currency)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
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

                // 2. Kiểm tra vật phẩm tồn tại
                var item = await _context.GameItems.FirstOrDefaultAsync(i => i.Id == itemId);
                if (item == null)
                {
                    throw new KeyNotFoundException($"Không tìm thấy vật phẩm có mã '{itemId}'.");
                }

                int deductGold = 0;
                int deductGem = 0;

                // 3. Khấu trừ số dư dựa theo đơn vị tiền tệ yêu cầu
                if (currency.Equals("Gold", StringComparison.OrdinalIgnoreCase))
                {
                    if (item.PriceGold <= 0)
                    {
                        throw new InvalidOperationException("Vật phẩm này không bán bằng Vàng.");
                    }
                    if (profile.GoldBalance < item.PriceGold)
                    {
                        throw new InvalidOperationException($"Số dư Vàng không đủ để mua vật phẩm này (Thiếu {item.PriceGold - profile.GoldBalance} Vàng).");
                    }
                    profile.GoldBalance -= item.PriceGold;
                    deductGold = -item.PriceGold;
                }
                else if (currency.Equals("Gem", StringComparison.OrdinalIgnoreCase))
                {
                    if (item.PriceGem <= 0)
                    {
                        throw new InvalidOperationException("Vật phẩm này không bán bằng Ngọc.");
                    }
                    if (profile.GemBalance < item.PriceGem)
                    {
                        throw new InvalidOperationException($"Số dư Ngọc không đủ để mua vật phẩm này (Thiếu {item.PriceGem - profile.GemBalance} Ngọc).");
                    }
                    profile.GemBalance -= item.PriceGem;
                    deductGem = -item.PriceGem;
                }
                else
                {
                    throw new ArgumentException("Đơn vị tiền tệ thanh toán không hợp lệ. Chỉ chấp nhận Gold hoặc Gem.");
                }

                // 4. Cập nhật kho đồ (UPSERT)
                var inventoryItem = await _context.PlayerInventories
                    .FirstOrDefaultAsync(pi => pi.UserId == userId && pi.ItemId == itemId);

                if (inventoryItem != null)
                {
                    inventoryItem.Quantity += 1;
                    inventoryItem.AcquiredAt = DateTime.UtcNow;
                }
                else
                {
                    var newInventory = new PlayerInventory
                    {
                        UserId = userId,
                        ItemId = itemId,
                        Quantity = 1,
                        AcquiredAt = DateTime.UtcNow
                    };
                    _context.PlayerInventories.Add(newInventory);
                }

                // 5. Ghi nhận giao dịch
                var txn = new Transaction
                {
                    UserId = userId,
                    TransactionType = "Purchase",
                    AmountVnd = 0,
                    AmountGold = deductGold,
                    AmountGem = deductGem,
                    PaymentMethod = currency.ToUpper(),
                    ReferenceId = $"BUY-{itemId.ToUpper()}-{Guid.NewGuid().ToString()[..8].ToUpper()}",
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

        public async Task<Transaction> ProcessTopupAsync(Guid userId, int amountVnd, string paymentMethod, string? referenceId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
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

                // 2. Quy đổi VNĐ sang Vàng và Ngọc (10,000 VND = 1,000 Gold và 100 Gems)
                // Công thức: Gold = VND / 10, Gem = VND / 100
                int creditGold = amountVnd / 10;
                int creditGem = amountVnd / 100;

                profile.GoldBalance += creditGold;
                profile.GemBalance += creditGem;

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
            var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Id == userId);
            if (profile == null)
            {
                throw new KeyNotFoundException("Không tìm thấy thông tin tài khoản người chơi.");
            }

            if (profile.GoldBalance + goldAmount < 0)
            {
                throw new ArgumentException($"Số dư Vàng không thể âm sau khi trừ. Hiện tại: {profile.GoldBalance}, yêu cầu trừ: {Math.Abs(goldAmount)}.");
            }

            if (profile.GemBalance + gemAmount < 0)
            {
                throw new ArgumentException($"Số dư Ngọc không thể âm sau khi trừ. Hiện tại: {profile.GemBalance}, yêu cầu trừ: {Math.Abs(gemAmount)}.");
            }

            int oldGold = profile.GoldBalance;
            int oldGem = profile.GemBalance;

            profile.GoldBalance += goldAmount;
            profile.GemBalance += gemAmount;

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
                $"Điều chỉnh số dư của '{profile.DisplayName}' ({userId}). Vàng: {oldGold} -> {profile.GoldBalance} ({goldAmount:+;-;0}), Ngọc: {oldGem} -> {profile.GemBalance} ({gemAmount:+;-;0}). Lý do: {reason}."
            );

            return profile;
        }
    }
}
