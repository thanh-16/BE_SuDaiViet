using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sử_Đại_Việt.Models
{
    [Table("profiles", Schema = "public")]
    public class Profile
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; }

        [Column("email")]
        [StringLength(255)]
        public string? Email { get; set; } // Nullable để hỗ trợ Đăng nhập bằng Số Điện Thoại hoặc Ẩn danh

        [Column("phone")]
        [StringLength(50)]
        public string? Phone { get; set; } // Hỗ trợ Đăng nhập bằng Số Điện Thoại

        [Column("display_name")]
        [StringLength(50)]
        public string DisplayName { get; set; } = "Nghĩa Sĩ";

        [Column("avatar_url")]
        [StringLength(500)]
        public string? AvatarUrl { get; set; } // Hỗ trợ Avatar từ Google, Facebook, Apple...

        [Required]
        [Column("is_banned")]
        public bool IsBanned { get; set; } = false; // Trạng thái khóa tài khoản người chơi

        [Required]
        [Column("role")]
        [StringLength(20)]
        public string Role { get; set; } = "player"; // Vai trò trong game: player hoặc admin

        [Required]
        [Column("gold_balance")]
        public int GoldBalance { get; set; } = 0; // Số dư Vàng

        [Required]
        [Column("gem_balance")]
        public int GemBalance { get; set; } = 0; // Số dư Ngọc (KNB)

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
