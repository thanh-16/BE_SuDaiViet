using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sử_Đại_Việt.Models
{
    [Table("marketplace_reservations", Schema = "public")]
    public class MarketplaceReservation
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("listing_id")]
        public long ListingId { get; set; }

        [ForeignKey("ListingId")]
        public MarketplaceListing? Listing { get; set; }

        [Required]
        [Column("user_id")]
        public Guid UserId { get; set; }

        [ForeignKey("UserId")]
        public Profile? Profile { get; set; }

        [Required]
        [Column("reserved_at")]
        public DateTime ReservedAt { get; set; } = DateTime.UtcNow;
    }
}
