using BlubLib.Serialization;

namespace NeoNetsphere.Network.Data.Game
{
  [BlubContract]
  public class RequitalGiveItemResultDto
  {
    public RequitalGiveItemResultDto()
    {
    }

    public RequitalGiveItemResultDto(ItemNumber itemNumber, int unk2)
    {
      ItemNumber = itemNumber;
      Unk2 = unk2;
    }

    [BlubMember(0)] public ItemNumber ItemNumber { get; set; }

    [BlubMember(1)] public int Unk2 { get; set; }
  }
}
