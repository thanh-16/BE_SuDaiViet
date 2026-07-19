using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public interface ILeaderboardService
    {
        Task<IEnumerable<Leaderboard>> GetTopScoresAsync(int limit, CancellationToken cancellationToken = default);
        Task<Leaderboard?> SubmitScoreAsync(Guid userId, string username, int score, string stageReached, CancellationToken cancellationToken = default);
        Task<bool> DeleteScoreAsync(long id, CancellationToken cancellationToken = default);
        Task<Leaderboard?> UpdateScoreAsync(long id, int score, string stageReached, CancellationToken cancellationToken = default);
    }
}
