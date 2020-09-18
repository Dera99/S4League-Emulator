using System.Xml.Serialization;

namespace NeoNetsphere.Resource.xml
{
  [XmlType(AnonymousType = true)]
  [XmlRoot(Namespace = "", IsNullable = true, ElementName = "ItemReward")]
  public class ItemRewardDto
  {
    [XmlElement("item")] public ItemDto[] Items { get; set; }
  }

  [XmlType(AnonymousType = true)]
  public class ItemDto
  {
    [XmlAttribute] public uint Number { get; set; }

    [XmlElement("group")] public RewardGroup[] Groups { get; set; }
  }

  [XmlType(AnonymousType = true)]
  public class RewardGroup
  {
    [XmlElement("reward")] public RewardDto[] Rewards { get; set; }
  }

  [XmlType(AnonymousType = true)]
  public class RewardDto
  {
    // 1: PEN
    // 2: Item
    [XmlAttribute] public uint Type { get; set; }

    // itemNumber
    [XmlAttribute] public uint Data { get; set; }

    // PriceType
    // Pen, Ap, Premium, Cp
    [XmlAttribute] public uint PriceType { get; set; } 

    // PeriodType
    // Permanent, Day, Hours, Unity
    [XmlAttribute] public uint PeriodType { get; set; }

    /// <summary>
    ///     Gets or Set Color of item
    /// </summary>
    [XmlAttribute]
    public byte Color { get; set; }

    // Period or Pen
    [XmlAttribute] public uint Value { get; set; }

    [XmlAttribute] public string Effects { get; set; }

    [XmlAttribute] public uint Rate { get; set; }
  }
}
