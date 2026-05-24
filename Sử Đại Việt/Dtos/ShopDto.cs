using System.ComponentModel.DataAnnotations;

namespace Sử_Đại_Việt.Dtos
{
    public class PurchaseItemDto
    {
        [Required(ErrorMessage = "Mã vật phẩm không được để trống")]
        public string ItemId { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phương thức thanh toán bằng đơn vị tiền tệ không được để trống")]
        [RegularExpression("(?i)^(Gold|Gem)$", ErrorMessage = "Đơn vị tiền tệ chỉ chấp nhận Gold hoặc Gem")]
        public string Currency { get; set; } = "Gold"; // "Gold" hoặc "Gem"
    }

    public class MockTopupDto
    {
        [Required(ErrorMessage = "Số tiền nạp không được để trống")]
        [Range(1000, 100000000, ErrorMessage = "Số tiền nạp tối thiểu là 1,000 VND và tối đa là 100,000,000 VND")]
        public int AmountVnd { get; set; }

        [Required(ErrorMessage = "Phương thức thanh toán không được để trống")]
        [StringLength(50)]
        public string PaymentMethod { get; set; } = "Momo"; // Momo, Banking, Card...

        [StringLength(100)]
        public string? ReferenceId { get; set; } // Mã đối chiếu giao dịch ngoài
    }

    public class UpdateItemPriceDto
    {
        [Range(0, 1000000000, ErrorMessage = "Giá trị Gold không hợp lệ")]
        public int PriceGold { get; set; }

        [Range(0, 1000000000, ErrorMessage = "Giá trị Gem không hợp lệ")]
        public int PriceGem { get; set; }

        [Range(0, 1000000000, ErrorMessage = "Giá trị VND không hợp lệ")]
        public int PriceVnd { get; set; }
    }

    public class AdjustBalanceDto
    {
        public int GoldAmount { get; set; } // Hỗ trợ số âm để trừ, số dương để cộng
        public int GemAmount { get; set; }  // Hỗ trợ số âm để trừ, số dương để cộng

        [Required(ErrorMessage = "Phải cung cấp lý do điều chỉnh")]
        [StringLength(250, ErrorMessage = "Lý do không được vượt quá 250 ký tự")]
        public string Reason { get; set; } = string.Empty;
    }
}
