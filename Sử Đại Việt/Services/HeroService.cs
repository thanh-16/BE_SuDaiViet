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
    public class HeroService : IHeroService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAdminLogService _adminLogService;

        public HeroService(ApplicationDbContext context, IAdminLogService adminLogService)
        {
            _context = context;
            _adminLogService = adminLogService;
        }

        public async Task<IEnumerable<PlayerHero>> GetHeroesAsync(Guid userId)
        {
            return await _context.PlayerHeroes
                .AsNoTracking()
                .Where(h => h.UserId == userId)
                .OrderBy(h => h.HeroKey)
                .ToListAsync();
        }

        public async Task<PlayerHero> UnlockHeroAsync(Guid userId, string heroKey)
        {
            var normalizedKey = heroKey.ToLower().Trim();
            if (normalizedKey != "hue" && normalizedKey != "nhac" && normalizedKey != "lu")
            {
                throw new ArgumentException("Mã tướng không hợp lệ. Chỉ chấp nhận 'hue', 'nhac', 'lu'.");
            }

            var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Id == userId);
            if (profile == null)
            {
                throw new KeyNotFoundException("Không tìm thấy thông tin tài khoản người chơi.");
            }

            var existing = await _context.PlayerHeroes.FirstOrDefaultAsync(h => h.UserId == userId && h.HeroKey == normalizedKey);
            if (existing != null)
            {
                throw new InvalidOperationException("Nghĩa sĩ đã mở khóa tướng này rồi.");
            }

            var hero = new PlayerHero
            {
                UserId = userId,
                HeroKey = normalizedKey,
                Level = 1,
                Experience = 0,
                SkillsLevel = "{\"skill_active\": 1, \"skill_passive\": 1}",
                UnlockedAt = DateTime.UtcNow
            };

            _context.PlayerHeroes.Add(hero);
            await _context.SaveChangesAsync();

            await _adminLogService.LogActionAsync(
                "System",
                "UnlockHero",
                $"Người chơi '{profile.DisplayName}' ({userId}) đã mở khóa tướng '{normalizedKey}'."
            );

            return hero;
        }

        public async Task<HeroEquipment> EquipItemAsync(Guid userId, long playerHeroId, long inventoryItemId, string slotType)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Kiểm tra tướng tồn tại và thuộc sở hữu của người chơi
                var hero = await _context.PlayerHeroes.FirstOrDefaultAsync(h => h.Id == playerHeroId && h.UserId == userId);
                if (hero == null)
                {
                    throw new KeyNotFoundException("Không tìm thấy thông tin tướng hoặc tướng không thuộc sở hữu của bạn.");
                }

                // 2. Kiểm tra vật phẩm tồn tại trong rương đồ của người chơi
                var inventory = await _context.PlayerInventories
                    .Include(pi => pi.ItemDetails)
                    .FirstOrDefaultAsync(pi => pi.Id == inventoryItemId && pi.UserId == userId);

                if (inventory == null || inventory.ItemDetails == null)
                {
                    throw new KeyNotFoundException("Không tìm thấy vật phẩm trong kho đồ của bạn.");
                }

                if (inventory.Quantity < 1)
                {
                    throw new InvalidOperationException("Số lượng vật phẩm trong kho không đủ để trang bị.");
                }

                var item = inventory.ItemDetails;

                // 3. Kiểm tra yêu cầu cấp độ của trang bị
                if (!string.IsNullOrEmpty(item.Attributes))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(item.Attributes);
                        if (doc.RootElement.TryGetProperty("level_requirement", out var levelReqElement) && levelReqElement.TryGetInt32(out var levelReq))
                        {
                            if (hero.Level < levelReq)
                            {
                                throw new InvalidOperationException($"Cấp độ của tướng {hero.HeroKey.ToUpper()} (Cấp {hero.Level}) chưa đủ để mặc trang bị này. Yêu cầu tối thiểu cấp {levelReq}.");
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // JSON lỗi, bỏ qua
                    }
                }

                // 4. Kiểm tra xem trang bị này có đang được mặc trên tướng khác hay slot khác của chính tướng này không
                var alreadyEquipped = await _context.HeroEquipments
                    .AnyAsync(he => he.InventoryItemId == inventoryItemId);

                if (alreadyEquipped)
                {
                    throw new InvalidOperationException("Món trang bị cụ thể này đã được mặc trên một tướng khác rồi. Hãy tháo ra trước.");
                }

                // 5. Tháo trang bị cũ ở slot này của tướng này ra (nếu có)
                var oldEquipment = await _context.HeroEquipments
                    .FirstOrDefaultAsync(he => he.PlayerHeroId == playerHeroId && he.SlotType.ToLower() == slotType.ToLower());

                if (oldEquipment != null)
                {
                    _context.HeroEquipments.Remove(oldEquipment);
                }

                // 6. Mặc trang bị mới
                var newEquipment = new HeroEquipment
                {
                    PlayerHeroId = playerHeroId,
                    InventoryItemId = inventoryItemId,
                    SlotType = slotType,
                    EquippedAt = DateTime.UtcNow
                };

                _context.HeroEquipments.Add(newEquipment);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return newEquipment;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task UnequipItemAsync(Guid userId, long playerHeroId, string slotType)
        {
            var hero = await _context.PlayerHeroes.FirstOrDefaultAsync(h => h.Id == playerHeroId && h.UserId == userId);
            if (hero == null)
            {
                throw new KeyNotFoundException("Không tìm thấy thông tin tướng hoặc tướng không thuộc sở hữu của bạn.");
            }

            var equipment = await _context.HeroEquipments
                .FirstOrDefaultAsync(he => he.PlayerHeroId == playerHeroId && he.SlotType.ToLower() == slotType.ToLower());

            if (equipment == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy trang bị ở slot '{slotType}' của tướng này.");
            }

            _context.HeroEquipments.Remove(equipment);
            await _context.SaveChangesAsync();
        }

        public async Task<PlayerHero> UpgradeSkillAsync(Guid userId, long playerHeroId, string skillKey)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Khóa ví wallets tránh race condition nâng skill đồng thời
                await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM public.wallets WHERE user_id = {userId} FOR UPDATE");

                var hero = await _context.PlayerHeroes.FirstOrDefaultAsync(h => h.Id == playerHeroId && h.UserId == userId);
                if (hero == null)
                {
                    throw new KeyNotFoundException("Không tìm thấy thông tin tướng hoặc tướng không thuộc sở hữu của bạn.");
                }

                var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Id == userId);
                if (profile == null)
                {
                    throw new KeyNotFoundException("Không tìm thấy thông tin tài khoản người chơi.");
                }

                var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
                if (wallet == null)
                {
                    throw new KeyNotFoundException("Không tìm thấy thông tin ví của người chơi.");
                }

                // Giải mã JSON kỹ năng
                var skillLevels = JsonSerializer.Deserialize<Dictionary<string, int>>(hero.SkillsLevel);
                if (skillLevels == null || !skillLevels.ContainsKey(skillKey))
                {
                    throw new ArgumentException($"Không tìm thấy kỹ năng '{skillKey}' để nâng cấp.");
                }

                int currentLevel = skillLevels[skillKey];
                int goldCost = currentLevel * 500; // Công thức: Level hiện tại * 500 Vàng

                if (wallet.GoldBalance < goldCost)
                {
                    throw new InvalidOperationException($"Số dư Vàng không đủ để nâng cấp kỹ năng (Cần {goldCost} Vàng, bạn hiện có {wallet.GoldBalance} Vàng).");
                }

                wallet.GoldBalance -= goldCost;
                wallet.UpdatedAt = DateTime.UtcNow;
                skillLevels[skillKey] = currentLevel + 1;

                hero.SkillsLevel = JsonSerializer.Serialize(skillLevels);

                // Ghi nhận lịch sử giao dịch tiêu hao Gold nâng skill
                var txn = new Transaction
                {
                    UserId = userId,
                    TransactionType = "SkillUpgrade",
                    AmountVnd = 0,
                    AmountGold = -goldCost,
                    AmountGem = 0,
                    PaymentMethod = "GOLD",
                    ReferenceId = $"UP-SKILL-{hero.HeroKey.ToUpper()}-{Guid.NewGuid().ToString()[..8].ToUpper()}",
                    Status = "Completed",
                    CreatedAt = DateTime.UtcNow
                };

                _context.Transactions.Add(txn);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return hero;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
