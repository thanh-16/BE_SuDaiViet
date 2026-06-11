using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sử_Đại_Việt.Models
{
    [Table("player_inventories", Schema = "public")]
    public class PlayerInventory
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("user_id")]
        public Guid UserId { get; set; }

        [ForeignKey("UserId")]
        public Profile? PlayerProfile { get; set; } // Liên kết thông tin người chơi

        [Required]
        [Column("item_id")]
        [StringLength(50)]
        public string ItemId { get; set; } = string.Empty;

        [ForeignKey("ItemId")]
        public GameItem? ItemDetails { get; set; } // Liên kết thông tin vật phẩm chi tiết

        [Required]
        [Column("quantity")]
        public int Quantity { get; set; } = 1; // Số lượng vật phẩm sở hữu trong kho đồ

        [Column("acquired_at")]
        public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [Column("deleted_at")]
        public DateTime? DeletedAt { get; set; }
    }
}
