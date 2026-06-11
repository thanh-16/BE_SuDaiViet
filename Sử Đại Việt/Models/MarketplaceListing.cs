using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sử_Đại_Việt.Models
{
    [Table("marketplace_listings", Schema = "public")]
    public class MarketplaceListing
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("seller_id")]
        public Guid SellerId { get; set; }

        [ForeignKey("SellerId")]
        public Profile? SellerProfile { get; set; }

        [Required]
        [Column("inventory_item_id")]
        public long InventoryItemId { get; set; }

        [ForeignKey("InventoryItemId")]
        public PlayerInventory? InventoryItem { get; set; }

        [Required]
        [Column("price_gold")]
        public int PriceGold { get; set; } = 0;

        [Required]
        [Column("price_gem")]
        public int PriceGem { get; set; } = 0;

        [Required]
        [Column("listing_fee")]
        public int ListingFee { get; set; } = 0;

        [Required]
        [Column("status")]
        [StringLength(20)]
        public string Status { get; set; } = "Active"; // 'Active', 'Sold', 'Cancelled'

        [Column("buyer_id")]
        public Guid? BuyerId { get; set; }

        [ForeignKey("BuyerId")]
        public Profile? BuyerProfile { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [Column("deleted_at")]
        public DateTime? DeletedAt { get; set; }
    }
}
