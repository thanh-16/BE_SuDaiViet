using System.ComponentModel.DataAnnotations;

namespace Sử_Đại_Việt.Dtos
{
    public class ListMarketplaceItemDto
    {
        [Required(ErrorMessage = "Mã vật phẩm trong kho đồ không được để trống")]
        public long InventoryItemId { get; set; }

        [Range(0, 1000000000, ErrorMessage = "Giá bán Vàng không hợp lệ")]
        public int PriceGold { get; set; }

        [Range(0, 1000000000, ErrorMessage = "Giá bán Ngọc không hợp lệ")]
        public int PriceGem { get; set; }
    }
}
