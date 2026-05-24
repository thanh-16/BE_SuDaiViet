using System.Collections.Generic;
using System.Threading.Tasks;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public interface IAdminLogService
    {
        /// <summary>
        /// Ghi nhận hành động kiểm toán của quản trị viên vào cơ sở dữ liệu.
        /// </summary>
        Task LogActionAsync(string adminUsername, string actionName, string actionDetails);

        /// <summary>
        /// Truy vấn danh sách nhật ký hành động phân trang có hỗ trợ tìm kiếm.
        /// </summary>
        Task<IEnumerable<AdminLog>> GetLogsAsync(string? search, int pageIndex, int pageSize);

        /// <summary>
        /// Đếm tổng số bản ghi nhật ký hành động thỏa mãn điều kiện tìm kiếm.
        /// </summary>
        Task<int> GetLogsCountAsync(string? search);
    }
}
