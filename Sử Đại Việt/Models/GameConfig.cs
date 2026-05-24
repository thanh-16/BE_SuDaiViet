using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sử_Đại_Việt.Models
{
    [Table("game_config", Schema = "public")]
    public class GameConfig
    {
        [Key]
        [Column("config_key")]
        [Required]
        [StringLength(100)]
        public string ConfigKey { get; set; } = string.Empty;

        [Column("config_value")]
        [Required]
        public decimal ConfigValue { get; set; }

        [Column("description")]
        public string? Description { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
