using BlubLib.Serialization;

namespace NeoNetsphere.Network.Data.Game
{
  [BlubContract]
  public class ArenaStatsDto
  {
    [BlubMember(0)]
    public float WinRate { get; set; }
    [BlubMember(1)]
    public float KdRate { get; set; }
    [BlubMember(2)]
    public float KdPercent { get; set; }
    [BlubMember(3)]
    public float DoubleKillRate { get; set; }
    [BlubMember(4)]
    public float TripleKillRate { get; set; }
    [BlubMember(5)]
    public uint ShortestKillTime { get; set; }
    [BlubMember(6)]
    public uint LeaderSelected { get; set; }
    [BlubMember(7)]
    public uint LeaderKills { get; set; }
  }
}
