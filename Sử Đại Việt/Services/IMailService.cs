using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public interface IMailService
    {
        Task<IEnumerable<MailboxItem>> GetMailboxAsync(Guid userId);
        Task<MailboxItem> ReadMailAsync(Guid userId, long mailId);
        Task<MailboxItem> ClaimMailAttachmentsAsync(Guid userId, long mailId);
        Task<MailboxItem> SendMailAsync(MailboxItem mail, string adminUsername);
    }
}
