using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using BlubLib.Configuration;
using NeoNetsphere.Network;
using NeoNetsphere.Resource.xml;
using Serilog;
using Serilog.Core;

namespace NeoNetsphere.Resource
{
  internal class ResourceLoader
  {
    // ReSharper disable once InconsistentNaming
    private static readonly ILogger Logger =
        Log.ForContext(Constants.SourceContextPropertyName, nameof(ResourceLoader));

    public ResourceLoader(string resourcePath)
    {
      ResourcePath = resourcePath;
    }

    public string ResourcePath { get; }

    public byte[] GetBytes(string fileName)
    {
      var path = Path.Combine(ResourcePath, fileName.Replace('/', Path.DirectorySeparatorChar));
      return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public IEnumerable<Experience> LoadExperience()
    {
      var dto = Deserialize<ExperienceDto>("xml/experience.x7");

      var i = 0;
      return dto.exp.Select(expDto => new Experience
      {
        Level = i++,
        ExperienceToNextLevel = expDto.require,
        TotalExperience = expDto.accumulate
      });
    }

        public IEnumerable<MapInfo> LoadMaps()
        {
            var stringTable = Deserialize<StringTableDto>("language/xml/gameinfo_string_table.x7");
            var dto = Deserialize<MapInfoDto>("xml/map.x7");

            var maps = new ConcurrentDictionary<Tuple<GameRule, byte>, MapInfo>();

            foreach (var mapDto in dto.map)
            {
                var pf = mapDto.resource?.previewinfo_path ?? "";
                if (!pf.EndsWith(".tga") &&
                    !pf.EndsWith(".dds"))
                    continue;

                var seu = mapDto.Switch?.eu ?? "";
                var skr = mapDto.Switch?.kr ?? "";
                if (seu != "on" &&
                    skr != "on")
                    continue;

                var byteId = unchecked((byte)mapDto.id);

                var map = new MapInfo
                {
                    Id = mapDto.id,
                    byteId = byteId,
                    MinLevel = 0,
                    ServerId = 0,
                    ChannelId = 0,
                    RespawnType = 0,
                    MaxPlayers = mapDto.Base.limit_player,
                    IsRandom = mapDto.id > 900,
                    GameRule = (GameRule)mapDto.Base.mode_number
                };

                var name_ = new StringTableStringDto();
                try
                {
                    name_ = stringTable.@string.First(s =>
                        s.key.Equals(mapDto.Base.map_name_key, StringComparison.InvariantCultureIgnoreCase));
                }
                catch (Exception ex)
                {
                    name_.eng = "unknown";
                }

                var name = name_;
                if (string.IsNullOrWhiteSpace(name.eng))
                    name.eng = mapDto.Base.map_name_key;

                map.Name = name.eng;
                maps.TryAdd(new Tuple<GameRule, byte>(map.GameRule, byteId), map);
            }

            return maps.Values;
        }

        public IEnumerable<LevelReward> LoadLevelRewards()
        {
            var dto = Deserialize<LevelRewardDto>("xml/LevelRewards.xml");

            foreach (var level in dto.Levels)
            {
                var lvl = new LevelReward
                {
                    Level = (byte)level.Number,
                    Reward = level.Reward,
                    AP = level.AP,
                    Pen = level.Pen
                };

                yield return lvl;
            }
        }

        public IEnumerable<EnchantSys> LoadEnchantSystem()
        {
            EnchantSystemDto dto = Deserialize<EnchantSystemDto>("xml/Enchant.xml");
            LevelEnchantDto[] levels = dto.Levels;
            foreach (LevelEnchantDto level in levels)
            {
                EnchantSys enchants = new EnchantSys
                {
                    Level = level.Value,
                    EnchantGroup = new List<EnchantGroup>()
                };
                EnchantGroupDto[] enchantGroup = level.EnchantGroup;
                foreach (EnchantGroupDto type in enchantGroup)
                {
                    EnchantGroup group = new EnchantGroup
                    {
                       
                        Category = type.Category,
                        SubCategory = type.SubCategory,
                        EnchantSystem = new List<EnchantSystem>()
                        
                };
                    EffectEnchantDto[] effectEnchant = type.EffectEnchant;
                    foreach (EffectEnchantDto eff in effectEnchant)
                    {
                        group.EnchantSystem.Add(new EnchantSystem
                        {
                            Effect = eff.Value,
                            Chance = (byte)eff.Amount
                        });
                        enchants.EnchantGroup.Add(group);
                    }
                }
                yield return enchants;
            }
        }

        public IEnumerable<ItemEffect> LoadEffects()
       {
      var dto = Deserialize<ItemEffectDto>("xml/item_effect.x7");
      var stringTable = Deserialize<StringTableDto>("language/xml/item_effect_string_table.x7");

      foreach (var itemEffectDto in dto.item.Where(itemEffect => itemEffect.id != 0))
      {
        var itemEffect = new ItemEffect
        {
          Id = itemEffectDto.id
        };

        foreach (var attributeDto in itemEffectDto.attribute)
        {
          itemEffect.Attributes.Add(new ItemEffectAttribute
          {
            Attribute = (Attribute)Enum.Parse(typeof(Attribute), attributeDto.effect.Replace("_", ""),
                  true),
            Value = attributeDto.value,
            Rate = float.Parse(attributeDto.rate, CultureInfo.InvariantCulture)
          });
        }

        var name = stringTable.@string.FirstOrDefault(s =>
            s.key.Equals(itemEffectDto.text_key, StringComparison.InvariantCultureIgnoreCase));

        if (name == null)
          name = new StringTableStringDto();

        if (string.IsNullOrWhiteSpace(name.eng))
          name.eng = itemEffectDto.NAME;

        itemEffect.Name = name.eng;
        yield return itemEffect;
      }
    }

    public IEnumerable<GameTempo> LoadGameTempos()
    {
      var dto = Deserialize<ConstantInfoDto>("xml/constant_info.x7");

      foreach (var gameTempoDto in dto.GAMEINFOLIST)
      {
        var tempo = new GameTempo
        {
          Name = gameTempoDto.TEMPVALUE.value
        };

        var values = gameTempoDto.GAMETEPMO_COMMON_TOTAL_VALUE;
        tempo.ActorDefaultHPMax =
            float.Parse(values.GAMETEMPO_actor_default_hp_max, CultureInfo.InvariantCulture);
        tempo.ActorDefaultMPMax =
            float.Parse(values.GAMETEMPO_actor_default_mp_max, CultureInfo.InvariantCulture);
        tempo.ActorDefaultMoveSpeed = values.GAMETEMPO_fastrun_required_mp;

        yield return tempo;
      }
    }
     

        #region DefaultItems

        public IEnumerable<DefaultItem> LoadDefaultItems()
    {
      var dto = Deserialize<DefaultItemDto>("xml/default_item.x7");

      foreach (var itemDto in dto.male.item)
      {
        var item = new DefaultItem
        {
          ItemNumber = new ItemNumber(itemDto.category, itemDto.sub_category, itemDto.number),
          Gender = CharacterGender.Male,
          //Slot = (byte) ParseDefaultItemSlot(itemDto.Value),
          Variation = itemDto.variation
        };
        yield return item;
      }

      foreach (var itemDto in dto.female.item)
      {
        var item = new DefaultItem
        {
          ItemNumber = new ItemNumber(itemDto.category, itemDto.sub_category, itemDto.number),
          Gender = CharacterGender.Female,
          //Slot = (byte) ParseDefaultItemSlot(itemDto.Value),
          Variation = itemDto.variation
        };
        yield return item;
      }
    }

    #endregion

    private T Deserialize<T>(string fileName)
    {
      var serializer = new XmlSerializer(typeof(T));

      var path = Path.Combine(ResourcePath, fileName.Replace('/', Path.DirectorySeparatorChar));
      using (var r = new StreamReader(path))
      {
        return (T)serializer.Deserialize(r);
      }
    }

    #region Items

    public IEnumerable<ItemInfo> LoadItems()
    {
      var dto = Deserialize<ItemInfoDto>("xml/iteminfo.x7");
      var stringTable = Deserialize<StringTableDto>("language/xml/iteminfo_string_table.xml");

      foreach (var categoryDto in dto.category)
      {
        foreach (var subCategoryDto in categoryDto.sub_category)
        {
          foreach (var itemDto in subCategoryDto.item)
          {
            var id = new ItemNumber(categoryDto.id, subCategoryDto.id, itemDto.number);
            ItemInfo item;

            switch (id.Category)
            {
              case ItemCategory.Skill:
                item = LoadAction(id, itemDto);
                break;

              case ItemCategory.Weapon:
                item = LoadWeapon(id, itemDto);
                break;

              default:
                item = new ItemInfo();
                break;
            }

            item.ItemNumber = id;
            item.Level = itemDto.@base.base_info.require_level;
            item.MasterLevel = itemDto.@base.base_info.require_master;
            item.Gender = ParseGender(itemDto.SEX);
            item.Image = itemDto.client.icon.image;

            if (itemDto.@base.license != null)
              item.License = ParseItemLicense(itemDto.@base.license.require);

            var name = stringTable.@string.FirstOrDefault(s =>
                s.key.Equals(itemDto.@base.base_info.name_key,
                    StringComparison.InvariantCultureIgnoreCase));
            if (string.IsNullOrWhiteSpace(name?.eng))
              item.Name = name != null ? name.key : itemDto.NAME;
            else
              item.Name = name.eng;

            yield return item;
          }
        }
      }
    }

    public IEnumerable<ItemInfo> LoadItems_2()
    {
      var dto = Deserialize<ItemInfoDto_2>("xml/item.x7");
      var stringTable = Deserialize<StringTableDto_2>("language/xml/iteminfo_string_table.x7");
      var ids = new List<ItemNumber>();
      foreach (var itemDto in dto.item)
      {
        var id = new ItemNumber(itemDto.item_key);
        if (!ids.Contains(id))
        {
          ids.Add(id);
          var item = new ItemInfo();
          item.ItemNumber = id;
          item.Level = 0;
          item.MasterLevel = 0;
          item.Gender = ParseGender_2(itemDto.Base.sex);
          item.Image = itemDto.graphic.icon_image;

          var name = stringTable.@string.FirstOrDefault(s =>
              s.key.Equals(itemDto.Base.name_key, StringComparison.InvariantCultureIgnoreCase));
          if (!string.IsNullOrWhiteSpace(name?.eng) && name?.eng.ToLower() != "no trans" &&
              name?.eng.ToLower() != "not trans")
            yield return item;
        }
      }
    }

    public IEnumerable<ItemInfo> LoadItems_3()
    {
      var dto = Deserialize<ItemInfoDto_2>("xml/item.x7");
      var dto2 = Deserialize<ItemInfoDto_3>("xml/dumpeditems.xml");
      var stringTable = Deserialize<StringTableDto_2>("language/xml/iteminfo_string_table.x7");
      var ids = new Dictionary<ItemNumber, ItemInfo>();
      foreach (var itemDto in dto.item)
      {
        var id = new ItemNumber(itemDto.item_key);
        if (!ids.Keys.Contains(id))
        {
          var item = new ItemInfo();
          item.ItemNumber = id;
          item.Level = 0;
          item.MasterLevel = 0;
          item.Gender = ParseGender_2(itemDto.Base.sex);
          item.Image = itemDto.graphic.icon_image;
          ids.Add(id, item);
        }
      }

      foreach (var itemdto in dto2.Item)
      {
        ItemInfo item;
        ids.TryGetValue(new ItemNumber(itemdto.ID), out item);
        if (item != null)
        {
          item.Colors = (int)itemdto.Color_Count;
          item.Name = itemdto.Name;

          if (!string.IsNullOrWhiteSpace(item.Name) &&
              item.Name != "not trans" &&
              item.Name != "no trans" &&
              !string.IsNullOrWhiteSpace(item.Image))
            yield return item;
        }
      }
    }

    private static ItemLicense ParseItemLicense(string license)
    {
      Func<string, bool> equals = str => license.Equals(str, StringComparison.InvariantCultureIgnoreCase);

      if (equals("license_none"))
        return ItemLicense.None;

      if (equals("LICENSE_CHECK_NONE"))
        return ItemLicense.None;

      if (equals("LICENSE_PLASMA_SWORD"))
        return ItemLicense.PlasmaSword;

      if (equals("license_counter_sword"))
        return ItemLicense.CounterSword;

      if (equals("LICENSE_STORM_BAT"))
        return ItemLicense.StormBat;

      if (equals("LICENSE_ASSASSIN_CLAW"))
        return ItemLicense.None; // ToDo

      if (equals("LICENSE_SUBMACHINE_GUN"))
        return ItemLicense.SubmachineGun;

      if (equals("license_revolver"))
        return ItemLicense.Revolver;

      if (equals("license_semi_rifle"))
        return ItemLicense.SemiRifle;

      if (equals("LICENSE_SMG3"))
        return ItemLicense.None; // ToDo

      if (equals("license_HAND_GUN"))
        return ItemLicense.None; // ToDo

      if (equals("LICENSE_SMG4"))
        return ItemLicense.None; // ToDo

      if (equals("LICENSE_HEAVYMACHINE_GUN"))
        return ItemLicense.HeavymachineGun;

      if (equals("LICENSE_GAUSS_RIFLE"))
        return ItemLicense.GaussRifle;

      if (equals("license_rail_gun"))
        return ItemLicense.RailGun;

      if (equals("license_cannonade"))
        return ItemLicense.Cannonade;

      if (equals("LICENSE_CENTRYGUN"))
        return ItemLicense.Sentrygun;

      if (equals("license_centi_force"))
        return ItemLicense.SentiForce;

      if (equals("LICENSE_SENTINEL"))
        return ItemLicense.SentiNel;

      if (equals("license_mine_gun"))
        return ItemLicense.MineGun;

      if (equals("LICENSE_MIND_ENERGY"))
        return ItemLicense.MindEnergy;

      if (equals("license_mind_shock"))
        return ItemLicense.MindShock;

      // SKILLS

      if (equals("LICENSE_ANCHORING"))
        return ItemLicense.Anchoring;

      if (equals("LICENSE_FLYING"))
        return ItemLicense.Flying;

      if (equals("LICENSE_INVISIBLE"))
        return ItemLicense.Invisible;

      if (equals("license_detect"))
        return ItemLicense.Detect;

      if (equals("LICENSE_SHIELD"))
        return ItemLicense.Shield;

      if (equals("LICENSE_BLOCK"))
        return ItemLicense.Block;

      if (equals("LICENSE_BIND"))
        return ItemLicense.Bind;

      if (equals("LICENSE_METALLIC"))
        return ItemLicense.Metallic;

      throw new Exception("Invalid license " + license);
    }

    private static Gender ParseGender(string gender)
    {
      Func<string, bool> equals = str => gender.Equals(str, StringComparison.InvariantCultureIgnoreCase);

      if (equals("all"))
        return Gender.None;

      if (equals("woman"))
        return Gender.Female;

      if (equals("man"))
        return Gender.Male;
      return Gender.None;
      //throw new Exception("Invalid gender "+ gender);
    }

    private static Gender ParseGender_2(string gender)
    {
      if (gender == "man")
        return Gender.Male;

      if (gender == "woman")
        return Gender.Female;

      if (gender == "unisex")
        return Gender.None;

      return Gender.None;
      //throw new Exception("Invalid gender "+ gender);
    }

    private static ItemInfo LoadAction(ItemNumber id, ItemInfoItemDto itemDto)
    {
      if (itemDto.action == null)
      {
        Logger.Warning("Missing action for item {id}", id);
        return new ItemInfoAction();
      }

      var item = new ItemInfoAction
      {
        RequiredMP = float.Parse(itemDto.action.ability.required_mp, CultureInfo.InvariantCulture),
        DecrementMP = float.Parse(itemDto.action.ability.decrement_mp, CultureInfo.InvariantCulture),
        DecrementMPDelay = float.Parse(itemDto.action.ability.decrement_mp_delay, CultureInfo.InvariantCulture)
      };

      if (itemDto.action.@float != null)
        item.ValuesF = itemDto.action.@float
            .Select(f => float.Parse(f.value.Replace("f", ""), CultureInfo.InvariantCulture)).ToList();

      if (itemDto.action.integer != null)
        item.Values = itemDto.action.integer.Select(i => i.value).ToList();

      return item;
    }

    private static ItemInfo LoadWeapon(ItemNumber id, ItemInfoItemDto itemDto)
    {
      if (itemDto.weapon == null)
        return new ItemInfoWeapon();

      var ability = itemDto.weapon.ability;
      var item = new ItemInfoWeapon
      {
        Type = ability.type,
        RateOfFire = float.Parse(ability.rate_of_fire, CultureInfo.InvariantCulture),
        Power = float.Parse(ability.power, CultureInfo.InvariantCulture),
        MoveSpeedRate = float.Parse(ability.move_speed_rate, CultureInfo.InvariantCulture),
        AttackMoveSpeedRate = float.Parse(ability.attack_move_speed_rate, CultureInfo.InvariantCulture),
        MagazineCapacity = ability.magazine_capacity,
        CrackedMagazineCapacity = ability.cracked_magazine_capacity,
        MaxAmmo = ability.max_ammo,
        Accuracy = float.Parse(ability.accuracy, CultureInfo.InvariantCulture),
        Range = string.IsNullOrWhiteSpace(ability.range)
              ? 0
              : float.Parse(ability.range, CultureInfo.InvariantCulture),
        SupportSniperMode = ability.support_sniper_mode > 0,
        SniperModeFov = ability.sniper_mode_fov > 0,
        AutoTargetDistance = ability.auto_target_distance == null
              ? 0
              : float.Parse(ability.auto_target_distance, CultureInfo.InvariantCulture)
      };

      if (itemDto.weapon.@float != null)
        item.ValuesF = itemDto.weapon.@float
            .Select(f => float.Parse(f.value.Replace("f", ""), CultureInfo.InvariantCulture)).ToList();

      if (itemDto.weapon.integer != null)
        item.Values = itemDto.weapon.integer.Select(i => i.value).ToList();

      return item;
    }

    public IEnumerable<ItemNumber> GetWorkingCapsules()
    {
      var dto = Deserialize<AddCapsuleDto>("xml/_eu_item_tooltip_addcapsule.x7");
      var capsules = new List<ItemNumber>();

      foreach (var capsule in dto.Item)
      {
        var hasItem = false;

        var item = capsule.Capsule_icon;
        var color = capsule.Color_index;
        var effect = capsule.Capsule_info;
        var slot = capsule.Capsule_slot;

        var items = new ConcurrentDictionary<ItemNumber, int>();
        var effects = new ConcurrentDictionary<int, List<CapsuleReward>>(); // Todo
        var slots = new ConcurrentStack<int>();
        var colors = new ConcurrentDictionary<int, int>();

        slots.Push(0);

        // Prepare Slots

        #region Prepare Slots

        if (int.TryParse(slot.Slot_1, out var Slot_1))
        {
          effects.TryAdd(Slot_1, new List<CapsuleReward>());
          slots.Push(Slot_1);
        }

        if (int.TryParse(slot.Slot_2, out var Slot_2))
        {
          effects.TryAdd(Slot_2, new List<CapsuleReward>());
          slots.Push(Slot_2);
        }

        if (int.TryParse(slot.Slot_3, out var Slot_3))
        {
          effects.TryAdd(Slot_3, new List<CapsuleReward>());
          slots.Push(Slot_3);
        }

        if (int.TryParse(slot.Slot_4, out var Slot_4))
        {
          effects.TryAdd(Slot_4, new List<CapsuleReward>());
          slots.Push(Slot_4);
        }

        if (int.TryParse(slot.Slot_5, out var Slot_5))
        {
          effects.TryAdd(Slot_5, new List<CapsuleReward>());
          slots.Push(Slot_5);
        }

        if (int.TryParse(slot.Slot_6, out var Slot_6))
        {
          effects.TryAdd(Slot_6, new List<CapsuleReward>());
          slots.Push(Slot_6);
        }

        if (int.TryParse(slot.Slot_7, out var Slot_7))
        {
          effects.TryAdd(Slot_7, new List<CapsuleReward>());
          slots.Push(Slot_7);
        }

        if (int.TryParse(slot.Slot_8, out var Slot_8))
        {
          effects.TryAdd(Slot_8, new List<CapsuleReward>());
          slots.Push(Slot_8);
        }

        if (int.TryParse(slot.Slot_9, out var Slot_9))
        {
          effects.TryAdd(Slot_9, new List<CapsuleReward>());
          slots.Push(Slot_9);
        }

        if (int.TryParse(slot.Slot_10, out var Slot_10))
        {
          effects.TryAdd(Slot_10, new List<CapsuleReward>());
          slots.Push(Slot_10);
        }

        if (int.TryParse(slot.Slot_11, out var Slot_11))
        {
          effects.TryAdd(Slot_11, new List<CapsuleReward>());
          slots.Push(Slot_11);
        }

        if (int.TryParse(slot.Slot_15, out var Slot_15))
        {
          effects.TryAdd(Slot_15, new List<CapsuleReward>());
          slots.Push(Slot_15);
        }

        if (int.TryParse(slot.Slot_16, out var Slot_16))
        {
          effects.TryAdd(Slot_16, new List<CapsuleReward>());
          slots.Push(Slot_16);
        }

        if (int.TryParse(slot.Slot_14, out var Slot_14))
        {
          effects.TryAdd(Slot_14, new List<CapsuleReward>());
          slots.Push(Slot_14);
        }

        if (int.TryParse(slot.Slot_12, out var Slot_12))
        {
          effects.TryAdd(Slot_12, new List<CapsuleReward>());
          slots.Push(Slot_12);
        }

        if (int.TryParse(slot.Slot_13, out var Slot_13))
        {
          effects.TryAdd(Slot_13, new List<CapsuleReward>());
          slots.Push(Slot_13);
        }

        #endregion

        #region Read Rewards 

        if (effects.TryGetValue(Slot_1, out var List_1) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_1),
                out CapsuleReward Effect_1))
          List_1.Add(Effect_1);

        if (effects.TryGetValue(Slot_2, out var List_2) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_2),
                out CapsuleReward Effect_2))
          List_2.Add(Effect_2);

