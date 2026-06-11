using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sử_Đại_Việt.Models
{
    [Table("hero_equipments", Schema = "public")]
    public class HeroEquipment
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("player_hero_id")]
        public long PlayerHeroId { get; set; }

        [ForeignKey("PlayerHeroId")]
        public PlayerHero? HeroDetails { get; set; }

        [Required]
        [Column("inventory_item_id")]
        public long InventoryItemId { get; set; }

        [ForeignKey("InventoryItemId")]
        public PlayerInventory? InventoryItem { get; set; }

        [Required]
        [Column("slot_type")]
        [StringLength(20)]
        public string SlotType { get; set; } = string.Empty; // 'Weapon', 'Armor', 'Helmet', 'Ring'

        [Column("equipped_at")]
        public DateTime EquippedAt { get; set; } = DateTime.UtcNow;

        [Column("deleted_at")]
        public DateTime? DeletedAt { get; set; }
    }
}
