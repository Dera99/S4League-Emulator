using System.Net;
using BlubLib.Serialization;
using ProudNetSrc.Serialization.Serializers;

namespace AuthServer.ServiceModel
{
  [BlubContract]
  public class ServerInfoDto
  {
    [BlubMember(0)] public string ApiKey { get; set; }

    [BlubMember(1)] public byte Id { get; set; }

    [BlubMember(2)] public string Name { get; set; }

    [BlubMember(3)] public ushort PlayerLimit { get; set; }

    [BlubMember(4)] public ushort PlayerOnline { get; set; }

    [BlubMember(5, typeof(IPEndPointSerializer))]
    public IPEndPoint EndPoint { get; set; }

    [BlubMember(6, typeof(IPEndPointSerializer))]
    public IPEndPoint ChatEndPoint { get; set; }
  }
}
