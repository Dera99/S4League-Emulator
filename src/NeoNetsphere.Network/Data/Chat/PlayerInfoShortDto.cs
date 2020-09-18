using BlubLib.Serialization;
using ProudNetSrc.Serialization.Serializers;

namespace NeoNetsphere.Network.Data.Chat
{
  [BlubContract]
  public class PlayerInfoShortDto
  {
    public PlayerInfoShortDto(ulong accountId, string nickname, uint totalExp, bool isGm)
    {
      AccountId = accountId;
      Nickname = nickname;
      TotalExp = totalExp;
      IsGM = isGm;
    }

    public PlayerInfoShortDto()
    {
      Nickname = "";
      IsGM = false;
    }

    [BlubMember(0)] public ulong AccountId { get; set; }

    [BlubMember(1, typeof(StringSerializer))]
    public string Nickname { get; set; }

    [BlubMember(2)] public int Unk { get; set; }

    [BlubMember(3)] public uint TotalExp { get; set; }

    [BlubMember(4)] public bool IsGM { get; set; }
  }
}
