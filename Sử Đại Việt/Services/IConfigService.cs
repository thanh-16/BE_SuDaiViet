using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public interface IConfigService
    {
        Task<IEnumerable<GameConfig>> GetAllConfigsAsync(CancellationToken cancellationToken = default);
        Task<GameConfig?> UpdateConfigAsync(string key, decimal value, string? description = null, CancellationToken cancellationToken = default);
        Task<bool> DeleteConfigAsync(string key, CancellationToken cancellationToken = default);
    }
}