        if (effects.TryGetValue(Slot_3, out var List_3) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_3),
                out CapsuleReward Effect_3))
          List_3.Add(Effect_3);

        if (effects.TryGetValue(Slot_4, out var List_4) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_4),
                out CapsuleReward Effect_4))
          List_4.Add(Effect_4);

        if (effects.TryGetValue(Slot_5, out var List_5) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_5),
                out CapsuleReward Effect_5))
          List_5.Add(Effect_5);

        if (effects.TryGetValue(Slot_6, out var List_6) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_6),
                out CapsuleReward Effect_6))
          List_6.Add(Effect_6);

        if (effects.TryGetValue(Slot_7, out var List_7) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_7),
                out CapsuleReward Effect_7))
          List_7.Add(Effect_7);

        if (effects.TryGetValue(Slot_8, out var List_8) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_8),
                out CapsuleReward Effect_8))
          List_8.Add(Effect_8);

        if (effects.TryGetValue(Slot_9, out var List_9) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_9),
                out CapsuleReward Effect_9))
          List_9.Add(Effect_9);

        if (effects.TryGetValue(Slot_10, out var List_10) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_10),
                out CapsuleReward Effect_10))
          List_10.Add(Effect_10);

        if (effects.TryGetValue(Slot_11, out var List_11) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_11),
                out CapsuleReward Effect_11))
          List_11.Add(Effect_11);

        if (effects.TryGetValue(Slot_14, out var List_14) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_14),
                out CapsuleReward Effect_14))
          List_14.Add(Effect_14);

        if (effects.TryGetValue(Slot_15, out var List_15) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_15),
                out CapsuleReward Effect_15))
          List_15.Add(Effect_15);

        if (effects.TryGetValue(Slot_16, out var List_16) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_16),
                out CapsuleReward Effect_16))
          List_16.Add(Effect_16);

        if (effects.TryGetValue(Slot_12, out var List_12) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_12),
                out CapsuleReward Effect_12))
          List_12.Add(Effect_12);

        if (effects.TryGetValue(Slot_13, out var List_13) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_13),
                out CapsuleReward Effect_13))
          List_13.Add(Effect_13);

        #endregion

        // Read Items

        #region Read Items

        if (int.TryParse(item.ID_1, out var ID_1))
          hasItem = true;

        if (int.TryParse(item.ID_2, out var ID_2))
          hasItem = true;

        if (int.TryParse(item.ID_3, out var ID_3))
          hasItem = true;

        if (int.TryParse(item.ID_4, out var ID_4))
          hasItem = true;

        if (int.TryParse(item.ID_5, out var ID_5))
          hasItem = true;

        if (int.TryParse(item.ID_6, out var ID_6))
          hasItem = true;

        if (int.TryParse(item.ID_7, out var ID_7))
          hasItem = true;

        if (int.TryParse(item.ID_8, out var ID_8))
          hasItem = true;

        if (int.TryParse(item.ID_9, out var ID_9))
          hasItem = true;

        if (int.TryParse(item.ID_10, out var ID_10))
          hasItem = true;

        if (int.TryParse(item.ID_11, out var ID_11))
          hasItem = true;

        if (int.TryParse(item.ID_15, out var ID_15))
          hasItem = true;

        if (int.TryParse(item.ID_16, out var ID_16))
          hasItem = true;

        if (int.TryParse(item.ID_14, out var ID_14))
          hasItem = true;

        if (int.TryParse(item.ID_12, out var ID_12))
          hasItem = true;

        if (int.TryParse(item.ID_13, out var ID_13))
          hasItem = true;

        #endregion

        if (hasItem)
          capsules.Add(int.Parse(capsule.Id));
      }

      return capsules;
    }

    public IEnumerable<AddCapsule> LoadCapsules()
    {
      var dto = Deserialize<AddCapsuleDto>("xml/_eu_item_tooltip_addcapsule.x7");

      var caps = new ConcurrentDictionary<ItemNumber, AddCapsule>();

      foreach (var capsule in dto.Item)
      {
        var retval = new AddCapsule(int.Parse(capsule.Id));

        var item = capsule.Capsule_icon;
        var color = capsule.Color_index;
        var effect = capsule.Capsule_info;
        var slot = capsule.Capsule_slot;

        var items = new ConcurrentDictionary<ItemNumber, int>();
        var effects = new ConcurrentDictionary<int, List<CapsuleReward>>(); // Todo
        var slots = new ConcurrentStack<int>();
        var colors = new ConcurrentDictionary<int, int>();
        var names = new ConcurrentDictionary<int, List<string>>();

        slots.Push(0);
        effects.TryAdd(0, new List<CapsuleReward>());

        // Prepare Slots

        #region Prepare Slots

        if (int.TryParse(slot.Slot_1, out var Slot_1))
        {
          effects.TryAdd(Slot_1, new List<CapsuleReward>());
          slots.Push(Slot_1);
        }

        if (int.TryParse(slot.Slot_2, out var Slot_2))
        {
          effects.TryAdd(Slot_2, new List<CapsuleReward>());
          slots.Push(Slot_2);
        }

        if (int.TryParse(slot.Slot_3, out var Slot_3))
        {
          effects.TryAdd(Slot_3, new List<CapsuleReward>());
          slots.Push(Slot_3);
        }

        if (int.TryParse(slot.Slot_4, out var Slot_4))
        {
          effects.TryAdd(Slot_4, new List<CapsuleReward>());
          slots.Push(Slot_4);
        }

        if (int.TryParse(slot.Slot_5, out var Slot_5))
        {
          effects.TryAdd(Slot_5, new List<CapsuleReward>());
          slots.Push(Slot_5);
        }

        if (int.TryParse(slot.Slot_6, out var Slot_6))
        {
          effects.TryAdd(Slot_6, new List<CapsuleReward>());
          slots.Push(Slot_6);
        }

        if (int.TryParse(slot.Slot_7, out var Slot_7))
        {
          effects.TryAdd(Slot_7, new List<CapsuleReward>());
          slots.Push(Slot_7);
        }

        if (int.TryParse(slot.Slot_8, out var Slot_8))
        {
          effects.TryAdd(Slot_8, new List<CapsuleReward>());
          slots.Push(Slot_8);
        }

        if (int.TryParse(slot.Slot_9, out var Slot_9))
        {
          effects.TryAdd(Slot_9, new List<CapsuleReward>());
          slots.Push(Slot_9);
        }

        if (int.TryParse(slot.Slot_10, out var Slot_10))
        {
          effects.TryAdd(Slot_10, new List<CapsuleReward>());
          slots.Push(Slot_10);
        }

        if (int.TryParse(slot.Slot_11, out var Slot_11))
        {
          effects.TryAdd(Slot_11, new List<CapsuleReward>());
          slots.Push(Slot_11);
        }

        if (int.TryParse(slot.Slot_15, out var Slot_15))
        {
          effects.TryAdd(Slot_15, new List<CapsuleReward>());
          slots.Push(Slot_15);
        }

        if (int.TryParse(slot.Slot_16, out var Slot_16))
        {
          effects.TryAdd(Slot_16, new List<CapsuleReward>());
          slots.Push(Slot_16);
        }

        if (int.TryParse(slot.Slot_14, out var Slot_14))
        {
          effects.TryAdd(Slot_14, new List<CapsuleReward>());
          slots.Push(Slot_14);
        }

        if (int.TryParse(slot.Slot_12, out var Slot_12))
        {
          effects.TryAdd(Slot_12, new List<CapsuleReward>());
          slots.Push(Slot_12);
        }

        if (int.TryParse(slot.Slot_13, out var Slot_13))
        {
          effects.TryAdd(Slot_13, new List<CapsuleReward>());
          slots.Push(Slot_13);
        }

        #endregion

        // Read Colors

        #region Read Colors 

        if (int.TryParse(color.Color_1, out var Color_1))
          colors.TryAdd(Slot_1, Color_1);

        if (int.TryParse(color.Color_2, out var Color_2))
          colors.TryAdd(Slot_2, Color_2);

        if (int.TryParse(color.Color_3, out var Color_3))
          colors.TryAdd(Slot_3, Color_3);

        if (int.TryParse(color.Color_4, out var Color_4))
          colors.TryAdd(Slot_4, Color_4);

        if (int.TryParse(color.Color_5, out var Color_5))
          colors.TryAdd(Slot_5, Color_5);

        if (int.TryParse(color.Color_6, out var Color_6))
          colors.TryAdd(Slot_6, Color_6);

        if (int.TryParse(color.Color_7, out var Color_7))
          colors.TryAdd(Slot_7, Color_7);

        if (int.TryParse(color.Color_8, out var Color_8))
          colors.TryAdd(Slot_8, Color_8);

        if (int.TryParse(color.Color_9, out var Color_9))
          colors.TryAdd(Slot_9, Color_9);

        if (int.TryParse(color.Color_10, out var Color_10))
          colors.TryAdd(Slot_10, Color_10);

        if (int.TryParse(color.Color_16, out var Color_16))
          colors.TryAdd(Slot_16, Color_16);

        #endregion

        // Read Rewards

        #region Read Rewards 

        if (effects.TryGetValue(Slot_1, out var List_1) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_1),
                out CapsuleReward Effect_1))
          List_1.Add(Effect_1);

        if (effects.TryGetValue(Slot_2, out var List_2) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_2),
                out CapsuleReward Effect_2))
          List_2.Add(Effect_2);

        if (effects.TryGetValue(Slot_3, out var List_3) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_3),
                out CapsuleReward Effect_3))
          List_3.Add(Effect_3);

        if (effects.TryGetValue(Slot_4, out var List_4) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_4),
                out CapsuleReward Effect_4))
          List_4.Add(Effect_4);

        if (effects.TryGetValue(Slot_5, out var List_5) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_5),
                out CapsuleReward Effect_5))
          List_5.Add(Effect_5);

        if (effects.TryGetValue(Slot_6, out var List_6) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_6),
                out CapsuleReward Effect_6))
          List_6.Add(Effect_6);

        if (effects.TryGetValue(Slot_7, out var List_7) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_7),
                out CapsuleReward Effect_7))
          List_7.Add(Effect_7);

        if (effects.TryGetValue(Slot_8, out var List_8) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_8),
                out CapsuleReward Effect_8))
          List_8.Add(Effect_8);

        if (effects.TryGetValue(Slot_9, out var List_9) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_9),
                out CapsuleReward Effect_9))
          List_9.Add(Effect_9);

        if (effects.TryGetValue(Slot_10, out var List_10) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_10),
                out CapsuleReward Effect_10))
          List_10.Add(Effect_10);

        if (effects.TryGetValue(Slot_11, out var List_11) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_11),
                out CapsuleReward Effect_11))
          List_11.Add(Effect_11);

        if (effects.TryGetValue(Slot_14, out var List_14) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_14),
                out CapsuleReward Effect_14))
          List_14.Add(Effect_14);

        if (effects.TryGetValue(Slot_15, out var List_15) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_15),
                out CapsuleReward Effect_15))
          List_15.Add(Effect_15);

        if (effects.TryGetValue(Slot_16, out var List_16) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_16),
                out CapsuleReward Effect_16))
          List_16.Add(Effect_16);

        if (effects.TryGetValue(Slot_12, out var List_12) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_12),
                out CapsuleReward Effect_12))
          List_12.Add(Effect_12);

        if (effects.TryGetValue(Slot_13, out var List_13) &&
            Enum.TryParse(AddCapsule.ConvertCapsuleReward(effect.Effect_key_13),
                out CapsuleReward Effect_13))
          List_13.Add(Effect_13);

        #endregion

        // Read Items

        #region Read Items

        if (int.TryParse(item.ID_1, out var ID_1))
          items.TryAdd(ID_1, Slot_1);

        if (int.TryParse(item.ID_2, out var ID_2))
          items.TryAdd(ID_2, Slot_2);

        if (int.TryParse(item.ID_3, out var ID_3))
          items.TryAdd(ID_3, Slot_3);

        if (int.TryParse(item.ID_4, out var ID_4))
          items.TryAdd(ID_4, Slot_4);

        if (int.TryParse(item.ID_5, out var ID_5))
          items.TryAdd(ID_5, Slot_5);

        if (int.TryParse(item.ID_6, out var ID_6))
          items.TryAdd(ID_6, Slot_6);

        if (int.TryParse(item.ID_7, out var ID_7))
          items.TryAdd(ID_7, Slot_7);

        if (int.TryParse(item.ID_8, out var ID_8))
          items.TryAdd(ID_8, Slot_8);

        if (int.TryParse(item.ID_9, out var ID_9))
          items.TryAdd(ID_9, Slot_9);

        if (int.TryParse(item.ID_10, out var ID_10))
          items.TryAdd(ID_10, Slot_10);

        if (int.TryParse(item.ID_11, out var ID_11))
          items.TryAdd(ID_11, Slot_11);

        if (int.TryParse(item.ID_15, out var ID_15))
          items.TryAdd(ID_15, Slot_15);

        if (int.TryParse(item.ID_16, out var ID_16))
          items.TryAdd(ID_16, Slot_16);

        if (int.TryParse(item.ID_14, out var ID_14))
          items.TryAdd(ID_14, Slot_14);

        if (int.TryParse(item.ID_12, out var ID_12))
          items.TryAdd(ID_12, Slot_12);

        if (int.TryParse(item.ID_13, out var ID_13))
          items.TryAdd(ID_13, Slot_13);

        #endregion

        var prizes = new List<AddCapsuleReward>();

        foreach (var iSlot in slots)
          prizes.Add(new AddCapsuleReward(iSlot));

        var shop = GameServer.Instance.ResourceCache.GetShop();
        var xitems = GameServer.Instance.ResourceCache.GetItems();

        foreach (var iItem in items)
        {
          if (slots.Contains(iItem.Value))
          {
            var xitem = xitems.FirstOrDefault(x => x.Key == iItem.Key);

            if (xitem.Value == null)
              continue;

            if (xitem.Value?.Name.Contains("(7/15/30") ?? false)
            {
              var xitem2 = xitems.FirstOrDefault(x =>
                  x.Value.Name.Trim().Contains(
                      xitem.Value?.Name
                          .Replace("(7/15/30/Permanent)", "(Permanent)")
                          .Replace("(7/15/30)", "(Permanent)")
                          .Replace("(7/15/30 Days/perm)", "(perm)")
                          .Trim()));

              if (xitem2.Value == null)
              {
                xitem2 = xitems.FirstOrDefault(x =>
                    x.Value.Name.Trim().Contains(
                        xitem.Value?.Name
                            .Replace("(7/15/30/Permanent)", " (Permanent)")
                            .Replace("(7/15/30)", " (Permanent)")
                            .Replace("(7/15/30 Days/perm)", " (perm)")
                            .Trim()));
              }

              if (xitem2.Value == null)
              {
                xitem2 = xitems.FirstOrDefault(x =>
                    x.Value.Name.Trim().Equals(
                        xitem.Value?.Name
                            .Replace("(7/15/30/Permanent)", string.Empty)
                            .Replace("(7/15/30)", string.Empty)
                            .Replace("(7/15/30 Days/perm)", string.Empty)
                            .Trim()));
              }

              if (xitem2.Value == null)
              {
                xitem2 = xitems.FirstOrDefault(x =>
                    x.Value.Name.Trim().Contains(
                        xitem.Value?.Name
                            .Replace("(AP)", " ")
                            .Replace("(7/15/30/Permanent)", "(Permanent)")
                            .Replace("(7/15/30)", "(Permanent)")
                            .Replace("(7/15/30 Days/perm)", "(perm)")
                            .Trim()));
              }

              if (xitem2.Value == null)
              {
                xitem2 = xitems.FirstOrDefault(x =>
                    x.Value.Name.Trim().Contains(
                        xitem.Value?.Name
                            .Replace("(AP)", " ")
                            .Replace("(7/15/30/Permanent)", " (Permanent)")
                            .Replace("(7/15/30)", " (Permanent)")
                            .Replace("(7/15/30 Days/perm)", " (perm)")
                            .Trim()));
              }

              if (xitem2.Value == null)
              {
                xitem2 = xitems.FirstOrDefault(x =>
                    x.Value.Name.Trim().Equals(
                        xitem.Value?.Name
                            .Replace("(AP)", " ")
                            .Replace("(7/15/30/Permanent)", string.Empty)
                            .Replace("(7/15/30)", string.Empty)
                            .Replace("(7/15/30 Days/perm)", string.Empty)
                            .Trim()));
              }

              if (xitem2.Value != null)
                items.TryAdd(xitem2.Key, iItem.Value);

              continue;
            }

            if (shop.Items.TryGetValue(iItem.Key, out var shopItem))
            {
              var prize = prizes.FirstOrDefault(x => x.SlotId == iItem.Value);
              if (prize != null)
              {
                colors.TryGetValue(iItem.Value, out var iColor);

                var colorAvailable =
                    shop.Items.Any(x => x.Key == iItem.Key && x.Value.ColorGroup > iColor);
                if (!colorAvailable)
                  iColor = 0;

                var existing = false;
                if (!names.ContainsKey(prize.SlotId))
                  names.TryAdd(prize.SlotId, new List<string>());
                names.TryGetValue(prize.SlotId, out var namelist);

                var name = xitem.Value?.Name
                    .Replace("(1 Day)", $"{prize.SlotId}-T")
                    .Replace("(7 Days)", $"{prize.SlotId}-T")
                    .Replace("(15 Days)", $"{prize.SlotId}-T")
                    .Replace("(30 Days)", $"{prize.SlotId}-T")
                    .Replace("(Permanent)", $"{prize.SlotId}-T")
                    .Trim() ?? "";

                if (namelist.Contains(name))
                  existing = true;
                else
                  namelist.Add(name);

                if (!existing)
                {
                  prize.Items.Add(shopItem, iColor);
                  retval.Ready = true;
                }
              }
            }
          }
        }

        foreach (var iEffect in effects)
        {
          if (slots.Contains(iEffect.Key))
          {
            var prize = prizes.FirstOrDefault(x => x.SlotId == iEffect.Key);
            prize.Rewards.AddRange(iEffect.Value);
          }
        }

        foreach (var iPrize in prizes)
        {
          if (iPrize.Items.Any() || iPrize.Rewards.Any())
            retval.Prizes.TryAdd(iPrize.SlotId, iPrize);
        }

        if (retval.Prizes.Any())
          caps.TryAdd(retval.CapsuleItemId, retval);
      }

      return caps.Values;

            //Values has Capsule itemid and prizes for it 
            // prizes is a dictionary containing slot id , shopitems , rewards
            //prizes.rewards contains effects
            //shop items are the items in shop
        }

        public IEnumerable<CapsuleRewards> LoadItemRewards()
    {
      var dto = Deserialize<ItemRewardDto>("xml/ItemBag.xml");
      if (dto?.Items != null)
        foreach (var it in dto.Items)
        {
          var ret = new CapsuleRewards { Item = it.Number, Bags = new List<BagReward>() };

          if (it.Groups != null)
          {
            foreach (var group in it.Groups)
            {
              var bag = new BagReward
              {
                Bag = new List<ItemReward>()
              };

              if (group.Rewards != null)
              {
                foreach (var rw in group.Rewards)
                {
                  var PEN = (CapsuleRewardType)rw.Type == CapsuleRewardType.PEN ? rw.Value : 0;
                  var Period = (CapsuleRewardType)rw.Type == CapsuleRewardType.PEN ? 0 : rw.Value;

                  bag.Bag.Add(new ItemReward
                  {
                    Type = (CapsuleRewardType)rw.Type,
                    Item = rw.Data,
                    PriceType = (ItemPriceType)rw.PriceType,
                    PeriodType = (ItemPeriodType)rw.PeriodType,
                    Period = Period,
                    PEN = PEN,
                    Effects = rw.Effects.Split(",").Select(e => uint.Parse(e)).ToArray(),
                    Rate = rw.Rate,
                    Color = rw.Color,
                    Value = rw.Value
                  });
                }
              }

              ret.Bags.Add(bag);
            }
          }

          yield return ret;
        }
    }

    #endregion
  }
}