using BlubLib.Serialization;
using ProudNetSrc.Serialization.Serializers;

namespace NeoNetsphere.Network.Data.Game
{
  [BlubContract]
  public class RoomPlayerDto
  {
    public RoomPlayerDto()
    {
      Nickname = "";
    }

    [BlubMember(0)] public uint ClanId { get; set; }

    [BlubMember(1)] public ulong AccountId { get; set; }

    [BlubMember(2)] public byte Unk1 { get; set; }

    [BlubMember(3, typeof(StringSerializer))]
    public string Nickname { get; set; }

    [BlubMember(4)] public byte Pos { get; set; }

    [BlubMember(5)] public bool IsGM { get; set; }

#if LATESTS4
        [BlubMember(6)]
        public byte Unk3 { get; set; }
#endif
  }
}
