using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sử_Đại_Việt.Data;
using Sử_Đại_Việt.Dtos;
using Sử_Đại_Việt.Models;
using Sử_Đại_Việt.Services;

using Microsoft.AspNetCore.RateLimiting;

namespace Sử_Đại_Việt.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("AdminApiPolicy")]
    public class ConfigController : ControllerBase
    {
        private readonly IConfigService _configService;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;
        private readonly IAdminLogService _adminLogService;

        public ConfigController(IConfigService configService, IConfiguration configuration, ApplicationDbContext context, IAdminLogService adminLogService)
        {
            _configService = configService;
            _configuration = configuration;
            _context = context;
            _adminLogService = adminLogService;
        }

        /// <summary>
        /// Lấy toàn bộ cấu hình chỉ số game từ xa (Sát thương, Tốc độ của 3 anh em Tây Sơn).
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<GameConfig>>> GetConfigs(CancellationToken cancellationToken)
        {
            var configs = await _configService.GetAllConfigsAsync(cancellationToken);
            return Ok(configs);
        }

        /// <summary>
        /// Cập nhật hoặc thêm mới cấu hình cân bằng chỉ số game (Dành cho Web Admin).
        /// </summary>
        [HttpPut]
        public async Task<ActionResult<GameConfig>> UpdateConfig([FromBody] UpdateConfigDto model, CancellationToken cancellationToken)
        {
            // Kiểm tra bảo mật: Yêu cầu Header X-Admin-Key để xác thực quyền quản trị
            if (!Request.Headers.TryGetValue("X-Admin-Key", out var extractedKey) || 
                extractedKey != _configuration["AdminSettings:AdminKey"])
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            // Bảo mật và tối ưu: Giới hạn độ dài mô tả để tránh tràn database
            var sanitizedDesc = model.Description != null && model.Description.Length > 200 
                ? model.Description.Substring(0, 200) 
                : model.Description;

            // Kiểm tra giá trị cũ trước khi cập nhật để lưu log chi tiết
            var oldConfig = await _context.GameConfigs.FirstOrDefaultAsync(c => c.ConfigKey == model.ConfigKey, cancellationToken);
            string actionDetail;
            if (oldConfig == null)
            {
                actionDetail = $"Tạo mới cấu hình '{model.ConfigKey}' với giá trị {model.ConfigValue}. Mô tả: {sanitizedDesc}";
            }
            else
            {
                actionDetail = $"Thay đổi cấu hình '{model.ConfigKey}' từ {oldConfig.ConfigValue} sang {model.ConfigValue}. Mô tả mới: {sanitizedDesc}";
            }

            var config = await _configService.UpdateConfigAsync(model.ConfigKey, model.ConfigValue, sanitizedDesc, cancellationToken);
            if (config == null)
            {
                return NotFound("Không tìm thấy cấu hình cần cập nhật.");
            }

            // Ghi nhật ký kiểm toán hành động quản trị thông qua Service chuyên biệt
            await _adminLogService.LogActionAsync("Admin_System", "Cập nhật cấu hình Game", actionDetail);

            return Ok(config);
        }

        /// <summary>
        /// Xóa cấu hình game từ xa (Dành cho Web Admin).
        /// </summary>
        [HttpDelete("{key}")]
        public async Task<IActionResult> DeleteConfig(string key, CancellationToken cancellationToken)
        {
            // Kiểm tra bảo mật: Yêu cầu Header X-Admin-Key để xác thực quyền quản trị
            if (!Request.Headers.TryGetValue("X-Admin-Key", out var extractedKey) || 
                extractedKey != _configuration["AdminSettings:AdminKey"])
            {
                return StatusCode(401, "Truy cập bị từ chối. Mã quản trị X-Admin-Key không chính xác hoặc trống.");
            }

            var deleted = await _configService.DeleteConfigAsync(key, cancellationToken);
            if (!deleted)
            {
                return NotFound($"Không tìm thấy chỉ số cấu hình '{key}' để xóa.");
            }

            // Ghi nhật ký kiểm toán hành động quản trị thông qua Service chuyên biệt
            await _adminLogService.LogActionAsync("Admin_System", "Xóa cấu hình Game", $"Đã xóa vĩnh viễn cấu hình '{key}'.");

            return Ok(new { message = $"Đã xóa thành công chỉ số cấu hình '{key}'." });
        }
    }
}
