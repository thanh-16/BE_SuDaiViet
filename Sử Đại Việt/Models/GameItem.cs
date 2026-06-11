using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sử_Đại_Việt.Models
{
    [Table("game_items", Schema = "public")]
    public class GameItem
    {
        [Key]
        [Column("id")]
        [StringLength(50)]
        public string Id { get; set; } = string.Empty; // Mã vật phẩm (Ví dụ: pot_hp_01)

        [Required]
        [Column("name")]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty; // Tên vật phẩm

        [Column("description")]
        public string? Description { get; set; } // Mô tả chi tiết vật phẩm

        [Required]
        [Column("price_gold")]
        public int PriceGold { get; set; } = 0; // Giá mua bằng Vàng (Gold)

        [Required]
        [Column("price_gem")]
        public int PriceGem { get; set; } = 0; // Giá mua bằng Ngọc (Gem)

        [Required]
        [Column("price_vnd")]
        public int PriceVnd { get; set; } = 0; // Giá mua trực tiếp bằng VNĐ (cho Gói nạp/Ưu đãi)

        [Required]
        [Column("item_type")]
        [StringLength(30)]
        public string ItemType { get; set; } = "Consumable"; // Equipment, Consumable, Skin...

        [Column("attributes", TypeName = "jsonb")]
        public string? Attributes { get; set; } // Các chỉ số động khác (JSON: attack_boost, level_requirement...)

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
