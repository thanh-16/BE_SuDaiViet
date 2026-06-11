using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sử_Đại_Việt.Models;

namespace Sử_Đại_Việt.Services
{
    public interface IHeroService
    {
        Task<IEnumerable<PlayerHero>> GetHeroesAsync(Guid userId);
        Task<PlayerHero> UnlockHeroAsync(Guid userId, string heroKey);
        Task<HeroEquipment> EquipItemAsync(Guid userId, long playerHeroId, long inventoryItemId, string slotType);
        Task UnequipItemAsync(Guid userId, long playerHeroId, string slotType);
        Task<PlayerHero> UpgradeSkillAsync(Guid userId, long playerHeroId, string skillKey);
    }
}
