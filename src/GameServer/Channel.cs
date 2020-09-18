using System.Threading.Tasks;

namespace NeoNetsphere
{
  using System;
  using System.Collections.Concurrent;
  using System.Collections.Generic;
  using System.Drawing;
  using System.Linq;
  using ExpressMapper.Extensions;
  using Network;
  using Network.Data.Chat;
  using Network.Message.Chat;
  using Network.Message.Game;

  internal class Channel
  {
    private readonly IDictionary<ulong, Player> _players = new ConcurrentDictionary<ulong, Player>();

    public Channel()
    {
      RoomManager = new RoomManager(this);
    }

    public int Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public int PlayerLimit { get; set; }

    public byte MinLevel { get; set; }

    public byte MaxLevel { get; set; }

    public Color Color { get; set; }

    public IReadOnlyDictionary<ulong, Player> Players => (IReadOnlyDictionary<ulong, Player>)_players;

    public RoomManager RoomManager { get; }

    public void Update(AccurateDelta delta)
    {
      RoomManager?.Update(delta);
    }

    public void Join(Player plr)
    {
      if (plr.Channel != null)
        throw new ChannelException("Player is already inside a channel");

      if (Id > 0 && Players.Count >= PlayerLimit)
        throw new ChannelLimitReachedException();

      if (Id > 0 && (plr.Level < MinLevel || plr.Level > MaxLevel) &&
          plr.Account.SecurityLevel < SecurityLevel.GameSage)
        throw new ChannelLevelLimitException();

      if (Config.Instance.ACMode == 2)
      {
        plr.BE.HB_Last = TimeSpan.FromMinutes(1);
        plr.BE.HB_Last_Issued = true;
      }

      if (CollectionExtensions.TryAdd(_players, plr.Account.Id, plr))
      {
        if (Config.Instance.ResCheck)
          plr.SendAsync(new LoginReguestAckMessage(GameLoginResult.OK, plr.Account.Id));

        plr.Channel = this;

        if (Id > 0)
          plr.Session.SendAsync(new ServerResultAckMessage(ServerResult.ChannelEnter));

        BroadcastExcept(plr, new ChannelEnterPlayerAckMessage(plr.Map<Player, PlayerInfoShortDto>()));
        SendPlayerlist(plr);

        try
        {
          plr.SendAsync(new NoteCountAckMessage((byte)plr.Mailbox.Count(mail => mail.IsNew), 0, 0));
          BroadcastLocationUpdate(plr);
        }
        finally
        {
          OnPlayerJoined(new ChannelPlayerJoinedEventArgs(this, plr));
        }
      }
    }

    public void BroadcastLocationUpdate(Player plr)
    {
      var playerList = new List<Player>();
      foreach (var chatplr in plr.FriendManager)
      {
        var xplr = GameServer.Instance.PlayerManager.Get(chatplr.FriendId);
        if (xplr != null && !playerList.Contains(xplr))
          playerList.Add(xplr);
      }

      if (plr.Club?.Id > 0)
      {
        foreach (var clubPlr in plr.Club.Players.Values)
        {
          var xplr = GameServer.Instance.PlayerManager.Get(clubPlr.AccountId);
          if (xplr != null && !playerList.Contains(xplr))
            playerList.Add(xplr);
        }
      }

      foreach (var player in playerList)
      {
        player.SendAsync(new ChatPlayerInfoAckMessage(plr.Map<Player, PlayerInfoDto>()));
      }
    }

    public void SendPlayerlist(Player plr)
    {
      if (plr.Channel != this)
        return;

      var visibleplayers = Players.Values.Where(x => x.Room == null).ToList();

      plr.SendAsync(new ChannelPlayerListAckMessage(visibleplayers
          .Select(p => p.Map<Player, PlayerInfoShortDto>()).ToArray()));
      plr.SendAsync(new Chennel_PlayerNameTagList_AckMessage(visibleplayers
          .Select(p => p.Map<Player, PlayerNameTagInfoDto>()).ToArray()));
    }

