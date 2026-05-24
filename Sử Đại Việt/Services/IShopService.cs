using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public interface IShopService
    {
        /// <summary>
        /// Lấy toàn bộ danh sách vật phẩm trong cửa hàng.
        /// </summary>
        Task<IEnumerable<GameItem>> GetShopItemsAsync();

        /// <summary>
        /// Lấy kho đồ cá nhân của người chơi (kèm thông tin chi tiết vật phẩm).
        /// </summary>
        Task<IEnumerable<PlayerInventory>> GetPlayerInventoryAsync(Guid userId);

        /// <summary>
        /// Xử lý mua vật phẩm bằng Vàng hoặc Ngọc.
        /// </summary>
        Task<Transaction> BuyItemAsync(Guid userId, string itemId, string currency);

        /// <summary>
        /// Giả lập nạp tiền mặt (VND) để quy đổi ra Vàng và Ngọc trong game.
        /// </summary>
        Task<Transaction> ProcessTopupAsync(Guid userId, int amountVnd, string paymentMethod, string? referenceId);

        /// <summary>
        /// [Admin] Lấy danh sách giao dịch phân trang.
        /// </summary>
        Task<IEnumerable<Transaction>> GetTransactionsAsync(string? search, int pageIndex, int pageSize);

        /// <summary>
        /// [Admin] Đếm tổng số giao dịch để phân trang.
        /// </summary>
        Task<int> GetTransactionsCountAsync(string? search);

        /// <summary>
        /// [Admin] Cập nhật giá vật phẩm trong shop.
        /// </summary>
        Task<GameItem> UpdateItemPriceAsync(string itemId, int priceGold, int priceGem, int priceVnd, string adminUsername);

        /// <summary>
        /// [Admin] Tăng/giảm số dư Vàng/Ngọc của người chơi.
        /// </summary>
        Task<Profile> AdjustPlayerBalanceAsync(Guid userId, int goldAmount, int gemAmount, string reason, string adminUsername);
    }
}
