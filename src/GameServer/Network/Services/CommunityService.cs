using System;
using System.Linq;
using BlubLib.DotNetty.Handlers.MessageHandling;
using ExpressMapper.Extensions;
using NeoNetsphere.Network.Data.Chat;
using NeoNetsphere.Network.Message.Chat;
using NeoNetsphere.Database.Auth;
using NeoNetsphere.Database.Game;
using ProudNetSrc.Handlers;
using Dapper;
using Dapper.FastCrud;
using NeoNetsphere.Network.Message.Game;
using Serilog;
using Serilog.Core;

namespace NeoNetsphere.Network.Services
{
  internal class CommunityService : ProudMessageHandler
  {
    private static readonly ILogger Logger =
        Log.ForContext(Constants.SourceContextPropertyName, nameof(CommunityService));

    [MessageHandler(typeof(OptionSaveCommunityReqMessage))]
    public void OptionSaveCommunityReq(ChatSession session, OptionSaveCommunityReqMessage message)
    {
      var plr = session.Player;

      plr.Settings.AddOrUpdate("AllowCombiInvite", message.AllowCombi);
      plr.Settings.AddOrUpdate("AllowFriendRequest", message.AllowFriendReq);
      plr.Settings.AddOrUpdate("AllowRoomInvite", message.AllowInvite);
      plr.Settings.AddOrUpdate("AllowInfoRequest", message.RevealInfo);
    }

    [MessageHandler(typeof(OptionSaveBinaryReqMessage))]
    public void OptionSaveBinaryReq(ChatSession session, OptionSaveBinaryReqMessage message)
    {
      //ToDo
    }

    [MessageHandler(typeof(UserDataOneReqMessage))]
    public void GetUserDataHandler(ChatSession session, UserDataOneReqMessage message)
    {
      var plr = session.Player;
      if (plr.Account.Id == message.AccountId)
        return;

      if (!plr.Channel.Players.TryGetValue(message.AccountId, out var target))
        return;

      /* TO DO
      switch (target.Settings.Get<CommunitySetting>("AllowInfoRequest"))
      {
          case CommunitySetting.Deny:
              // Not sure if there is an answer to this
              return;

          case CommunitySetting.FriendOnly:
              // ToDo
              return;
      }
      */
      session.SendAsync(new UserDataFourAckMessage(0, target.Map<Player, UserDataDto>()));
    }

