using System;
using System.ComponentModel.DataAnnotations;

namespace Sử_Đại_Việt.Dtos
{
    public class CreateMailDto
    {
        public Guid? ReceiverId { get; set; } // Nullable đại diện cho thư toàn server

        [Required(ErrorMessage = "Tiêu đề thư không được để trống")]
        [StringLength(150, ErrorMessage = "Tiêu đề không được vượt quá 150 ký tự")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nội dung thư không được để trống")]
        public string Content { get; set; } = string.Empty;

        public string? AttachmentsJson { get; set; } // JSON: {"gold": 1000, "gems": 50, "items": [{"id": "pot_hp_01", "qty": 2}]}

        [Range(1, 365, ErrorMessage = "Thời hạn thư tối thiểu 1 ngày và tối đa 365 ngày")]
        public int DurationDays { get; set; } = 30; // Mặc định hết hạn sau 30 ngày
    }
}
