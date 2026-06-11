using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sử_Đại_Việt.Models
{
    [Table("transactions", Schema = "public")]
    public class Transaction
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("user_id")]
        public Guid UserId { get; set; }

        [ForeignKey("UserId")]
        public Profile? PlayerProfile { get; set; } // Liên kết thông tin người thực hiện giao dịch

        [Required]
        [Column("transaction_type")]
        [StringLength(30)]
        public string TransactionType { get; set; } = "Topup"; // Topup (Nạp tiền), Purchase (Mua đồ)...

        [Required]
        [Column("amount_vnd")]
        public int AmountVnd { get; set; } = 0; // Số tiền mặt VND (nếu là nạp tiền)

        [Required]
        [Column("amount_gold")]
        public int AmountGold { get; set; } = 0; // Biến động số dư Vàng (Xu) trong game (+/-)

        [Required]
        [Column("amount_gem")]
        public int AmountGem { get; set; } = 0; // Biến động số dư Ngọc (Kích Ngọc) trong game (+/-)

        [Column("payment_method")]
        [StringLength(50)]
        public string? PaymentMethod { get; set; } // Momo, Banking, Card...

        [Column("reference_id")]
        [StringLength(100)]
        public string? ReferenceId { get; set; } // Mã đối chiếu giao dịch ngoài (Momo TxnId...)

        [Required]
        [Column("status")]
        [StringLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Completed, Failed...

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
