using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sử_Đại_Việt.Models
{
    [Table("wallets", Schema = "public")]
    public class Wallet
    {
        [Key]
        [Column("user_id")]
        public Guid UserId { get; set; }

        [ForeignKey("UserId")]
        public Profile? Profile { get; set; }

        [Required]
        [Column("gold_balance")]
        public int GoldBalance { get; set; } = 0;

        [Required]
        [Column("gem_balance")]
        public int GemBalance { get; set; } = 0;

        [Required]
        [ConcurrencyCheck]
        [Column("version")]
        public long Version { get; set; } = 1;

        [Required]
        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
