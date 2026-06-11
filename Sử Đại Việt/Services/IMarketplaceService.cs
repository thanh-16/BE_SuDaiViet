using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public interface IMarketplaceService
    {
        Task<IEnumerable<MarketplaceListing>> GetActiveListingsAsync(string? search, int pageIndex, int pageSize);
        Task<int> GetActiveListingsCountAsync(string? search);
        Task<MarketplaceListing> ListItemAsync(Guid sellerId, long inventoryItemId, int goldPrice, int gemPrice);
        Task<Transaction> BuyMarketplaceItemAsync(Guid buyerId, long listingId);
        Task CancelListingAsync(Guid sellerId, long listingId);
        Task<MarketplaceReservation> ReserveListingAsync(Guid userId, long listingId);
        Task ReleaseReservationAsync(long listingId);
    }
}
