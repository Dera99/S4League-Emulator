﻿using System.Collections.Generic;

namespace NeoNetsphere.Resource
{
  public class ItemInfo
  {
    public ItemNumber ItemNumber { get; set; }
    public string Name { get; set; }
    public int Level { get; set; }
    public int MasterLevel { get; set; }
    public ItemLicense License { get; set; }
    public Gender Gender { get; set; }
    public int Colors { get; set; }
    public string Image { get; set; }

    public override string ToString()
    {
      return Name + "- " + ItemNumber;
    }
  }

  public class ItemInfoAction : ItemInfo
  {
    public ItemInfoAction()
    {
      ValuesF = new List<float>();
      Values = new List<int>();
    }

    public float RequiredMP { get; set; }
    public float DecrementMP { get; set; }
    public float DecrementMPDelay { get; set; }
    public IList<float> ValuesF { get; set; }
    public IList<int> Values { get; set; }
  }

  public class ItemInfoWeapon : ItemInfo
  {
    public byte Type { get; set; }
    public float RateOfFire { get; set; }
    public float Power { get; set; }
    public float MoveSpeedRate { get; set; }
    public float AttackMoveSpeedRate { get; set; }
    public int MagazineCapacity { get; set; }
    public int CrackedMagazineCapacity { get; set; }
    public int MaxAmmo { get; set; }
    public float Accuracy { get; set; }
    public float Range { get; set; }
    public bool SupportSniperMode { get; set; }
    public bool SniperModeFov { get; set; }
    public float AutoTargetDistance { get; set; }
    public IList<float> ValuesF { get; set; }
    public IList<int> Values { get; set; }
  }
}