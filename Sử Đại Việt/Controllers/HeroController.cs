using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sử_Đại_Việt.Dtos;
using Sử_Đại_Việt.Models;
using Sử_Đại_Việt.Services;

namespace Sử_Đại_Việt.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [EnableRateLimiting("ScoreSubmitPolicy")]
    public class HeroController : ControllerBase
    {
        private readonly IHeroService _heroService;

        public HeroController(IHeroService heroService)
        {
            _heroService = heroService;
        }

        /// <summary>
        /// Lấy danh sách tướng đã mở khóa của người chơi.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PlayerHero>>> GetMyHeroes()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                             ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            var heroes = await _heroService.GetHeroesAsync(userId);
            return Ok(heroes);
        }

        /// <summary>
        /// Mở khóa vị tướng mới (hue, nhac, lu).
        /// </summary>
        [HttpPost("unlock/{heroKey}")]
        public async Task<IActionResult> UnlockHero(string heroKey)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                             ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            try
            {
                var hero = await _heroService.UnlockHeroAsync(userId, heroKey);
                return Ok(new { message = $"Đã mở khóa tướng {heroKey.ToUpper()} thành công!", hero = hero });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi mở khóa tướng.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Mặc trang bị vào rương tướng.
        /// </summary>
        [HttpPost("equip")]
        public async Task<IActionResult> EquipItem([FromBody] EquipItemDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                             ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            try
            {
                var eq = await _heroService.EquipItemAsync(userId, model.PlayerHeroId, model.InventoryItemId, model.SlotType);
                return Ok(new { message = $"Đã mặc trang bị vào slot {model.SlotType} thành công!", equipment = eq });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi mặc trang bị.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Tháo trang bị khỏi slot trên tướng.
        /// </summary>
        [HttpPost("unequip")]
        public async Task<IActionResult> UnequipItem([FromBody] EquipItemDto model)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                             ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            try
            {
                await _heroService.UnequipItemAsync(userId, model.PlayerHeroId, model.SlotType);
                return Ok(new { message = $"Đã tháo trang bị slot {model.SlotType} thành công!" });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi tháo trang bị.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Nâng cấp kỹ năng chủ động/bị động của tướng (Tiêu hao Vàng).
        /// </summary>
        [HttpPost("upgrade-skill")]
        public async Task<IActionResult> UpgradeSkill([FromBody] UpgradeSkillDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                             ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Mã định danh người dùng trong Token không hợp lệ hoặc bị thiếu." });
            }

            try
            {
                var hero = await _heroService.UpgradeSkillAsync(userId, model.PlayerHeroId, model.SkillKey);
                return Ok(new { message = $"Đã nâng cấp kỹ năng {model.SkillKey} thành công!", hero = hero });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi nâng cấp kỹ năng tướng.", detail = ex.Message });
            }
        }
    }
}
