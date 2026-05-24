using System.ComponentModel.DataAnnotations;

namespace Sử_Đại_Việt.Dtos
{
    public class UpdateConfigDto
    {
        [Required(ErrorMessage = "Tên chỉ số cấu hình (ConfigKey) là bắt buộc.")]
        [StringLength(100, ErrorMessage = "Tên chỉ số cấu hình không được vượt quá 100 ký tự.")]
        public string ConfigKey { get; set; } = string.Empty;

        [Required(ErrorMessage = "Giá trị chỉ số cấu hình (ConfigValue) là bắt buộc.")]
        public decimal ConfigValue { get; set; }

        [StringLength(200, ErrorMessage = "Mô tả chỉ số cấu hình không được vượt quá 200 ký tự.")]
        public string? Description { get; set; }
    }
}
