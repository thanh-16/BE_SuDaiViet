using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sử_Đại_Việt.Data;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public class ConfigService : IConfigService
    {
        private readonly ApplicationDbContext _context;

        public ConfigService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<GameConfig>> GetAllConfigsAsync()
        {
            return await _context.GameConfigs.AsNoTracking().ToListAsync();
        }

        public async Task<GameConfig?> UpdateConfigAsync(string key, decimal value, string? description = null)
        {
            var config = await _context.GameConfigs.FindAsync(key);
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

            await _context.SaveChangesAsync();
            return config;
        }

        public async Task<bool> DeleteConfigAsync(string key)
        {
            var config = await _context.GameConfigs.FindAsync(key);
            if (config == null) return false;

            _context.GameConfigs.Remove(config);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
