using System.ComponentModel.DataAnnotations;

namespace Sử_Đại_Việt.Dtos
{
    public class EquipItemDto
    {
        [Required(ErrorMessage = "Mã tướng không được để trống")]
        public long PlayerHeroId { get; set; }

        [Required(ErrorMessage = "Mã vật phẩm trong túi đồ không được để trống")]
        public long InventoryItemId { get; set; }

        [Required(ErrorMessage = "Slot trang phục không được để trống")]
        [RegularExpression("(?i)^(Weapon|Armor|Helmet|Ring)$", ErrorMessage = "Slot chỉ chấp nhận Weapon, Armor, Helmet hoặc Ring")]
        public string SlotType { get; set; } = "Weapon";
    }

    public class UpgradeSkillDto
    {
        [Required(ErrorMessage = "Mã tướng không được để trống")]
        public long PlayerHeroId { get; set; }

        [Required(ErrorMessage = "Mã kỹ năng không được để trống")]
        [RegularExpression("(?i)^(skill_active|skill_passive)$", ErrorMessage = "Chỉ nhận skill_active hoặc skill_passive")]
        public string SkillKey { get; set; } = "skill_active";
    }
}
