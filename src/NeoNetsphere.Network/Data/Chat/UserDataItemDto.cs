using BlubLib.Serialization;
using NeoNetsphere.Network.Serializers;

namespace NeoNetsphere.Network.Data.Chat
{
  [BlubContract]
  public class UserDataItemDto
  {
    [BlubMember(0)]
    public int Unk1 { get; set; } //slot?

    [BlubMember(1)]
    public int Unk2 { get; set; } //id?

    [BlubMember(2)]
    public int Unk3 { get; set; } //?

    [BlubMember(3)]
    public short Unk4 { get; set; } //?

    [BlubMember(4)]
    public byte Unk5 { get; set; } //?

    [BlubMember(5, typeof(ArrayWithIntPrefixSerializer))]
    public int[] Effects { get; set; } //?
  }
}
