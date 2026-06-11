using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sử_Đại_Việt.Models
{
    [Table("player_heroes", Schema = "public")]
    public class PlayerHero
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
        [Column("hero_key")]
        [StringLength(50)]
        public string HeroKey { get; set; } = string.Empty; // 'hue', 'nhac', 'lu'

        [Required]
        [Column("level")]
        public int Level { get; set; } = 1;

        [Required]
        [Column("experience")]
        public int Experience { get; set; } = 0;

        [Required]
        [Column("skills_level", TypeName = "jsonb")]
        public string SkillsLevel { get; set; } = "{\"skill_active\": 1, \"skill_passive\": 1}";

        [Column("unlocked_at")]
        public DateTime UnlockedAt { get; set; } = DateTime.UtcNow;
    }
}