    [MessageHandler(typeof(FriendActionReqMessage))]
    public void FriendActionRequest(ChatSession session, FriendActionReqMessage message)
    {
      var plr = session.Player;
      if (message.AccountId == plr.Account.Id)
        return;

      using (var authdb = AuthDatabase.Open())
      using (var db = GameDatabase.Open())
      {
        var target = DbUtil.Find<AccountDto>(authdb, statement => statement
            .Where($"{nameof(AccountDto.Id):C} = @Id")
            .WithParameters(new { Id = message.AccountId })).FirstOrDefault();

        var targetPlayerAccount = DbUtil.Find<PlayerDto>(db, statement => statement
            .Where($"{nameof(PlayerDto.Id):C} = @Id")
            .WithParameters(new { Id = message.AccountId })).FirstOrDefault();

        if (target == null || targetPlayerAccount == null)
        {
          plr.SendAsync(new FriendActionAckMessage
          {
            Friend = new FriendDto(),
            Result = FriendResult.UserNotExist,
            Unk = 0
          });
          return;
        }

        var targetPlayer = GameServer.Instance.PlayerManager.Get(message.AccountId);
        session.Player.FriendManager.GetValue(message.AccountId, out var friend);

        switch (message.Action)
        {
          case FriendAction.Add:
            if (friend != null)
            {
              plr.SendAsync(new ServerResultAckMessage(ServerResult.DBError));
              return;
            }

            var settingMananger = new PlayerSettingManager(null, targetPlayerAccount);
            switch (settingMananger.GetSetting("AllowFriendRequest"))
            {
              case CommunitySetting.Allow:
                friend = plr.FriendManager.AddOrUpdate(message.AccountId, targetPlayer,
                    FriendState.Requesting, FriendState.RequestDialog);

                session.SendAsync(new FriendActionAckMessage
                {
                  Friend = friend.GetFriend(),
                  Result = FriendResult.Ok,
                  Unk = 0
                });

                if (targetPlayer != null)
                {
                  targetPlayer.ChatSession?.SendAsync(new FriendActionAckMessage
                  {
                    Friend = friend.GetPlayer(),
                    Result = FriendResult.Ok,
                    Unk = 0
                  });
                }

                break;
              case CommunitySetting.Deny:
                session.SendAsync(new FriendActionAckMessage
                {
                  Friend = new FriendDto(),
                  Result = FriendResult.UserNotExist,
                  Unk = 0
                });
                break;
            }

            break;
          case FriendAction.Decline:
          case FriendAction.Remove:
            if (friend == null)
            {
              plr.SendAsync(new ServerResultAckMessage(ServerResult.DBError));
              return;
            }

            friend.PlayerState = FriendState.NotInList;
            friend.FriendState = FriendState.NotInList;
            plr.FriendManager.Remove(message.AccountId, targetPlayer);

            session.SendAsync(new FriendActionAckMessage
            {
              Friend = friend.GetFriend(),
              Result = FriendResult.Ok,
              Unk = 0
            });
            session.SendAsync(
                new FriendListAckMessage(plr.FriendManager.Select(d => d.GetFriend())
                    .Where(x => x.State != 0).ToArray()));

            if (targetPlayer != null)
            {
              targetPlayer.ChatSession?.SendAsync(new FriendActionAckMessage
              {
                Friend = friend.GetPlayer(),
                Result = FriendResult.Ok,
                Unk = 0
              });
              targetPlayer.ChatSession?.SendAsync(
                  new FriendListAckMessage(targetPlayer.FriendManager.Select(d => d.GetFriend())
                      .Where(x => x.State != 0).ToArray()));
            }

            break;
          case FriendAction.Update:
            if (friend == null)
            {
              plr.SendAsync(new ServerResultAckMessage(ServerResult.DBError));
              return;
            }

            friend = plr.FriendManager.AddOrUpdate(message.AccountId, targetPlayer,
                FriendState.InList, FriendState.InList);

            session.SendAsync(new FriendActionAckMessage
            {
              Friend = friend.GetFriend(),
              Result = FriendResult.Ok,
              Unk = 0
            });

            if (targetPlayer != null)
            {
              targetPlayer.ChatSession?.SendAsync(new FriendActionAckMessage
              {
                Friend = friend.GetPlayer(),
                Result = FriendResult.Ok,
                Unk = 0
              });
            }

            break;
          default:
            Console.WriteLine("UNKNOWN FriendAction:" + message.Action);
            break;
        }

        targetPlayer?.SendAsync(new ChatPlayerInfoAckMessage(plr.Map<Player, PlayerInfoDto>()));
        plr.SendAsync(new ChatPlayerInfoAckMessage(targetPlayerAccount.Map<PlayerDto, PlayerInfoDto>()));
      }
    }

        [MessageHandler(typeof(RoomInvitationPlayerReqMessage))]
        public void RoomInvitationPlayerRequest(ChatSession session, RoomInvitationPlayerReqMessage message)
        {
            var plr = session.Player;
            var target = GameServer.Instance.PlayerManager[message.AccountId];
            // Todo
            if (target != null)
            {
                session.SendAsync(new RoomInvitationPlayerAckMessage
                {
                    Location = target.Map<Player, PlayerLocationDto>(),
                    SenderId = message.AccountId,
                    SenderNickname = "NicknameTest"
                });
            }
        }

    [MessageHandler(typeof(DenyActionReqMessage))]
    public void DenyHandler(ChatServer service, ChatSession session, DenyActionReqMessage message)
    {
      var plr = session.Player;

      if (message.Deny.AccountId == plr.Account.Id)
        return;

      Deny deny;
      switch (message.Action)
      {
        case DenyAction.Add:
          if (plr.DenyManager.Contains(message.Deny.AccountId))
            return;

          var target = GameServer.Instance.PlayerManager[message.Deny.AccountId];
          if (target == null)
            return;

          deny = plr.DenyManager.Add(target);
          session.SendAsync(new DenyActionAckMessage(0, DenyAction.Add, deny.Map<Deny, DenyDto>()));
          break;

        case DenyAction.Remove:
          deny = plr.DenyManager[message.Deny.AccountId];
          if (deny == null)
            return;

          plr.DenyManager.Remove(message.Deny.AccountId);
          session.SendAsync(new DenyActionAckMessage(0, DenyAction.Remove, deny.Map<Deny, DenyDto>()));
          break;
      }
    }
  }
}
