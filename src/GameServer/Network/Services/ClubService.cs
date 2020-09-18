namespace NeoNetsphere.Network.Services
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using BlubLib.DotNetty.Handlers.MessageHandling;
  using BlubLib.Threading.Tasks;
  using Dapper.FastCrud;
  using ExpressMapper.Extensions;
  using NeoNetsphere.Database.Auth;
  using NeoNetsphere.Database.Game;
  using NeoNetsphere.Network.Data.Chat;
  using NeoNetsphere.Network.Data.Club;
  using NeoNetsphere.Network.Data.Game;
  using NeoNetsphere.Network.Message.Chat;
  using NeoNetsphere.Network.Message.Club;
  using NeoNetsphere.Network.Message.Game;
  using ProudNetSrc.Handlers;
  using Serilog;
  using Serilog.Core;

  internal class ClubService : ProudMessageHandler
  {
    // ReSharper disable once InconsistentNaming
    private static readonly ILogger Logger =
        Log.ForContext(Constants.SourceContextPropertyName, nameof(ClubService));

    private readonly AsyncLock _sync = new AsyncLock();

    public static async Task Update(GameSession session = null, bool broadcast = false)
    {
      if (session == null && broadcast == false)
        return;
      var targets = new List<GameSession>();
      if (broadcast)
        targets.AddRange(GameServer.Instance.Sessions.Values.Cast<GameSession>());
      else
        targets.Add(session);

      foreach (var proudSession in targets)
      {
        var plr = proudSession?.Player;

        if (plr != null)
        {
          Club.LogOff(plr, true);
          plr.Club = GameServer.Instance.ClubManager.GetClubByAccount(plr.Account.Id);
          await proudSession.SendAsync(new ClubMyInfoAckMessage(plr.Map<Player, ClubMyInfoDto>()));
          Club.LogOn(plr, true);

          if (plr.Room != null)
          {
            await plr.Session.SendAsync(new ClubClubInfoAckMessage(plr.Map<Player, ClubInfoDto>()));
            await plr.Session.SendAsync(new ClubClubInfoAck2Message(plr.Map<Player, ClubInfoDto2>()));
          }
        }
      }

      foreach (var channel in GameServer.Instance.ChannelManager)
      {
        foreach (var room in channel.RoomManager.Where(x => x.Players.Any(y => y.Value.Club?.Id != 0)))
        {
          room.Broadcast(new RoomPlayerInfoListForEnterPlayerAckMessage(room.TeamManager.Players
              .Select(r => r.Room.GetRoomPlrDto(r)).ToArray()));
          var clubList = new List<PlayerClubInfoDto>();

          foreach (var player in room.TeamManager.Players.Where(p => p.Club != null))
          {
            if (clubList.All(club => club.Id != player.Club.Id))
              clubList.Add(player.Map<Player, PlayerClubInfoDto>());
          }

          room.Broadcast(new RoomClubInfoListForEnterPlayerAckMessage(clubList.ToArray()));
        }
      }
    }

    [MessageHandler(typeof(ClubClubInfoReqMessage))]
    public void ClubClubInfoReq(GameSession session, ClubClubInfoReqMessage message)
    {
      var plr = session.Player;
      if (plr == null)
        return;

      session.SendAsync(new ClubClubInfoAckMessage(plr.Map<Player, ClubInfoDto>()));
    }

    [MessageHandler(typeof(ClubClubInfoReq2Message))]
    public void ClubClubInfoReq2(GameSession session, ClubClubInfoReq2Message message)
    {
      var plr = session.Player;
      if (plr == null)
        return;

      session.SendAsync(new ClubClubInfoAck2Message(plr.Map<Player, ClubInfoDto2>()));
    }

    [MessageHandler(typeof(ClubInfoReqMessage))]
    public void ClubInfoReq(GameSession session, ClubInfoReqMessage message)
    {
      var plr = session.Player;
      if (plr == null)
        return;

      session.SendAsync(new ClubInfoAckMessage(plr.Map<Player, PlayerClubInfoDto>()));
    }

    [MessageHandler(typeof(ClubJoinWaiterInfoReqMessage))]
    public void ClubJoinWaiterInfoReq(GameSession session, ClubJoinWaiterInfoReqMessage message)
    {
      //Todo
      session.SendAsync(new ClubJoinWaiterInfoAckMessage());
    }

    [MessageHandler(typeof(ClubStuffListReqMessage))]
    public void ClubStuffListReq(GameSession session, ClubStuffListReqMessage message)
    {
      //Todo
      session.SendAsync(new ClubStuffListAckMessage());
    }

    [MessageHandler(typeof(ClubStuffListReq2Message))]
    public void ClubStuffListReq2(GameSession session, ClubStuffListReq2Message message)
    {
      //Todo
      session.SendAsync(new ClubStuffListAck2Message());
    }

    [MessageHandler(typeof(ClubSearchReqMessage))]
    public void ClubSearchReq(GameSession session, ClubSearchReqMessage message)
    {
      //Todo
      session.SendAsync(new ClubSearchAckMessage { Unk1 = 1 });
    }

    [MessageHandler(typeof(ClubNameCheckReqMessage))]
    public void ClubNameCheckReq(GameSession session, ClubNameCheckReqMessage message)
    {
      var plr = session.Player;
      if (plr == null)
        return;

      var ascii = Config.Instance.Game.NickRestrictions.AsciiOnly;

      if (!Namecheck.IsNameValid(message.Name, true) ||
          ascii && message.Name.Any(c => c > 127) ||
          !ascii && message.Name.Any(c => c > 255))
      {
        session.SendAsync(new NickCheckAckMessage(true));
        return;
      }

      if (GameServer.Instance.ClubManager.Any(c => c.ClanName == message.Name))
      {
        session.SendAsync(new ClubNameCheckAckMessage(2));
        return;
      }

      session.SendAsync(new ClubNameCheckAckMessage(0));
    }

    [MessageHandler(typeof(ClubCreateReqMessage))]
    public async Task ClubCreateReq(GameSession session, ClubCreateReqMessage message)
    {
      await ClubCreateReq2(session, message.Map<ClubCreateReqMessage, ClubCreateReq2Message>());
    }

    [MessageHandler(typeof(ClubCreateReq2Message))]
    public async Task ClubCreateReq2(GameSession session, ClubCreateReq2Message message)
    {
      var plr = session.Player;
      if (plr == null)
        return;

      var ascii = Config.Instance.Game.NickRestrictions.AsciiOnly;
      if (GameServer.Instance.ClubManager.Any(c =>
              c.ClanName == message.Name || c.Players.ContainsKey(plr.Account.Id)) ||
          !Namecheck.IsNameValid(message.Name, true) ||
          ascii && message.Name.Any(c => c > 127) ||
          !ascii && message.Name.Any(c => c > 255))
      {
        Logger.ForAccount(plr).Information($"Couldnt create Clan : {message.Name}");
        await session.SendAsync(new ClubCreateAck2Message(1));
      }
      else
      {
        var clubDto = new ClubDto
        {
          Name = message.Name,
          Icon = ""
        };

        using (var db = GameDatabase.Open())
        {
          try
          {
            using (var transaction = DbUtil.BeginTransaction(db))
            {
              await DbUtil.InsertAsync(db, clubDto,
                  statement => statement.AttachToTransaction(transaction));

              var clubPlayerInfo = new ClubPlayerInfo
              {
                AccountId = session.Player.Account.Id,
                Account = session.Player.Account.AccountDto,
                State = ClubState.Joined,
                Rank = ClubRank.Master
              };

              var club = new Club(clubDto, new[] { clubPlayerInfo });
              GameServer.Instance.ClubManager.Add(club);
              transaction.Commit();

              var clubplrdto = new ClubPlayerDto
              {
                PlayerId = (int)session.Player.Account.Id,
                ClubId = club.Id,
                Rank = (byte)ClubRank.Master,
                State = (int)ClubState.Joined
              };
              await DbUtil.InsertAsync(db, clubplrdto);

              session.Player.Club = club;
            }
          }
          catch (Exception ex)
          {
            Logger.Error(ex.ToString());
            await session.SendAsync(new ClubCreateAck2Message(1));
            return;
          }

          await session.SendAsync(new ClubCreateAck2Message(0));
          await session.SendAsync(new ClubMyInfoAckMessage(plr.Map<Player, ClubMyInfoDto>()));
          Club.LogOn(plr);
        }
      }
    }

    [MessageHandler(typeof(ClubRankListReqMessage))]
    public void ClubRankListReq(GameSession session, ClubRankListReqMessage message)
    {
      var plr = session.Player;
      if (plr == null)
        return;

      //Todo
      session.SendAsync(new ClubRankListAckMessage());
    }

    [MessageHandler(typeof(ClubAddressReqMessage))]
    public void CClubAddressReq(GameSession session, ClubAddressReqMessage message)
    {
      //Todo
      session.SendAsync(new ClubAddressAckMessage("", 0));
    }

    [MessageHandler(typeof(ClubClubMemberInfoReq2Message))]
    public void ClubClubMemberInfoReq2(ChatSession session, ClubClubMemberInfoReq2Message message)
    {
      var targetplr = GameServer.Instance.PlayerManager[message.AccountId];
      if (targetplr?.Club?.Id > 0)
      {
        var isMod = targetplr.Club.Players.Any(x =>
            x.Value.Rank == ClubRank.Master && x.Key == targetplr.Account.Id);
        session.SendAsync(new ClubClubMemberInfoAck2Message
        {
          ClanId = message.ClanId,
          AccountId = targetplr.Account.Id,
          Nickname = targetplr.Account.Nickname,
          IsModerator = isMod ? 1 : 0
        });
      }
      else if (session.Player != null && targetplr != null)
      {
        session.SendAsync(new ClubClubMemberInfoAck2Message
        {
          ClanId = message.ClanId,
          AccountId = targetplr.Account.Id,
          Nickname = targetplr.Account.Nickname
        });
      }
      else
      {
        session.SendAsync(new ClubClubMemberInfoAck2Message
        {
          ClanId = message.ClanId,
          AccountId = 0,
          Nickname = "n/A"
        });
      }
    }

    [MessageHandler(typeof(ClubMemberListReqMessage))]
    public void ClubMemberListReq(ChatSession session, ClubMemberListReqMessage message)
    {
      var plr = session.Player;
      if (plr?.Club?.Id > 0)
      {
        var clanMembers = new List<ClubMemberDto>();

        clanMembers.AddRange(GameServer.Instance.PlayerManager
            .Where(p => plr.Club.Players.Keys.Contains(p.Account.Id))
            .Select(p => p.Map<Player, ClubMemberDto>()));
        clanMembers.AddRange(plr.Club.Players.Select(x => x.Value.Map<ClubPlayerInfo, ClubMemberDto>()));

        plr.ChatSession.SendAsync(new ClubMemberListAckMessage(plr.Club.Id, clanMembers.ToArray()));
      }
      else
      {
        plr?.ChatSession.SendAsync(new ClubMemberListAckMessage());
      }
    }

    [MessageHandler(typeof(ClubMemberListReq2Message))]
    public void ClubMemberListReq2(ChatSession session, ClubMemberListReq2Message message)
    {
      var plr = session.Player;
      if (plr?.Club?.Id > 0)
      {
        var clanMembers = new List<ClubMemberDto2>();

        clanMembers.AddRange(GameServer.Instance.PlayerManager
            .Where(p => plr.Club.Players.Keys.Contains(p.Account.Id))
            .Select(p => p.Map<Player, ClubMemberDto2>()));
        clanMembers.AddRange(plr.Club.Players.Select(x => x.Value.Map<ClubPlayerInfo, ClubMemberDto2>()));

        plr.ChatSession.SendAsync(new ClubMemberListAck2Message(plr.Club.Id, clanMembers.ToArray()));
      }
      else
      {
        plr?.ChatSession.SendAsync(new ClubMemberListAck2Message());
      }
    }

    [MessageHandler(typeof(ClubNoteSendReq2Message))]
    public void ClubNoteSendReq2(ChatSession session, ClubNoteSendReq2Message message)
    {
      //Todo
      session.GameSession?.SendAsync(new ClubNoteSendAckMessage { Unk = 1 });
    }

    [MessageHandler(typeof(ClubNoticePointRefreshReqMessage))]
    public void ClubNoticePointRefreshReq(GameSession session, ClubNoticePointRefreshReqMessage message)
    {
      //Todo
      session.SendAsync(new ClubNoticePointRefreshAckMessage());
    }

    [MessageHandler(typeof(ClubNoticeRecordRefreshReqMessage))]
    public void ClubNoticeRecordRefreshReq(GameSession session, ClubNoticeRecordRefreshReqMessage message)
    {
      //Todo
      session.SendAsync(new ClubNoticeRecordRefreshAckMessage());
    }

    [MessageHandler(typeof(ClubUnjoinReqMessage))]
    public async Task ClubUnjoinReq(GameSession session, ClubUnjoinReqMessage message)
    {
      await ClubUnjoinReq2(session, message.Map<ClubUnjoinReqMessage, ClubUnjoinReq2Message>());
    }

    [MessageHandler(typeof(ClubUnjoinReq2Message))]
    public async Task ClubUnjoinReq2(GameSession session, ClubUnjoinReq2Message message)
    {
      var plr = session.Player;
      if (plr?.Club == null || plr.Club.Id != message.ClanId)
      {
        await session.SendAsync(new ClubUnjoinAck2Message(4));
        return;
      }

      if (plr.Club.Players.Values.Any(x =>
          x.Account?.Id == (int)plr.Account.Id && x.Rank != ClubRank.Master))
      {
        using (var db = GameDatabase.Open())
        {
          var club = (await DbUtil.FindAsync<ClubDto>(db, statement => statement
              .Where($"{nameof(ClubDto.Id):C} = @Id")
              .WithParameters(new { plr.Club.Id }))).FirstOrDefault();

          if (club != null)
          {
            var player = (await DbUtil.FindAsync<ClubPlayerDto>(db, statement => statement
                    .Where($"{nameof(ClubPlayerDto.ClubId):C} = @Id")
                    .WithParameters(new { plr.Club.Id }))
                ).FirstOrDefault(x => x.PlayerId == (int)plr.Account.Id);

            if (player != null)
            {
              Club.LogOff(plr);
              plr.Club.Players.TryRemove(plr.Account.Id, out var _);
              DbUtil.Delete(db, new ClubPlayerDto { PlayerId = player.PlayerId });
              plr.Club = null;
              await session.SendAsync(new ClubMyInfoAckMessage(plr.Map<Player, ClubMyInfoDto>()));
              await session.SendAsync(new ClubUnjoinAck2Message());
            }
            else
            {
              await session.SendAsync(new ServerResultAckMessage(ServerResult.CantReadClanInfo));
            }
          }
          else
          {
            await session.SendAsync(new ServerResultAckMessage(ServerResult.CantReadClanInfo));
          }
        }
      }
      else
      {
        await session.SendAsync(new ServerResultAckMessage(ServerResult.DBError));
      }
    }

    [MessageHandler(typeof(ClubCloseReqMessage))]
    public async Task ClubCloseReq(GameSession session, ClubCloseReqMessage message)
    {
      await ClubCloseReq2(session, message.Map<ClubCloseReqMessage, ClubCloseReq2Message>());
    }

    [MessageHandler(typeof(ClubCloseReq2Message))]
    public async Task ClubCloseReq2(GameSession session, ClubCloseReq2Message message)
    {
      var plr = session.Player;
      if (plr?.Club == null || plr.Club.Id != message.ClanId)
      {
        await session.SendAsync(new ClubCloseAck2Message(1));
        return;
      }

      if (plr.Club.Players.Any(x => x.Key == plr.Account.Id && x.Value.Rank == ClubRank.Master))
      {
        using (var db = GameDatabase.Open())
        {
          var club = (await DbUtil.FindAsync<ClubDto>(db, statement => statement
              .Where($"{nameof(ClubDto.Id):C} = @Id")
              .WithParameters(new { plr.Club.Id }))).FirstOrDefault();

          if (club != null)
          {
            var players = await DbUtil.FindAsync<ClubPlayerDto>(db, statement => statement
                .Where($"{nameof(ClubPlayerDto.ClubId):C} = @Id")
                .WithParameters(new { plr.Club.Id }));

            foreach (var member in players)
              await DbUtil.DeleteAsync(db, member);

            DbUtil.Delete(db, club);

            foreach (var member in plr.Club.Players)
              plr.Club.Players.TryRemove(member.Key, out _);

            GameServer.Instance.ClubManager.Remove(plr.Club);

            await session.SendAsync(new ClubCloseAck2Message());
            foreach (var member in GameServer.Instance.PlayerManager.Where(x => x.Club?.Id == club.Id))
            {
              Club.LogOff(member);
              member.Club = null;
              member.Session?.SendAsync(new ClubMyInfoAckMessage(member.Map<Player, ClubMyInfoDto>()));
            }
          }
        }
      }
    }

    [MessageHandler(typeof(ClubAdminMasterChangeReqMessage))]
    public async Task ClubAdminMasterChangeReq(GameSession session, ClubAdminMasterChangeReqMessage message)
    {
      var plr = session.Player;
      if (plr?.Club?.Id > 0)
      {
        if (await plr.Club.ChangeMaster(plr, message.Target))
        {
          await plr.SendAsync(new ClubAdminMasterChangeAckMessage(ClanMasterChangeMessage.Ok));
          return;
        }
        else
        {
          await plr.SendAsync(new ClubAdminMasterChangeAckMessage(ClanMasterChangeMessage.MemberNotHaveAuthority));
          return;
        }
      }

      if (plr != null)
        await plr.SendAsync(new ClubAdminMasterChangeAckMessage(ClanMasterChangeMessage.NotInClan));
    }

    [MessageHandler(typeof(ClubAdminJoinCommandReqMessage))]
    public async Task ClubAdminJoinCommandReq(GameSession session, ClubAdminJoinCommandReqMessage message)
    {
      Logger.Information($"ClubAdminJoinCommandReqMessage => " + message.Unk1 + ", " + message.Unk2);
      var plr = session.Player;
      if (plr.Club?.Id > 0)
      {
        if (plr.Club.Players[plr.Account.Id].Rank <= ClubRank.Staff)
        {
        }
      }

      await session.SendAsync(new ServerResultAckMessage(ServerResult.CantReadClanInfo));
      //await session.SendAsync(new ClubAdminJoinCommandAckMessage(1, new ulong[0]));
    }

    [MessageHandler(typeof(ClubAdminGradeChangeReqMessage))]
    public async Task ClubAdminGradeChangeReq(GameSession session, ClubAdminGradeChangeReqMessage message)
    {
      Logger.Information($"ClubAdminGradeChangeReqMessage => " + message.Unk);
      var plr = session.Player;
      if (plr.Club?.Id > 0)
      {
        if (plr.Club.Players[plr.Account.Id].Rank <= ClubRank.Staff)
        {
        }
      }

      await session.SendAsync(new ServerResultAckMessage(ServerResult.CantReadClanInfo));
    }

    [MessageHandler(typeof(ClubAdminInviteReqMessage))]
    public void ClubAdminInviteReq(GameSession session, ClubAdminInviteReqMessage message)
    {
      // Todo
      var plr = session.Player;
      if (plr.Club?.Id > 0)
      {
        if (plr.Club.Players[plr.Account.Id].Rank <= ClubRank.Staff)
        {
          var target = GameServer.Instance.PlayerManager[message.AccountId];
          if (target != null)
          {
            plr.Club.SendInvite(plr, target);
            session.SendAsync(new ClubAdminInviteAckMessage(0));
            return;
          }

          // Todo custom result
        }

        // Todo custom result
      }

      session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
    }

    [MessageHandler(typeof(ClubJoinReq2Message))]
    public async Task ClubJoinReq2(GameSession session, ClubJoinReq2Message message)
    {
      var plr = session.Player;
      if (plr == null)
        return;

      if (plr.Club?.Id > 0)
      {
        await plr.SendAsync(new ServerResultAckMessage(ServerResult.CantReadClanInfo));
        return;
      }

      var targetClan = GameServer.Instance.ClubManager.GetClub(message.ClanId);
      if (targetClan == null)
      {
        await plr.SendAsync(new ServerResultAckMessage(ServerResult.DBError));
        return;
      }

      var invitepattern = $"<Note Key =\"4\"Srl =\"{targetClan.Id}\"";

      var mail = plr.Mailbox.FirstOrDefault(x => x.Message.StartsWith(invitepattern) && x.IsClan);
      if (mail != null)
      {
        await targetClan.AddPlayer(plr.Account.Id);
        await plr.SendAsync(new ClubJoinAck2Message(0));
        plr.Mailbox.Remove(new[] { mail });
      }
    }
  }
}