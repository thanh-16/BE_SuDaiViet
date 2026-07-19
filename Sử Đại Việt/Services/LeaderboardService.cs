using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sử_Đại_Việt.Data;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public class LeaderboardService : ILeaderboardService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;
        private const string CacheKey = "Leaderboard_Top100";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

        public LeaderboardService(ApplicationDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // Tải danh sách Top Scores sử dụng Cache Top 100 tối ưu hóa
        public async Task<IEnumerable<Leaderboard>> GetTopScoresAsync(int limit, CancellationToken cancellationToken = default)
        {
            if (!_cache.TryGetValue(CacheKey, out List<Leaderboard>? top100) || top100 == null)
            {
                top100 = await _context.Leaderboards
                    .AsNoTracking()
                    .Include(l => l.Profile)
                    .OrderByDescending(l => l.Score)
                    .Take(100)
                    .ToListAsync(cancellationToken);
                _cache.Set(CacheKey, top100, CacheDuration);
            }

            return top100.Take(limit);
        }

        // Gửi điểm số mới. Nếu đã tồn tại điểm của user_id, cập nhật nếu điểm mới cao hơn (UPSERT)
        public async Task<Leaderboard?> SubmitScoreAsync(Guid userId, string username, int score, string stageReached, CancellationToken cancellationToken = default)
        {
            // Kiểm tra xem User có tồn tại trong bảng profiles không
            var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Id == userId, cancellationToken);
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
                .FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken);

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

            await _context.SaveChangesAsync(cancellationToken);
            _cache.Remove(CacheKey); // Invalidate Cache
            return resultRecord;
        }

        public async Task<bool> DeleteScoreAsync(long id, CancellationToken cancellationToken = default)
        {
            var record = await _context.Leaderboards.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
            if (record == null) return false;

            _context.Leaderboards.Remove(record);
            await _context.SaveChangesAsync(cancellationToken);
            _cache.Remove(CacheKey); // Invalidate Cache
            return true;
        }

        public async Task<Leaderboard?> UpdateScoreAsync(long id, int score, string stageReached, CancellationToken cancellationToken = default)
        {
            var record = await _context.Leaderboards.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
            if (record == null) return null;

            record.Score = score;
            record.StageReached = stageReached;
            record.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            _cache.Remove(CacheKey); // Invalidate Cache
            return record;
        }
    }
}
