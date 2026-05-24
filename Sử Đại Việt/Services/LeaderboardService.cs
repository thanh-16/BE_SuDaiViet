using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sử_Đại_Việt.Data;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public class LeaderboardService : ILeaderboardService
    {
        private readonly ApplicationDbContext _context;

        public LeaderboardService(ApplicationDbContext context)
        {
            _context = context;
        }

        // Tải danh sách Top 10 hoặc theo số lượng giới hạn
        public async Task<IEnumerable<Leaderboard>> GetTopScoresAsync(int limit)
        {
            return await _context.Leaderboards
                .AsNoTracking()
                .Include(l => l.Profile)
                .OrderByDescending(l => l.Score)
                .Take(limit)
                .ToListAsync();
        }

        // Gửi điểm số mới. Nếu đã tồn tại điểm của user_id, cập nhật nếu điểm mới cao hơn (UPSERT)
        public async Task<Leaderboard?> SubmitScoreAsync(Guid userId, string username, int score, string stageReached)
        {
            // Kiểm tra xem User có tồn tại trong bảng profiles không
            var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Id == userId);
            string finalUsername = username;

            if (profile == null)
            {
                // Nếu chưa có profile (ví dụ trigger Supabase Auth chưa chạy kịp), tạo tạm profile
                var newProfile = new Profile
                {
                    Id = userId,
                    Email = username + "@sudaiviet.com",
                    DisplayName = username
                };
                _context.Profiles.Add(newProfile);
            }
            else
            {
                // Tối ưu bảo mật: Sử dụng tên hiển thị đồng bộ từ Google/Facebook đã được xác thực trong Profile
                finalUsername = profile.DisplayName;
            }

            var existingRecord = await _context.Leaderboards
                .FirstOrDefaultAsync(l => l.UserId == userId);

            Leaderboard resultRecord;

            if (existingRecord == null)
            {
                // Chưa từng ghi danh -> Tạo mới
                resultRecord = new Leaderboard
                {
                    UserId = userId,
                    Username = finalUsername,
                    Score = score,
                    StageReached = stageReached,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Leaderboards.Add(resultRecord);
            }
            else
            {
                // Đồng bộ tên hiển thị mới nhất nếu có sự thay đổi từ Profiles
                if (existingRecord.Username != finalUsername)
                {
                    existingRecord.Username = finalUsername;
                }

                // Đã có điểm -> Cập nhật nếu điểm mới cao hơn điểm cũ
                if (score > existingRecord.Score)
                {
                    existingRecord.Score = score;
                    existingRecord.StageReached = stageReached;
                    existingRecord.UpdatedAt = DateTime.UtcNow;
                }
                resultRecord = existingRecord;
            }

            // Tối ưu hóa hiệu năng: Chỉ gọi SaveChangesAsync đúng 1 lần duy nhất để lưu toàn bộ thay đổi trong 1 giao dịch (Transaction)
            await _context.SaveChangesAsync();
            return resultRecord;
        }

        public async Task<bool> DeleteScoreAsync(long id)
        {
            var record = await _context.Leaderboards.FindAsync(id);
            if (record == null) return false;

            _context.Leaderboards.Remove(record);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<Leaderboard?> UpdateScoreAsync(long id, int score, string stageReached)
        {
            var record = await _context.Leaderboards.FindAsync(id);
            if (record == null) return null;

            record.Score = score;
            record.StageReached = stageReached;
            record.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return record;
        }
    }
}
