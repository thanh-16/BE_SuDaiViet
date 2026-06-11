using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sử_Đại_Việt.Models
{
    [Table("mailbox", Schema = "public")]
    public class MailboxItem
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("receiver_id")]
        public Guid? ReceiverId { get; set; } // Nullable đại diện cho Broadcast Mail

        [ForeignKey("ReceiverId")]
        public Profile? ReceiverProfile { get; set; }

        [Required]
        [Column("title")]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [Column("content")]
        public string Content { get; set; } = string.Empty;

        [Column("attachments", TypeName = "jsonb")]
        public string? Attachments { get; set; } // {"gold": 1000, "gems": 50, "items": [{"id": "pot_hp_01", "qty": 2}]}

        [Required]
        [Column("is_read")]
        public bool IsRead { get; set; } = false;

        [Required]
        [Column("is_claimed")]
        public bool IsClaimed { get; set; } = false;

        [Column("expired_at")]
        public DateTime? ExpiredAt { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [Column("deleted_at")]
        public DateTime? DeletedAt { get; set; }
    }
}