    public void Leave(Player plr)
    {
      if (plr.Channel != this)
        throw new ChannelException("Player is not in this channel");

      if (CollectionExtensions.Remove(_players, plr.Account.Id, out _))
      {
        plr.Channel = null;

        try
        {
          if (Id > 0)
          {
            plr.SendAsync(new ServerResultAckMessage(ServerResult.ChannelLeave));
            BroadcastLocationUpdate(plr);
          }
        }
        finally
        {
          Broadcast(new ChannelLeavePlayerAckMessage(plr.Account.Id));
          OnPlayerLeft(new ChannelPlayerLeftEventArgs(this, plr));
        }
      }
    }

    #region Broadcast

    public void SendChatMessage(Player plr, string message)
    {
      OnMessage(new ChannelMessageEventArgs(this, plr, message));

      foreach (var p in Players.Values.Where(p => !p.DenyManager.Contains(plr.Account.Id) && p.Room == null))
      {
        p.SendAsync(new MessageChatAckMessage(ChatType.Channel, plr.Account.Id, plr.Account.Nickname, message));
      }
    }

    public void BroadcastNotice(string message)
    {
      Broadcast(new NoticeAdminMessageAckMessage(message));
    }

    public void BroadcastCencored(RoomChangeRoomInfoAck2Message message)
    {
      foreach (var plr in Players.Values.Where(plr => plr.Room?.Id == message.Room.RoomId))
        plr.SendAsync(message);

      var cencored = message.Map<RoomChangeRoomInfoAck2Message, RoomChangeRoomInfoAck2Message>();
      cencored.Room.Password =
          !string.IsNullOrWhiteSpace(message.Room.Password) || !string.IsNullOrEmpty(message.Room.Password)
              ? "nice try :)"
              : "";

      foreach (var plr in Players.Values.Where(plr => plr.Room?.Id != message.Room.RoomId || plr.Room == null))
        plr.SendAsync(cencored);
    }

    public void Broadcast(IGameMessage message)
    {
      foreach (var plr in Players.Values.Where(plr => plr.Room == null))
        plr.SendAsync(message);
    }

    public void BroadcastExcept(Player blacklisted, IGameMessage message)
    {
      foreach (var plr in Players.Values.Where(x => x.Room == null && x != blacklisted))
        plr.SendAsync(message);
    }

    public void BroadcastExcept(Player blacklisted, IChatMessage message)
    {
      foreach (var plr in Players.Values.Where(x => x.Room == null && x != blacklisted))
        plr.SendAsync(message);
    }

    public void BroadcastExcept(List<Player> blacklist, IGameMessage message)
    {
      foreach (var plr in Players.Values.Where(x => x.Room == null && !blacklist.Contains(x)))
        plr.SendAsync(message);
    }

    public void BroadcastExcept(List<Player> blacklist, IChatMessage message)
    {
      foreach (var plr in Players.Values.Where(x => x.Room == null && !blacklist.Contains(x)))
        plr.SendAsync(message);
    }

    public void Broadcast(IChatMessage message)
    {
      foreach (var plr in Players.Values.Where(plr => plr.Room == null))
        plr.SendAsync(message);
    }

    #endregion

    #region Events

    public event EventHandler<ChannelPlayerJoinedEventArgs> PlayerJoined;
    public event EventHandler<ChannelPlayerLeftEventArgs> PlayerLeft;
    public event EventHandler<ChannelMessageEventArgs> Message;

    protected virtual void OnPlayerJoined(ChannelPlayerJoinedEventArgs e)
    {
      PlayerJoined?.Invoke(this, e);
    }

    protected virtual void OnPlayerLeft(ChannelPlayerLeftEventArgs e)
    {
      PlayerLeft?.Invoke(this, e);
    }

    protected virtual void OnMessage(ChannelMessageEventArgs e)
    {
      Message?.Invoke(this, e);
    }

    #endregion
  }
}