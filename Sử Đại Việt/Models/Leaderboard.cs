using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sử_Đại_Việt.Models
{
    [Table("leaderboard", Schema = "public")]
    public class Leaderboard
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("user_id")]
        [Required]
        public Guid UserId { get; set; }

        [Column("username")]
        [Required]
        [StringLength(50)]
        public string Username { get; set; } = string.Empty;

        [Column("score")]
        public int Score { get; set; } = 0;

        [Column("stage_reached")]
        [Required]
        [StringLength(50)]
        public string StageReached { get; set; } = "Ải 1";

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Quan hệ khóa ngoại tới Profiles
        [ForeignKey(nameof(UserId))]
        public Profile? Profile { get; set; }
    }
}
