using System;
using System.Collections.Generic;
using System.Linq;
using NeoNetsphere;
using NeoNetsphere.Database.Game;
using NeoNetsphere.Network;
using NeoNetsphere.Resource;

// ReSharper disable once Checknamespace 
namespace NeoNetsphere
{
  internal class Character
  {
    internal Character(CharacterManager characterManager, PlayerCharacterDto dto)
    {
      CharacterManager = characterManager;

      Weapons = new WeaponManager(this, dto);
      Skills = new SkillManager(this, dto);
      Costumes = new CostumeManager(this, dto);

      var defaultItems = GameServer.Instance.ResourceCache.GetDefaultItems();

      ExistsInDatabase = true;
      Id = dto.Id;
      Slot = dto.Slot;
      Gender = (CharacterGender)dto.Gender;
    }

    internal Character(CharacterManager characterManager, byte slot, CharacterGender gender)
    {
      CharacterManager = characterManager;

      Weapons = new WeaponManager(this);
      Skills = new SkillManager(this);
      Costumes = new CostumeManager(this);

      Slot = slot;
      Gender = gender;
    }

    public CharacterManager CharacterManager { get; }

    internal bool ExistsInDatabase { get; set; }
    internal bool NeedsToSave { get; set; }

    public int Id { get; }
    public byte Slot { get; }

    public CharacterGender Gender { get; }
    public WeaponManager Weapons { get; }
    public SkillManager Skills { get; }
    public CostumeManager Costumes { get; }

    public void Equip(PlayerItem item, byte slot)
    {
      switch (item.ItemNumber.Category)
      {
        case ItemCategory.Costume:
          Costumes.Equip(item, (CostumeSlot)slot);
          break;

        case ItemCategory.Weapon:
          Weapons.Equip(item, (WeaponSlot)slot);
          break;

        case ItemCategory.Skill:
          Skills.Equip(item, (SkillSlot)slot);
          break;
        default:
          throw new CharacterException("Invalid category " + item.ItemNumber.Category);
      }
    }

    public void UnEquip(ItemCategory category, byte slot)
    {
      switch (category)
      {
        case ItemCategory.Costume:
          Costumes.UnEquip((CostumeSlot)slot);
          break;

        case ItemCategory.Weapon:
          Weapons.UnEquip((WeaponSlot)slot);
          break;

        case ItemCategory.Skill:
          Skills.UnEquip((SkillSlot)slot);
          break;

        default:
          throw new CharacterException("Invalid category" + category);
      }
    }

    public bool CanEquip(PlayerItem item, byte slot)
    {
      switch (item.ItemNumber.Category)
      {
        case ItemCategory.Costume:
          return Costumes.CanEquip(item, (CostumeSlot)slot);

        case ItemCategory.Weapon:
          return Weapons.CanEquip(item, (WeaponSlot)slot);

        case ItemCategory.Skill:
          return Skills.CanEquip(item, (SkillSlot)slot);

        default:
          return false;
      }
    }

    public uint GetHP()
    {
      var extraHp = 0U;

      var items = new List<PlayerItem>();
      items.AddRange(Costumes.GetItems());
      items.AddRange(Skills.GetItems());
      items.AddRange(Weapons.GetItems());

      foreach (var item in items)
      {
        if (item?.Effects == null)
          continue;

        foreach (var effect in item.Effects.Where(x => x.Number < 1000))
        {
          if (effect.SubCategory < 10 &&
              effect.Sub2Category == 30)
          {
            extraHp += effect.Number;
          }
          else
          {
            switch (effect)
            {
              case 1999300009:
                extraHp += 15;
                break;
              case 1999300010:
                extraHp += 20;
                break;
              case 1999300011:
                extraHp += 30;
                break;
              case 1999300012:
                extraHp += 25;
                break;
            }
          }
        }
      }

      return 100 + extraHp;
    }
  }
}