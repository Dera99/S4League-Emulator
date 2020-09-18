using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BlubLib;
using BlubLib.DotNetty.Handlers.MessageHandling;
using ExpressMapper.Extensions;
using NeoNetsphere.Network.Data.Chat;
using NeoNetsphere.Network.Data.Game;
using NeoNetsphere.Network.Message.Chat;
using NeoNetsphere.Network.Message.Game;
using ProudNetSrc;
using ProudNetSrc.Handlers;
using Serilog;
using Serilog.Core;

namespace NeoNetsphere.Network.Services
{
  internal class ChannelService : ProudMessageHandler
  {
    // ReSharper disable once InconsistentNaming
    private static readonly ILogger Logger =
        Log.ForContext(Constants.SourceContextPropertyName, nameof(ChannelService));

    [MessageHandler(typeof(ChannelInfoReqMessage))]
    public void ChannelInfoReq(GameSession session, ChannelInfoReqMessage message)
    {
      if (session.Player.Room != null)
        return;

      if (session.Player.Channel == null)
      {
        try
        {
          GameServer.Instance.ChannelManager[0].Join(session.Player);
        }
        catch (Exception)
        {
          //ignored
        }
      }

      try
      {
        switch (message.Request)
        {
          case ChannelInfoRequest.ChannelList:
            var channels = GameServer.Instance.ChannelManager.Select(c => c.Map<Channel, ChannelInfoDto>())
                .ToArray();
            channels = channels.Skip(1).ToArray();
            foreach (var channel in channels)
            {
              if (channel.Name.Contains("Clan"))
                channel.IsClanChannel = true;
            }

            session.SendAsync(new ChannelListInfoAckMessage(channels));
            break;

          case ChannelInfoRequest.RoomList:
          case ChannelInfoRequest.RoomList2:
            if (session.Player?.Channel == null)
              return;

            var roomlist2 = new List<RoomDto>();
            foreach (var room in session.Player.Channel.RoomManager)
            {
              if (room == null || room.Disposed)
                continue;

              if (!room.TeamManager.Players.Any())
                continue;

              var temproom2 = room.GetRoomInfo();
              temproom2.Password =
                  !string.IsNullOrWhiteSpace(room.Options.Password) ||
                  !string.IsNullOrEmpty(room.Options.Password)
                      ? "nice try :)"
                      : string.Empty;
              roomlist2.Add(temproom2);
            }

            session.SendAsync(new RoomListInfoAck2Message(roomlist2.ToArray()),
                SendOptions.ReliableSecureCompress);
            break;

          default:
            Logger.ForAccount(session)
                .Error("Invalid request {request}", message.Request);
            break;
        }
      }
      catch (Exception e)
      {
        Logger.Error(e.ToString());
      }

      if (session.ConnectDate.Add(TimeSpan.FromMinutes(5)) >
          DateTimeOffset.Now)
      {
        session.ConnectDate = DateTimeOffset.Now;
        session.Player.BE.EnableBE();
      }
    }

    [MessageHandler(typeof(ChannelEnterReqMessage))]
    public void ChannelEnterReq(GameSession session, ChannelEnterReqMessage message)
    {
      if (session.Player.Room != null)
        return;

      if (session.ConnectDate.Add(TimeSpan.FromMinutes(5)) >
          DateTimeOffset.Now)
      {
        session.ConnectDate = DateTimeOffset.Now;
        session.Player.BE.EnableBE();
      }

      var channel = GameServer.Instance.ChannelManager[message.Channel];
      if (channel == null)
      {
        session.SendAsync(new ServerResultAckMessage(ServerResult.NonExistingChannel));
        return;
      }

      session.Player.Channel?.Leave(session.Player);
      try
      {
        channel.Join(session.Player);
        return;
      }
      catch (ChannelLimitReachedException)
      {
        // Fix current client bug
        session.SendAsync(new ServerResultAckMessage(ServerResult.ChannelLeave));
        session.SendAsync(new ServerResultAckMessage(ServerResult.ChannelLimitReached));
      }
      catch (ChannelLevelLimitException)
      {
        // Fix current client bug
        session.SendAsync(new ServerResultAckMessage(ServerResult.ChannelLeave));
        session.SendAsync(new ServerResultAckMessage(ServerResult.JoinChannelFailed));
      }
      catch (ChannelException)
      {
        // Fix current client bug
        session.SendAsync(new ServerResultAckMessage(ServerResult.ChannelLeave));
        session.SendAsync(new ServerResultAckMessage(ServerResult.JoinChannelFailed));
      }
    }

