using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sử_Đại_Việt.Data;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public class AdminLogService : IAdminLogService
    {
        private readonly ApplicationDbContext _context;

        public AdminLogService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task LogActionAsync(string adminUsername, string actionName, string actionDetails)
        {
            var log = new AdminLog
            {
                AdminUsername = adminUsername,
                ActionName = actionName,
                ActionDetails = actionDetails,
                CreatedAt = DateTime.UtcNow
            };
            _context.AdminLogs.Add(log);
            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<AdminLog>> GetLogsAsync(string? search, int pageIndex, int pageSize)
        {
            var query = _context.AdminLogs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(l => 
                    l.AdminUsername.ToLower().Contains(lowerSearch) ||
                    l.ActionName.ToLower().Contains(lowerSearch) ||
                    l.ActionDetails.ToLower().Contains(lowerSearch)
                );
            }

            return await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> GetLogsCountAsync(string? search)
        {
            var query = _context.AdminLogs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(l => 
                    l.AdminUsername.ToLower().Contains(lowerSearch) ||
                    l.ActionName.ToLower().Contains(lowerSearch) ||
                    l.ActionDetails.ToLower().Contains(lowerSearch)
                );
            }

            return await query.CountAsync();
        }
    }
}
