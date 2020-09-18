using BlubLib.Serialization;

namespace NeoNetsphere.Network.Data.Chat
{
  [BlubContract]
  public class PlayerLocationDto
  {
    public PlayerLocationDto()
    {
      ServerGroupId = -1;
      RoomId = -1;
      Unk = -1;
      ChannelId = -1;
      GameServerId = -1;
      ChatServerId = -1;
    }

    [BlubMember(0)] public int ServerGroupId { get; set; }

    [BlubMember(2)] public int ChannelId { get; set; }

    [BlubMember(3)] public int RoomId { get; set; }

    [BlubMember(4)] public int Unk { get; set; }

    [BlubMember(5)] public int GameServerId { get; set; }

    [BlubMember(6)] public int ChatServerId { get; set; }
  }
}