    [MessageHandler(typeof(ChannelLeaveReqMessage))]
    public void ChannelLeaveReq(GameSession session)
    {
      if (session.Player?.Room != null)
      {
        return;
      }

      session.Player?.Channel?.Leave(session.Player);
      GameServer.Instance.ChannelManager[0].Join(session.Player);
    }

    [MessageHandler(typeof(MessageChatReqMessage))]
    public void MessageChatReq(ChatSession session, MessageChatReqMessage message)
    {
      switch (message.ChatType)
      {
        case ChatType.Channel:
          session.Player?.Channel?.SendChatMessage(session.Player, message.Message);
          break;

        case ChatType.Club:
          if (session.Player?.Club?.Id > 0)
          {
            foreach (var member in GameServer.Instance.PlayerManager.Where(p =>
                p.Club == session.Player.Club))
            {
              member.SendAsync(new MessageChatAckMessage(ChatType.Club,
                  session.Player.Account.Id,
                  session.Player.Account.Nickname, message.Message));
            }
          }

          break;
        default:
          Logger.ForAccount(session)
              .Warning("Invalid chat type {chatType}", message.ChatType);
          break;
      }
    }

    [MessageHandler(typeof(MessageWhisperChatReqMessage))]
    public async Task MessageWhisperChatReq(ChatSession session, MessageWhisperChatReqMessage message)
    {
      var toPlr = GameServer.Instance.PlayerManager.Get(message.ToNickname);
      if (string.Equals(message.ToNickname, "server", StringComparison.CurrentCultureIgnoreCase) &&
            session.Player.Account.SecurityLevel >= SecurityLevel.GameSage)
      {
        var args = message.Message.GetArgs();
        if (!await GameServer.Instance.CommandManager.Execute(session.Player, args))
        {
          await session.Player.ChatSession.SendAsync(new MessageChatAckMessage(ChatType.Channel,
              session.Player.Account.Id, "SYSTEM",
              "Unknown command! Try to contact the server administrators"));
        }
      }
      else if (string.Equals(message.ToNickname, "clan", StringComparison.CurrentCultureIgnoreCase))
      {
        var commands = new List<string> { "/clan" };
        commands.AddRange(message.Message.GetArgs());
        var args = commands.ToArray();
        if (!await GameServer.Instance.CommandManager.Execute(session.Player, args))
        {
          await session.Player.ChatSession.SendAsync(new MessageChatAckMessage(ChatType.Channel,
              session.Player.Account.Id, "ClanMgr", "An error occoured"));
        }
      }
      else
      {
        if (toPlr == null)
        {
          await session.Player.SendAsync(new MessageWhisperChatAckMessage(3, message.ToNickname,
              session.Player.Account.Id, session.Player.Account.Nickname, message.Message));
          return;
        }

        if (session.Player.DenyManager.Contains(toPlr.Account.Id))
        {
          await session.Player.SendAsync(new MessageWhisperChatAckMessage(3, message.ToNickname,
              session.Player.Account.Id, session.Player.Account.Nickname, message.Message));
          return;
        }

        // ToDo Is there an answer for this case?
        if (toPlr.DenyManager.Contains(session.Player.Account.Id))
        {
          await session.Player.SendAsync(new MessageWhisperChatAckMessage(3, message.ToNickname,
              session.Player.Account.Id, session.Player.Account.Nickname, message.Message));
          return;
        }

        try
        {
          await toPlr?.SendAsync(new ChatPlayerInfoAckMessage(session.Player.Map<Player, PlayerInfoDto>()));
        }
        finally
        {
          await toPlr.SendAsync(new MessageWhisperChatAckMessage(0, toPlr.Account.Nickname,
              session.Player.Account.Id, session.Player.Account.Nickname, message.Message));
        }
      }
    }

    [MessageHandler(typeof(ChannellistReqMessage))]
    public void Channellistreq(ChatSession session, ChannellistReqMessage message)
    {
      session.Player?.Channel.SendPlayerlist(session.Player);
    }
  }
}