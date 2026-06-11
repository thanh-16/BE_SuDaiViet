using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sử_Đại_Việt.Models
{
    [Table("player_broadcast_claims", Schema = "public")]
    public class PlayerBroadcastClaim
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("user_id")]
        public Guid UserId { get; set; }

        [ForeignKey("UserId")]
        public Profile? PlayerProfile { get; set; }

        [Required]
        [Column("mail_id")]
        public long MailId { get; set; }

        [ForeignKey("MailId")]
        public MailboxItem? MailboxItem { get; set; }

        [Required]
        [Column("is_read")]
        public bool IsRead { get; set; } = true;

        [Required]
        [Column("is_claimed")]
        public bool IsClaimed { get; set; } = false;

        [Column("claimed_at")]
        public DateTime ClaimedAt { get; set; } = DateTime.UtcNow;
    }
}
