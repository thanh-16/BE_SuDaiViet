using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sử_Đại_Việt.Data;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public class MarketplaceService : IMarketplaceService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAdminLogService _adminLogService;

        public MarketplaceService(ApplicationDbContext context, IAdminLogService adminLogService)
        {
            _context = context;
            _adminLogService = adminLogService;
        }

        public async Task<IEnumerable<MarketplaceListing>> GetActiveListingsAsync(string? search, int pageIndex, int pageSize)
        {
            var query = _context.MarketplaceListings
                .Include(ml => ml.SellerProfile)
                .Include(ml => ml.InventoryItem)
                .ThenInclude(pi => pi!.ItemDetails)
                .Where(ml => ml.Status == "Active")
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(ml => 
                    ml.InventoryItem != null && ml.InventoryItem.ItemDetails != null &&
                    ((ml.InventoryItem.ItemDetails.Name != null && ml.InventoryItem.ItemDetails.Name.ToLower().Contains(lowerSearch)) ||
                     (ml.InventoryItem.ItemDetails.Id != null && ml.InventoryItem.ItemDetails.Id.ToLower().Contains(lowerSearch)) ||
                     (ml.SellerProfile != null && ml.SellerProfile.DisplayName != null && ml.SellerProfile.DisplayName.ToLower().Contains(lowerSearch)))
                );
            }

            return await query
                .OrderByDescending(ml => ml.CreatedAt)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> GetActiveListingsCountAsync(string? search)
        {
            var query = _context.MarketplaceListings
                .Where(ml => ml.Status == "Active");

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(ml => 
                    ml.InventoryItem != null && ml.InventoryItem.ItemDetails != null &&
                    (ml.InventoryItem.ItemDetails.Name.ToLower().Contains(lowerSearch) ||
                     ml.InventoryItem.ItemDetails.Id.ToLower().Contains(lowerSearch))
                );
            }

            return await query.CountAsync();
        }

        public async Task<MarketplaceListing> ListItemAsync(Guid sellerId, long inventoryItemId, int goldPrice, int gemPrice)
        {
            if (goldPrice < 0 || gemPrice < 0)
            {
                throw new ArgumentException("Giá bán không thể là số âm.");
            }

            if (goldPrice == 0 && gemPrice == 0)
            {
                throw new ArgumentException("Giá bán bắt buộc phải lớn hơn 0 Vàng hoặc 0 Ngọc.");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Khóa ví wallets tránh race condition phí niêm yết
                await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM public.wallets WHERE user_id = {sellerId} FOR UPDATE");

                // 1. Kiểm tra vật phẩm trong túi đồ của người bán
                var inventory = await _context.PlayerInventories
                    .Include(pi => pi!.ItemDetails)
                    .FirstOrDefaultAsync(pi => pi.Id == inventoryItemId && pi.UserId == sellerId);

                if (inventory == null || inventory.ItemDetails == null)
                {
                    throw new KeyNotFoundException("Không tìm thấy vật phẩm trong túi đồ của bạn.");
                }

                if (inventory.Quantity < 1)
                {
                    throw new InvalidOperationException("Vật phẩm đã được bán hoặc sử dụng hết trong túi đồ.");
                }

                // 2. Chặn đăng bán nếu trang bị đang được mặc trên tướng
                var isEquipped = await _context.HeroEquipments.AnyAsync(he => he.InventoryItemId == inventoryItemId);
                if (isEquipped)
                {
                    throw new InvalidOperationException("Món đồ này đang được trang bị trên tướng, hãy tháo ra trước khi đăng bán.");
                }

                // 3. Khấu trừ 1 món đồ từ túi của người bán (khóa tạm thời)
                inventory.Quantity -= 1;
                // Lưu ý: Không xóa record rương của người bán ngay kể cả khi qty = 0 vì FK của bảng listings sẽ tham chiếu tới Id rương này

                // 4. Tạo bài đăng bán chợ
                var listing = new MarketplaceListing
                {
                    SellerId = sellerId,
                    InventoryItemId = inventoryItemId,
                    PriceGold = goldPrice,
                    PriceGem = gemPrice,
                    ListingFee = 10, // Thu phí tượng trưng 10 vàng để tránh spam đăng bài
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow
                };

                // Phạt nhẹ 10 vàng phí niêm yết trên ví người bán
                var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == sellerId);
                if (wallet != null && wallet.GoldBalance >= 10)
                {
                    wallet.GoldBalance -= 10;
                    wallet.UpdatedAt = DateTime.UtcNow;
                }

                _context.MarketplaceListings.Add(listing);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return listing;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<Transaction> BuyMarketplaceItemAsync(Guid buyerId, long listingId)
        {
            var resultList = await _context.Database
                .SqlQueryRaw<string>("SELECT public.buy_marketplace_item({0}, {1}) as \"Value\"", buyerId, listingId)
                .ToListAsync();

            var result = resultList.FirstOrDefault();

            if (result != null && result.StartsWith("ERROR:"))
            {
                throw new InvalidOperationException(result.Substring(6).Trim());
            }

            // Stored Procedure tự động cập nhật rương và thêm dòng transaction, ta tải lại transaction mới nhất để trả về
            var txn = await _context.Transactions
                .Where(t => t.UserId == buyerId && t.TransactionType == "MarketplacePurchase")
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (txn == null)
            {
                throw new InvalidOperationException("Không tìm thấy giao dịch mua hàng trên chợ.");
            }

            return txn;
        }

        public async Task CancelListingAsync(Guid sellerId, long listingId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var listing = await _context.MarketplaceListings
                    .Include(ml => ml.InventoryItem)
                    .FirstOrDefaultAsync(ml => ml.Id == listingId);

                if (listing == null || listing.SellerId != sellerId)
                {
                    throw new KeyNotFoundException("Không tìm thấy thông tin bài đăng chợ của nghĩa sĩ.");
                }

                if (listing.Status != "Active" && listing.Status != "Reserved")
                {
                    throw new InvalidOperationException("Bài đăng bán này đã hoàn thành hoặc đã bị hủy trước đó.");
                }

                // 1. Trả lại vật phẩm vào túi đồ của người bán
                if (listing.InventoryItem != null)
                {
                    listing.InventoryItem.Quantity += 1;
                    listing.InventoryItem.DeletedAt = null;
                }

                // 2. Đổi trạng thái bài đăng sang Cancelled
                listing.Status = "Cancelled";
                listing.UpdatedAt = DateTime.UtcNow;

                // Xóa bất kỳ giữ chỗ nào nếu có
                var reservation = await _context.MarketplaceReservations.FirstOrDefaultAsync(r => r.ListingId == listingId);
                if (reservation != null)
                {
                    _context.MarketplaceReservations.Remove(reservation);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<MarketplaceReservation> ReserveListingAsync(Guid userId, long listingId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var listing = await _context.MarketplaceListings.FirstOrDefaultAsync(ml => ml.Id == listingId);
                if (listing == null)
                {
                    throw new KeyNotFoundException("Không tìm thấy thông tin bài đăng chợ.");
                }

                if (listing.Status != "Active")
                {
                    throw new InvalidOperationException("Bài đăng chợ không còn hoạt động để đặt chỗ.");
                }

                if (listing.SellerId == userId)
                {
                    throw new InvalidOperationException("Không thể tự đặt chỗ bài đăng của chính mình.");
                }

                // Kiểm tra xem đã có đặt chỗ chưa
                var existingRes = await _context.MarketplaceReservations.FirstOrDefaultAsync(r => r.ListingId == listingId);
                if (existingRes != null)
                {
                    // Nếu quá hạn 5 phút, giải phóng đặt cũ
                    if ((DateTime.UtcNow - existingRes.ReservedAt).TotalMinutes >= 5)
                    {
                        _context.MarketplaceReservations.Remove(existingRes);
                    }
                    else
                    {
                        throw new InvalidOperationException("Vật phẩm này đã được đặt chỗ giữ hàng bởi một người chơi khác.");
                    }
                }

                // Tạo đặt chỗ mới
                var reservation = new MarketplaceReservation
                {
                    ListingId = listingId,
                    UserId = userId,
                    ReservedAt = DateTime.UtcNow
                };

                listing.Status = "Reserved";
                listing.UpdatedAt = DateTime.UtcNow;

                _context.MarketplaceReservations.Add(reservation);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return reservation;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task ReleaseReservationAsync(long listingId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var listing = await _context.MarketplaceListings.FirstOrDefaultAsync(ml => ml.Id == listingId);
                var reservation = await _context.MarketplaceReservations.FirstOrDefaultAsync(r => r.ListingId == listingId);

                if (reservation != null)
                {
                    _context.MarketplaceReservations.Remove(reservation);
                }

                if (listing != null && listing.Status == "Reserved")
                {
                    listing.Status = "Active";
                    listing.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
