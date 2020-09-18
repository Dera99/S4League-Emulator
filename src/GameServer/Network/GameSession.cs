using DotNetty.Transport.Channels;
using ProudNetSrc;

namespace NeoNetsphere.Network
{
  internal class GameSession : ProudSession
  {
    //public ChatSession ChatSession { get; set; }

    public GameSession(uint hostId, IChannel channel, ProudServer server)
        : base(hostId, channel, server)
    {
      UpdateShop = false;
      ValidRes = 0;
      Player = null;
    }

    public Player Player { get; set; }
    public bool UpdateShop;
    public byte ValidRes;
  }

  internal class GameSessionFactory : ISessionFactory
  {
    public ProudSession Create(uint hostId, IChannel channel, ProudServer server)
    {
      return new GameSession(hostId, channel, server);
    }
  }
}