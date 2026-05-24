using System;
using System.ComponentModel.DataAnnotations;

namespace Sử_Đại_Việt.Dtos
{
    public class SubmitScoreDto
    {
        [Required(ErrorMessage = "Mã định danh người chơi (UserId) là bắt buộc.")]
        public Guid UserId { get; set; }

        [Required(ErrorMessage = "Tên hiển thị người chơi không được để trống.")]
        [StringLength(50, ErrorMessage = "Tên hiển thị người chơi không được vượt quá 50 ký tự.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Điểm số là bắt buộc.")]
        [Range(0, 99999999, ErrorMessage = "Điểm số lập chiến công phải nằm trong khoảng từ 0 đến 99,999,999.")]
        public int Score { get; set; }

        [Required(ErrorMessage = "Thông tin ải vượt qua không được để trống.")]
        [StringLength(50, ErrorMessage = "Tên ải vượt qua không được vượt quá 50 ký tự.")]
        public string StageReached { get; set; } = "Ải 1";
    }

    public class AdminUpdateScoreDto
    {
        [Required(ErrorMessage = "Điểm số là bắt buộc.")]
        [Range(0, 99999999, ErrorMessage = "Điểm số lập chiến công phải nằm trong khoảng từ 0 đến 99,999,999.")]
        public int Score { get; set; }

        [Required(ErrorMessage = "Thông tin ải vượt qua không được để trống.")]
        [StringLength(50, ErrorMessage = "Tên ải vượt qua không được vượt quá 50 ký tự.")]
        public string StageReached { get; set; } = "Ải 1";
    }
}
