using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sử_Đại_Việt.Models
{
    [Table("admin_logs", Schema = "public")]
    public class AdminLog
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("admin_username")]
        [StringLength(100)]
        public string AdminUsername { get; set; } = string.Empty;

        [Required]
        [Column("action_name")]
        [StringLength(100)]
        public string ActionName { get; set; } = string.Empty;

        [Required]
        [Column("action_details")]
        public string ActionDetails { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
