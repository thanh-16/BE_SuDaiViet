using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public interface ILeaderboardService
    {
        Task<IEnumerable<Leaderboard>> GetTopScoresAsync(int limit);
        Task<Leaderboard?> SubmitScoreAsync(Guid userId, string username, int score, string stageReached);
        Task<bool> DeleteScoreAsync(long id);
        Task<Leaderboard?> UpdateScoreAsync(long id, int score, string stageReached);
    }
}
