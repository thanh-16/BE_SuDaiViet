using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Sử_Đại_Việt.Dtos
{
    public class PlayerAdminDto
    {
        public Guid Id { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string DisplayName { get; set; } = "Nghĩa Sĩ";
        public string Username { get; set; } = "Nghĩa Sĩ";
        public string? AvatarUrl { get; set; }
        public bool IsBanned { get; set; }
        public string Role { get; set; } = "player";
        public int Level { get; set; } = 1;
        public int Experience { get; set; } = 0;
        public int GoldBalance { get; set; } = 0;
        public int GemBalance { get; set; } = 0;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class AdminLogDto
    {
        public long Id { get; set; }
        public string AdminUsername { get; set; } = string.Empty;
        public string ActionName { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string ActionDetails { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class DatabaseInitResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> ExecutedSteps { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }
}
