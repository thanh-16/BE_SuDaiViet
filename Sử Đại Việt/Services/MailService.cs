using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sử_Đại_Việt.Data;
using Sử_Đại_Việt.Models;
using System.Text.Json;

namespace Sử_Đại_Việt.Services
{
    public class MailService : IMailService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAdminLogService _adminLogService;

        public MailService(ApplicationDbContext context, IAdminLogService adminLogService)
        {
            _context = context;
            _adminLogService = adminLogService;
        }

        public async Task<IEnumerable<MailboxItem>> GetMailboxAsync(Guid userId)
        {
            var now = DateTime.UtcNow;

            // 1. Lấy tất cả thư cá nhân còn hạn
            var personalMails = await _context.MailboxItems
                .Where(m => m.ReceiverId == userId && (m.ExpiredAt == null || m.ExpiredAt > now))
                .ToListAsync();

            // 2. Lấy tất cả thư Broadcast còn hạn
            var broadcastMails = await _context.MailboxItems
                .Where(m => m.ReceiverId == null && (m.ExpiredAt == null || m.ExpiredAt > now))
                .ToListAsync();

            // 3. Lấy thông tin trạng thái nhận thư Broadcast của người chơi này
            var claims = await _context.PlayerBroadcastClaims
                .Where(c => c.UserId == userId)
                .ToDictionaryAsync(c => c.MailId);

            var result = new List<MailboxItem>();

            // Add personal mails
            result.AddRange(personalMails);

            // Add broadcast mails projected with player claim status
            foreach (var bMail in broadcastMails)
            {
                var projectedMail = new MailboxItem
                {
                    Id = bMail.Id,
                    ReceiverId = null,
                    Title = bMail.Title,
                    Content = bMail.Content,
                    Attachments = bMail.Attachments,
                    CreatedAt = bMail.CreatedAt,
                    ExpiredAt = bMail.ExpiredAt,
                    IsRead = false,
                    IsClaimed = false
                };

                if (claims.TryGetValue(bMail.Id, out var claim))
                {
                    projectedMail.IsRead = claim.IsRead;
                    projectedMail.IsClaimed = claim.IsClaimed;
                }

                result.Add(projectedMail);
            }

            return result.OrderByDescending(m => m.CreatedAt);
        }

        public async Task<MailboxItem> ReadMailAsync(Guid userId, long mailId)
        {
            var mail = await _context.MailboxItems.FirstOrDefaultAsync(m => m.Id == mailId);
            if (mail == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy thư có mã '{mailId}'.");
            }

            if (mail.ExpiredAt.HasValue && mail.ExpiredAt.Value < DateTime.UtcNow)
            {
                throw new InvalidOperationException("Thư này đã hết hạn.");
            }

            // A. Thư cá nhân
            if (mail.ReceiverId.HasValue)
            {
                if (mail.ReceiverId.Value != userId)
                {
                    throw new UnauthorizedAccessException("Bạn không có quyền đọc thư này.");
                }

                mail.IsRead = true;
                await _context.SaveChangesAsync();
                return mail;
            }
            // B. Thư Broadcast toàn server
            else
            {
                var claim = await _context.PlayerBroadcastClaims
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.MailId == mailId);

                if (claim == null)
                {
                    claim = new PlayerBroadcastClaim
                    {
                        UserId = userId,
                        MailId = mailId,
                        IsRead = true,
                        IsClaimed = false,
                        ClaimedAt = DateTime.UtcNow
                    };
                    _context.PlayerBroadcastClaims.Add(claim);
                }
                else
                {
                    claim.IsRead = true;
                }

                await _context.SaveChangesAsync();

                // Tạo thực thể trả về tạm thời đại diện cho hiển thị của người chơi
                var displayMail = new MailboxItem
                {
                    Id = mail.Id,
                    ReceiverId = null,
                    Title = mail.Title,
                    Content = mail.Content,
                    Attachments = mail.Attachments,
                    CreatedAt = mail.CreatedAt,
                    ExpiredAt = mail.ExpiredAt,
                    IsRead = true,
                    IsClaimed = claim.IsClaimed
                };

                return displayMail;
            }
        }

        public async Task<MailboxItem> ClaimMailAttachmentsAsync(Guid userId, long mailId)
        {
            var resultList = await _context.Database
                .SqlQueryRaw<string>("SELECT public.claim_mailbox_attachments({0}, {1}) as \"Value\"", userId, mailId)
                .ToListAsync();

            var result = resultList.FirstOrDefault();

            if (result != null && result.StartsWith("ERROR:"))
            {
                throw new InvalidOperationException(result.Substring(6).Trim());
            }

            // Tải lại hòm thư đã được cập nhật thành công từ DB
            var mail = await _context.MailboxItems.FirstOrDefaultAsync(m => m.Id == mailId);
            if (mail == null)
            {
                throw new KeyNotFoundException("Không tìm thấy thông tin thư trong hệ thống.");
            }

            return mail;
        }

        public async Task<MailboxItem> SendMailAsync(MailboxItem mail, string adminUsername)
        {
            // Kiểm tra tính hợp lệ của chuỗi JSON attachments nếu có
            if (!string.IsNullOrEmpty(mail.Attachments))
            {
                try
                {
                    using var doc = JsonDocument.Parse(mail.Attachments);
                }
                catch (JsonException)
                {
                    throw new ArgumentException("Chuỗi Attachments đính kèm không phải là JSON hợp lệ.");
                }
            }

            mail.CreatedAt = DateTime.UtcNow;
            mail.IsRead = false;
            mail.IsClaimed = false;

            _context.MailboxItems.Add(mail);
            await _context.SaveChangesAsync();

            string mailType = mail.ReceiverId.HasValue ? $"Cá nhân (User: {mail.ReceiverId})" : "Broadcast Toàn Server";
            await _adminLogService.LogActionAsync(
                adminUsername,
                "SendMail",
                $"Gửi thư '{mail.Title}' ({mailType}). Đính kèm: {mail.Attachments ?? "Không có"}."
            );

            return mail;
        }

        private class ItemAttachment
        {
            public string Id { get; set; } = string.Empty;
            public int Qty { get; set; }
        }
    }
}
