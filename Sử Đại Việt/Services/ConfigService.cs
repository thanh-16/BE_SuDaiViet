using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sử_Đại_Việt.Data;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public class ConfigService : IConfigService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;
        private const string CacheKey = "GameConfigsCacheKey";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        public ConfigService(ApplicationDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        public async Task<IEnumerable<GameConfig>> GetAllConfigsAsync(CancellationToken cancellationToken = default)
        {
            if (!_cache.TryGetValue(CacheKey, out IEnumerable<GameConfig>? configs) || configs == null)
            {
                configs = await _context.GameConfigs.AsNoTracking().ToListAsync(cancellationToken);
                _cache.Set(CacheKey, configs, CacheDuration);
            }
            return configs;
        }

        public async Task<GameConfig?> UpdateConfigAsync(string key, decimal value, string? description = null, CancellationToken cancellationToken = default)
        {
            var config = await _context.GameConfigs.FirstOrDefaultAsync(c => c.ConfigKey == key, cancellationToken);
            if (config == null)
            {
                config = new GameConfig
                {
                    ConfigKey = key,
                    ConfigValue = value,
                    Description = description,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.GameConfigs.Add(config);
            }
            else
            {
                config.ConfigValue = value;
                config.UpdatedAt = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(description))
                {
                    config.Description = description;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            _cache.Remove(CacheKey); // Invalidate Cache
            return config;
        }

        public async Task<bool> DeleteConfigAsync(string key, CancellationToken cancellationToken = default)
        {
            var config = await _context.GameConfigs.FirstOrDefaultAsync(c => c.ConfigKey == key, cancellationToken);
            if (config == null) return false;

            _context.GameConfigs.Remove(config);
            await _context.SaveChangesAsync(cancellationToken);
            _cache.Remove(CacheKey); // Invalidate Cache
            return true;
        }
    }
}
