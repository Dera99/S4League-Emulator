using System.Threading.Tasks;

namespace NeoNetsphere.Network.Services
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using BlubLib.DotNetty.Handlers.MessageHandling;
  using ExpressMapper.Extensions;
  using NeoNetsphere.Game.GameRules;
  using NeoNetsphere.Network.Data.Game;
  using NeoNetsphere.Network.Data.GameRule;
  using NeoNetsphere.Network.Message.Game;
  using NeoNetsphere.Network.Message.GameRule;
  using NeoNetsphere.Game.GameRules;
  using ProudNetSrc.Handlers;
  using Serilog;
  using Serilog.Core;

  internal class RoomService : ProudMessageHandler
  {
    // ReSharper disable once InconsistentNaming
    private static readonly ILogger Logger =
        Log.ForContext(Constants.SourceContextPropertyName, nameof(RoomService));

    [MessageHandler(typeof(RoomAutoMixingTeamReqMessage))]
    public async Task RoomAutoMixingTeamReq(GameSession session, RoomAutoMixingTeamReqMessage message)
    {
      var plr = session.Player;
      var room = plr.Room;
      if (room == null)
        return;

      if (plr != plr.Room.Master || plr.Room.GameState != GameState.Waiting)
        return;

      foreach (var member in room.Players.Values.Shuffle())
      {
        var team = room.TeamManager.Keys.Shuffle().FirstOrDefault();
        room.TeamManager.ChangeTeam(member, team);
      }
    }

    [MessageHandler(typeof(RoomInfoRequestReqMessage))]
    public void RoomInfoRequestReq(GameSession session, RoomInfoRequestReqMessage message)
    {
      var plr = session.Player;
      var room = plr.Channel.RoomManager[message.RoomId];
      if (room == null)
        return;

      session.SendAsync(new RoomInfoRequestAck2Message
      {
        Info = new RoomInfoRequestDto
        {
          MasterName = room.Master.Account.Nickname,
          MasterLevel = room.Master.Level,
          ScoreLimit = room.Options.ScoreLimit,
          TimeLimit = room.Options.TimeLimit,
          State = room.GameState,
          IsMasterInClan = false, // Todo
          Unk8 = 1,
          Unk9 = 1
        }
      });
    }

    [MessageHandler(typeof(RoomEnterPlayerReqMessage))]
    public void CEnterPlayerReq(GameSession session)
    {
      var plr = session.Player;

      if (plr == null)
        return;

      if (plr.Room == null)
      {
        Logger.Information("{0} | Failed to join room, not in any room.", plr.Account.Nickname);
        session.SendAsync(new ServerResultAckMessage(ServerResult.CannotFindRoom));
        return;
      }

      plr.RoomInfo.IsConnecting = false;

      if (!plr.Room.ChangeMasterIfNeeded(plr))
        plr.SendAsync(new RoomChangeMasterAckMessage(plr.Room.Master.Account.Id));

      if (!plr.Room.ChangeHostIfNeeded(plr))
        plr.SendAsync(new RoomChangeRefereeAckMessage(plr.Room.Host.Account.Id));

      plr.Room.Broadcast(new RoomEnterPlayerForBookNameTagsAckMessage
      {
        AccountId = plr.Account.Id,
        Team = plr.RoomInfo.Team.Team,
        PlayerGameMode = plr.RoomInfo.Mode,
        Exp = plr.TotalExperience,
        Nickname = plr.Account.Nickname,
        Unk1 = 1,
        Unk2 = 0
      });

      plr.Room.Broadcast(new RoomEnterPlayerInfoListForNameTagAckMessage(plr.Room.Players.Values
          .Select(player => new NameTagDto(player.Account.Id, 0)).ToArray()));

      plr.Room.SendBriefing(plr);
      plr.Room.GameRuleManager.GameRule.RoomJoinCompleted(plr);
    }

    [MessageHandler(typeof(RoomMakeReqMessage))]
    public async Task CMakeRoomReq(GameSession session, RoomMakeReqMessage message)
    {
      await CMakeRoomReq2(session, message.Map<RoomMakeReqMessage, RoomMakeReq2Message>());
    }

    [MessageHandler(typeof(RoomMakeReq2Message))]
    public async Task CMakeRoomReq2(GameSession session, RoomMakeReq2Message message)
    {
      try
      {
        var plr = session.Player;

        if (plr?.Room != null)
        {
          await session.SendAsync(new ServerResultAckMessage(ServerResult.ImpossibleToEnterRoom));
          return;
        }

        if (plr?.Channel == null)
        {
          // Official result
          await session.SendAsync(new ServerResultAckMessage(ServerResult.JoinChannelFailed));
          return;
        }

        if (plr?.Channel?.Id < 1)
        {
          await session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToCreateRoom));
          return;
        }

        var israndom = false;
        var maps = GameServer.Instance.ResourceCache.GetMaps();
        var map = maps
            .FirstOrDefault(x => x.Value.byteId == message.MapId && x.Value.GameRule == message.GameRule)
            .Value;

        if (!plr.Channel?.RoomManager.GameRuleFactory.Contains(message.GameRule) ?? false)
        {
          Logger.ForAccount(plr)
              .Error("Game rule {0} does not exist", message.GameRule);
          await session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToCreateRoom));
          return;
        }

        if (map == null)
        {
          Logger.ForAccount(plr)
              .Error("Map {map} does not exist", message.MapId);
          await session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToCreateRoom));
          return;
        }

        if (map.IsRandom && map.GameRule == message.GameRule)
        {
          israndom = true;
          var modeMaps = maps.Where(x => x.Value.GameRule == message.GameRule && !x.Value.IsRandom);
          var selmap = modeMaps.ElementAtOrDefault(new SecureRandom().Next(0, modeMaps.Count()));
          message.MapId = (byte)selmap.Key;
        }

        map = maps.GetValueOrDefault(message.MapId);

        Logger.ForAccount(plr)
            .Information("CreateRoom || Room: {mode}, {mapid}", message.GameRule, message.MapId);

        if (message.GameRule != GameRule.Practice &&
            message.GameRule != GameRule.CombatTrainingTD &&
            message.GameRule != GameRule.CombatTrainingDM)
        {
          if (map.GameRule != message.GameRule)
          {
            Logger.ForAccount(plr).Error("Map {mapId}({mapName}) is not available for game rule {gameRule}",
                map.Id, map.Name, message.GameRule);
            await session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToCreateRoom));
            return;
          }

          if (message.GameRule == GameRule.Practice)
          {
            if (!Namecheck.IsNameValid(message.Name, true))
            {
              await session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
              return;
            }
          }
        }

        if (message.PlayerLimit > map.MaxPlayers)
        {
          Logger.ForAccount(plr).Error("Wrong playerLimit for Map {0}", map.Id);
          await session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToCreateRoom));
          return;
        }

        var isfriendly = false;
        var isburning = false;
        var isWithoutStats = false;
        var isNoIntrusion = message.GameRule == GameRule.Horde;

        if (message.GameRule == GameRule.CombatTrainingDM ||
            message.GameRule == GameRule.CombatTrainingTD ||
            message.GameRule == GameRule.Practice)
        {
          isfriendly = true;
          isNoIntrusion = true;
          message.PlayerLimit = 1;
        }

        switch (message.FMBURNMode)
        {
          case 0:
            isfriendly = false;
            break;
          case 1:
            isfriendly = true;
            break;
          case 2:
            isfriendly = false;
            isburning = true;
            break;
          case 3:
            isburning = true;
            isfriendly = true;
            break;
          case 4:
            isWithoutStats = true;
            break;
          case 5:
            isWithoutStats = isfriendly = true;
            break;
        }

        var room = plr.Channel.RoomManager.Create_2(
            new RoomCreationOptions
            {
              Name = message.Name,
              GameRule = message.GameRule,
              PlayerLimit = message.PlayerLimit,
              TimeLimit = TimeSpan.FromMinutes(message.TimeLimit),
              ScoreLimit = (ushort)message.ScoreLimit,
              Password = message.Password,
              IsFriendly = isfriendly,
              IsBurning = isburning,
              IsWithoutStats = isWithoutStats,
              MapId = message.MapId,
              ItemLimit = (byte)message.WeaponLimit,
              IsNoIntrusion = isNoIntrusion,
              SpectatorLimit = message.SpectatorLimit,
              IsRandom = israndom,
              HasSpectator = message.SpectatorLimit > 0,
              UniqueId = message.CreationId,
              ServerEndPoint =
                    new IPEndPoint(IPAddress.Parse(Config.Instance.IP), Config.Instance.RelayListener.Port),
              Creator = plr
            }, RelayServer.Instance.P2PGroupManager.Create(true));

        room.Join(plr);
        plr.Channel?.RoomManager._rooms.TryAdd(room.Id, room);
      }
      catch (RoomAccessDeniedException)
      {
        await session.SendAsync(new ServerResultAckMessage(ServerResult.CantEnterBecauseKicked));
      }
      catch (RoomLimitReachedException)
      {
        await session.SendAsync(new ServerResultAckMessage(ServerResult.CantEnterRoom));
      }
      catch (RoomLimitIsNoIntrutionException)
      {
        await session.SendAsync(new ServerResultAckMessage(ServerResult.ImpossibleToEnterRoom));
      }
      catch (RoomException)
      {
        await session.SendAsync(new ServerResultAckMessage(ServerResult.ImpossibleToEnterRoom));
      }
      catch (Exception ex)
      {
        Logger.Error(ex.ToString());
        await session.SendAsync(new ServerResultAckMessage(ServerResult.ImpossibleToEnterRoom));
      }
    }

    [MessageHandler(typeof(RoomChoiceMasterChangeReqMessage))]
    public void RoomChoiceMasterChangeReq(GameSession session, RoomChoiceMasterChangeReqMessage message)
    {
      var plr = session.Player;
      if (plr == null)
        return;

      if (plr.Room == null)
        return;

      if (plr.Room.Master != plr)
        return;

      var targetplayer = GameServer.Instance.PlayerManager.FirstOrDefault(target =>
          target.Room == plr.Room && target.Account.Id == message.AccountId);

      if (targetplayer == null)
      {
        plr.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
        return;
      }

      plr.Room.ChangeMasterIfNeeded(targetplayer, true);
      plr.Room.ChangeHostIfNeeded(targetplayer, true);
    }

    [MessageHandler(typeof(RoomChoiceTeamChangeReqMessage))]
    public void CMixChangeTeamReq(GameSession session, RoomChoiceTeamChangeReqMessage message)
    {
      var plr = session.Player;
      if (plr != plr.Room.Master || plr.Room.GameState != GameState.Waiting)
        return;

      var plrToMove = plr.Room.Players.GetValueOrDefault(message.PlayerToMove);
      var plrToReplace = plr.Room.Players.GetValueOrDefault(message.PlayerToReplace);
      var fromTeam = plr.Room.TeamManager[message.FromTeam];
      var toTeam = plr.Room.TeamManager[message.ToTeam];

      var room = plr.Room;

      if (fromTeam == null || toTeam == null || plrToMove == null ||
          fromTeam != plrToMove.RoomInfo.Team ||
          (plrToReplace != null && toTeam != plrToReplace.RoomInfo.Team))
      {
        session.SendAsync(new RoomMixedTeamBriefingInfoAckMessage());
        return;
      }

      if (plrToReplace == null)
      {
        try
        {
          room.TeamManager.Join(plrToMove);
        }
        catch (TeamLimitReachedException)
        {
          session.SendAsync(new RoomChoiceTeamChangeFailAckMessage());
        }
      }
      else
      {
        room.TeamManager.ChangeTeam(plrToMove, toTeam.Team);
        room.TeamManager.ChangeTeam(plrToReplace, fromTeam.Team);

        plr.Room.Broadcast(new RoomChoiceTeamChangeAckMessage(plrToMove.Account.Id, plrToReplace.Account.Id,
            fromTeam.Team, toTeam.Team));
        plr.Room.BroadcastBriefing();
      }
    }

    [MessageHandler(typeof(InGamePlayerResponseReqMessage))]
    public void InGamePlayerResponseReq(GameSession session, InGamePlayerResponseReqMessage message)
    {
      var plr = session.Player;
      if (plr?.Room == null || plr.RoomInfo == null || plr.RoomInfo?.State == PlayerState.Lobby)
        return;

      plr.RoomInfo.State = PlayerState.Alive;
    }

    [MessageHandler(typeof(RoomEnterReqMessage))]
    public async Task CGameRoomEnterReq(GameSession session, RoomEnterReqMessage message)
    {
      try
      {
        var plr = session.Player;
        if (plr.Room != null || plr.Channel == null)
        {
          await session.SendAsync(new ServerResultAckMessage(ServerResult.ImpossibleToEnterRoom));
          return;
        }

        if (plr.Channel.RoomManager._rooms.TryGetValue(message.RoomId, out var room))
        {
          if (room.IsChangingRules)
          {
            await session.SendAsync(new ServerResultAckMessage(ServerResult.RoomChangingRules));
            return;
          }

          if (!string.IsNullOrEmpty(room.Options.Password) &&
              !room.Options.Password.Equals(message.Password) &&
              plr.Account.SecurityLevel <= SecurityLevel.Tester)
          {
            await session.SendAsync(new ServerResultAckMessage(ServerResult.PasswordError));
            return;
          }

          room.Join(plr);
        }
        else
        {
          Logger.ForAccount(plr).Error("Room {roomId} in channel {channelId} not found", message.RoomId,
              plr.Channel.Id);
          await session.SendAsync(new ServerResultAckMessage(ServerResult.CannotFindRoom));
        }
      }
      catch (RoomAccessDeniedException)
      {
        await session.SendAsync(new ServerResultAckMessage(ServerResult.CantEnterBecauseKicked));
      }
      catch (RoomLimitReachedException)
      {
        await session.SendAsync(new ServerResultAckMessage(ServerResult.CantEnterRoom));
      }
      catch (RoomLimitIsNoIntrutionException)
      {
        await session.SendAsync(new ServerResultAckMessage(ServerResult.ImpossibleToEnterRoom));
      }
      catch (RoomException)
      {
        await session.SendAsync(new ServerResultAckMessage(ServerResult.ImpossibleToEnterRoom));
      }
      catch (Exception ex)
      {
        Logger.Error(ex.ToString());
        await session.SendAsync(new ServerResultAckMessage(ServerResult.ImpossibleToEnterRoom));
      }
    }

    [MessageHandler(typeof(RoomLeaveReqMessage))]
    public void CJoinTunnelInfoReq(GameSession session)
    {
      var plr = session.Player;
      plr?.Room?.Leave(plr);
    }

    [MessageHandler(typeof(RoomTeamChangeReqMessage))]
    public void CChangeTeamReq(GameSession session, RoomTeamChangeReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null || plr.Room.GameState != GameState.Waiting)
        return;

      try
      {
        plr.Room.TeamManager.ChangeMode(plr, message.Mode);
        plr.Room.TeamManager.ChangeTeam(plr, message.Team);
      }
      catch (RoomException ex)
      {
        Logger.ForAccount(plr).Error(ex, "Failed to change team to {team}", message.Team);
      }
      catch (Exception ex)
      {
        Logger.ForAccount(plr).Error(ex, "Failed to change mode to {mode}", message.Mode);
        plr.SendAsync(new RoomChangeTeamFailAckMessage(ChangeTeamResult.Full));
      }
    }

    [MessageHandler(typeof(RoomPlayModeChangeReqMessage))]
    public void CPlayerGameModeChangeReq(GameSession session, RoomPlayModeChangeReqMessage message)
    {
      var plr = session.Player;

      try
      {
        plr.Room.TeamManager.ChangeMode(plr, message.Mode);
      }
      catch (Exception ex)
      {
        Logger.ForAccount(plr).Error(ex, "Failed to change mode to {mode}", message.Mode);
        plr.SendAsync(new RoomChangeTeamFailAckMessage(ChangeTeamResult.Full));
      }
    }

    [MessageHandler(typeof(GameLoadingSuccessReqMessage))]
    public void CLoadingSucceeded(GameSession session)
    {
      var plr = session.Player;
      if (plr.Room == null)
        return;

      plr.RoomInfo.HasLoaded = true;
      plr.RoomInfo.State = PlayerState.Waiting;
      plr.Room.Broadcast(new RoomGameEndLoadingAckMessage(plr.Account.Id));

      if (!plr.Room.HasStarted)
        return;

      foreach (var member in plr.Room.Players.Where(x => x.Value.RoomInfo.HasLoaded))
        plr.SendAsync(new RoomGameEndLoadingAckMessage(member.Value.Account.Id));

      plr.RoomInfo.State = plr.RoomInfo.Mode == PlayerGameMode.Spectate
          ? PlayerState.Spectating
          : PlayerState.Alive;
      plr.Room.GameRuleManager.GameRule.IntrudeCompleted(plr);
    }

    [MessageHandler(typeof(RoomIntrudeRoundReq2Message))]
    public void CIntrudeRoundReq2(GameSession session)
    {
      var plr = session.Player;
      plr?.Room?.IntrudeRoom(plr);
    }

    [MessageHandler(typeof(RoomIntrudeRoundReqMessage))]
    public void CIntrudeRoundReq(GameSession session)
    {
      var plr = session.Player;
      plr?.Room?.IntrudeRoom(plr);
    }

    [MessageHandler(typeof(RoomBeginRoundReqMessage))]
    public void CBeginRoundReq(GameSession session)
    {
      var plr = session.Player;
      plr?.Room?.BeginRound(plr);
    }

    [MessageHandler(typeof(RoomBeginRoundReq2Message))]
    public void CBeginRoundReq2(GameSession session)
    {
      var plr = session.Player;
      plr?.Room?.BeginRound(plr);
    }

    [MessageHandler(typeof(RoomReadyRoundReqMessage))]
    public void CReadyRoundReq(GameSession session)
    {
      var plr = session.Player;
      plr?.Room?.ChangeReadyStatus(plr);
    }

    [MessageHandler(typeof(RoomReadyRoundReq2Message))]
    public void CReadyRoundReq2(GameSession session)
    {
      var plr = session.Player;
      plr?.Room?.ChangeReadyStatus(plr);
    }

    [MessageHandler(typeof(GameEventMessageReqMessage))]
    public void CEventMessageReq(GameSession session, GameEventMessageReqMessage message)
    {
      var plr = session.Player;

      if (plr.Room == null)
        return;

      //Todo Add checks!
      plr.Room.Broadcast(new GameEventMessageAckMessage(message.Event, message.AccountId, message.Unk1,
          message.Value, ""));

      if (!plr.Room.GameRuleManager.GameRule.StateMachine.IsInState(GameRuleState.Playing))
        return;

      if (plr.RoomInfo.State != PlayerState.Lobby)
        return;

      if (!plr.Room.HasStarted || plr.RoomInfo.HasLoaded)
        return;

      plr.Session?.SendAsync(new RoomGameLoadingAckMessage());
    }


    [MessageHandler(typeof(RoomItemChangeReqMessage))]
    public void CItemsChangeReq(GameSession session, RoomItemChangeReqMessage message)
    {
      var plr = session.Player;
      if (plr?.Room == null)
        return;

      plr.Room?.Broadcast(new RoomChangeItemAckMessage(message.Unk1, message.Unk2));

      // var @char = plr.CharacterManager.CurrentCharacter;
      // var unk1 = new ChangeItemsUnkDto
      // {
      //     AccountId = plr.Account.Id,
      //     Skills = @char.Skills.GetItems().Select(item => item?.ItemNumber ?? 0).ToArray(),
      //     Weapons = @char.Weapons.GetItems().Select(item => item?.ItemNumber ?? 0).ToArray(),
      //     Unk4 = message.Unk1.Unk4,
      //     Unk5 = message.Unk1.Unk5,
      //     Unk6 = message.Unk1.Unk6,
      //     HP = @char.GetHP()
      // };
    }

    [MessageHandler(typeof(GameAvatarChangeReqMessage))]
    public void CAvatarChangeReq(GameSession session, GameAvatarChangeReqMessage message)
    {
      var plr = session.Player;
      if (plr?.Room == null)
        return;

      plr.Room?.Broadcast(new GameAvatarChangeAckMessage(message.Unk1, message.Unk2));
      //var @char = plr.CharacterManager.CurrentCharacter;
      //var unk1 = new ChangeAvatarUnk1Dto
      //{
      //    AccountId = plr.Account.Id,
      //    Skills = @char.Skills.GetItems().Select(item => item?.ItemNumber ?? 0).ToArray(),
      //    Weapons = @char.Weapons.GetItems().Select(item => item?.ItemNumber ?? 0).ToArray(),
      //    Costumes = @char.Costumes.GetItems().Select(item => item?.ItemNumber ?? 0).ToArray(),
      //    Unk5 = message.Unk1.Unk5,
      //    Unk6 = message.Unk1.Unk6,
      //    Unk7 = message.Unk1.Unk7,
      //    Unk8 = message.Unk1.Unk8,
      //    Gender = plr.CharacterManager.CurrentCharacter.Gender,
      //    HP = @char.GetHP()
      //};
    }

    [MessageHandler(typeof(RoomChangeRuleNotifyReqMessage))]
    public void CChangeRuleNotifyReq(GameSession session, RoomChangeRuleNotifyReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null)
        return;

      if (plr != plr.Room.Master)
      {
        session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
        return;
      }

      if (plr.Room.GameState != GameState.Waiting)
      {
        session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
        return;
      }

      try
      {
        session.Player.Room.ChangeRules(message.Settings);
      }
      catch (Exception)
      {
        session.SendAsync(new RoomChangeRuleFailAckMessage { Result = 1 });
      }
    }

    [MessageHandler(typeof(RoomChangeRuleNotifyReq2Message))]
    public void CChangeRuleNotifyReq2(GameSession session, RoomChangeRuleNotifyReq2Message message)
    {
      var plr = session.Player;
      if (plr.Room == null)
        return;

      if (plr != plr.Room.Master)
      {
        session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
        return;
      }

      if (plr.Room.GameState != GameState.Waiting)
      {
        session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
        return;
      }

      try
      {
        session.Player.Room.ChangeRules2(message.Settings);
      }
      catch (Exception)
      {
        session.SendAsync(new RoomChangeRuleFailAckMessage { Result = 1 });
      }
    }

    [MessageHandler(typeof(RoomLeaveReguestReqMessage))]
    public void CLeavePlayerRequestReq(GameSession session, RoomLeaveReguestReqMessage message)
    {
      var plr = session.Player;
      var room = plr.Room;

      if (room == null)
        return;

      var targetPlr = room.Players.GetValueOrDefault(message.AccountId);
      if (targetPlr == null)
        return;

      switch (message.Reason)
      {
        case RoomLeaveReason.Kicked:
        case RoomLeaveReason.ModeratorKick:
          // Only the master can kick people and kick is only allowed in the lobby
          if ((room.Master != plr || plr.Account.SecurityLevel < SecurityLevel.Tester) &&
              !room.GameRuleManager.GameRule.StateMachine.IsInState(GameRuleState.Waiting))
            return;
          break;
        default:
          // Only allow client to kick itself
          if (plr != targetPlr)
            return;
          break;
      }

      room.Leave(targetPlr, message.Reason);
    }

    [MessageHandler(typeof(RoomQuickJoinReqMessage))]
    public void QuickJoinReq(GameSession session, RoomQuickJoinReqMessage message)
    {
      var plr = session.Player;
      if (plr == null)
        return;

      if (plr.Room != null)
        return;

      try
      {
        var rooms = new Dictionary<Room, int>();

        foreach (var room in plr.Channel.RoomManager)
        {
          if (room.Options.Password == string.Empty)
          {
            if (!room.Options.GameRule.Equals((GameRule)message.GameRule))
              continue;

            if (!room.GameRuleManager.GameRule.StateMachine.IsInState(GameRuleState.Waiting) &&
                (!room.GameRuleManager.GameRule.StateMachine.IsInState(GameRuleState.Playing) ||
                 room.Options.IsNoIntrusion))
              continue;

            var priority = 0;
            priority += Math.Abs(room.TeamManager[Team.Alpha].Players.Count() -
                                 room.TeamManager[Team.Beta].Players
                                     .Count()); // Calculating team balance

            if (room.GameRuleManager.GameRule.StateMachine.IsInState(GameRuleState.SecondHalf))
            {
              // If only 15 seconds are left...
              if (room.Options.TimeLimit.TotalSeconds / 2 -
                  room.GameRuleManager.GameRule.RoundTime.TotalSeconds <= 15)
              {
                priority -= 3;

                // ...lower the room priority
              }
            }

            rooms.Add(room, priority);
          }
        }

        var roomList = rooms.ToList();
        if (roomList.Any())
        {
          roomList.Sort((room1, room2) => room2.Value.CompareTo(room1.Value));
          session.SendAsync(new RoomQuickJoinAckMessage(1, (byte)roomList.First().Key.Id));
          return;
        }

        session.SendAsync(new RoomQuickJoinAckMessage(0, 0));
        //session.SendAsync(new ServerResultAckMessage(ServerResult.CannotFindRoom));
      }
      catch (Exception)
      {
        session.SendAsync(new ServerResultAckMessage(ServerResult.ServerError));
      }
    }

    [MessageHandler(typeof(TutorialCompletedReqMessage))]
    public void TutorialCompletedReq(GameSession session, TutorialCompletedReqMessage message)
    {
      session.Player.TutorialState = 1;
      session.SendAsync(new TutorialCompletedAckMessage { Unk = 0 });
    }

    [MessageHandler(typeof(Btc_Clear_ReqMessage))]
    public void BtcClearReqMessage(GameSession session, Btc_Clear_ReqMessage message)
    {
      switch (message.Mode)
      {
        case 1: // Tutorial
          session.Player.TutorialState = 1;
          session.SendAsync(new TutorialCompletedAckMessage { Unk = 0 });
          break;
      }
    }

    [MessageHandler(typeof(ArcadeStageFailedReqMessage))]
    public void ArcadeStageFailedReq(GameSession session, ArcadeStageFailedReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null || plr.Room.GameRuleManager.GameRule.GameRule != GameRule.Horde)
        return;

      plr.Room.GameRuleManager.GameRule.StateMachine.Fire(GameRuleStateTrigger.StartResult);
    }

    [MessageHandler(typeof(ArcadeStageClearReqMessage))]
    public void ArcadeStageClearReq(GameSession session, ArcadeStageClearReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null || plr.Room.GameRuleManager.GameRule.GameRule != GameRule.Horde)
        return;

      plr.Room.GameRuleManager.GameRule.StateMachine.Fire(GameRuleStateTrigger.StartResult);
    }

    [MessageHandler(typeof(GameKickOutRequestReqMessage))]
    public void GameKickOutRequest(GameSession session, GameKickOutRequestReqMessage message)
    {
      const uint voteKickPrice = 100u;
      const int minPlayerRequiredForKick = 4;

      var sender = session.Player;
      var room = sender.Room;

      if (room == null)
      {
        sender.SendAsync(new ServerResultAckMessage(ServerResult.CannotFindRoom));
        return;
      }

      if (sender.Room.Players.Count() < minPlayerRequiredForKick)
      {
        sender.SendAsync(new GameKickOutRequestAckMessage { Message = VoteKickMessage.NotEnoughtPlayerToVote });
        return;
      }

      if (room.VoteKickMgr.State == VoteKickManager.KickState.Execution || room.VoteKickMgr.State == VoteKickManager.KickState.End)
      {
        sender.SendAsync(new GameKickOutRequestAckMessage { Message = VoteKickMessage.CurrentlyRunning });
        return;
      }

      if (sender.PEN < voteKickPrice)
      {
        sender.SendAsync(new GameKickOutRequestAckMessage { Message = VoteKickMessage.InsufficientMoney });
        return;
      }

      var target = room.Players.FirstOrDefault(x => x.Value.Account.Id == message.Target).Value;
      if (target == null)
      {
        sender.SendAsync(new GameKickOutRequestAckMessage { Message = VoteKickMessage.PlayerNotInRoom });
        return;
      }

      if (target.Account.SecurityLevel > SecurityLevel.Tester)
      {
        sender.SendAsync(new GameKickOutRequestAckMessage { Message = VoteKickMessage.CantKickGM });
        return;
      }

      sender.PEN -= voteKickPrice;
      sender.SendAsync(new MoneyRefreshPenInfoAckMessage { Unk = sender.PEN });

      room.VoteKickMgr.Start(sender, target, message.Reason);
    }

    [MessageHandler(typeof(GameKickOutVoteResultReqMessage))]
    public void GameKickOutVoteResultRequest(GameSession session, GameKickOutVoteResultReqMessage message)
    {
      var player = session.Player;
      var room = player.Room;

      if (room == null)
      {
        player.SendAsync(new ServerResultAckMessage(ServerResult.CannotFindRoom));
        return;
      }

      if (room.VoteKickMgr.State == VoteKickManager.KickState.Execution)
      {
        room.VoteKickMgr.UpdateResult(message.IsYes);
        player.SendAsync(new GameKickOutVoteResultAckMessage { Result = VoteKickResult.Ok });
      }
    }

    [MessageHandler(typeof(InGameItemGetReqMessage))]
    public void InGameItemGetReq(GameSession session, InGameItemGetReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null || plr.Room.GameRuleManager.GameRule.GameRule != GameRule.Horde)
        return;

      plr.Room.Broadcast(new InGameItemGetAckMessage
      {
        Unk1 = (long)plr.Account.Id,
        Unk2 = message.Unk1,
        Unk3 = message.Unk2
      });
    }

    [MessageHandler(typeof(InGameItemDropReqMessage))]
    public void InGameItemDropReq(GameSession session, InGameItemDropReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null || plr.Room.GameRuleManager.GameRule.GameRule != GameRule.Horde)
        return;

      var gamerule = (ConquestGameRule)plr.Room.GameRuleManager.GameRule;

      var num = new SecureRandom().Next(0, 10);

      var unk4 = 0;
      var unk6 = 0L;

      if (num < 8) //static ammo
      {
        unk4 = 319717609;
        unk6 = 28154369870397440;
      }
      else //static hp
      {
        unk4 = 319786968;
        unk6 = 28154369635516416;
      }

      var x = new InGameItemDropAckMessage
      {
        Item = new ItemDropAckDto //static ammo drop
        {
          Counter = gamerule.DropCount++,
          Unk2 = 3,
          Unk3 = 2,
          Unk4 = unk4,
          Position = message.Item.Position,
          Unk6 = unk6
        }
      };

      plr.Room.Broadcast(x);
    }

    [MessageHandler(typeof(MoneyUseCoinReqMessage))]
    public void MoneyUseCoinRequest(GameSession session, MoneyUseCoinReqMessage message)
    {
      var plr = session.Player;

      if (plr.PlayerCoinBuff != null)
      { plr.PlayerCoinBuff.StartBuffSystem(message.BuffType); }
    }

    #region Scores

    [MessageHandler(typeof(ScoreKillReqMessage))]
    public void CScoreKillReq(GameSession session, ScoreKillReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null || !plr.Room.HasStarted)
        return;

      var room = plr.Room;

      var target = room.Players.GetValueOrDefault(message.Score.Target.AccountId);
      if (target != null && target.RoomInfo.PeerId.EqualSlot(message.Score.Target))
        target.RoomInfo.PeerId = message.Score.Target;

      var killer = room.Players.GetValueOrDefault(message.Score.Killer.AccountId);
      if (killer != null && killer.RoomInfo.PeerId.EqualSlot(message.Score.Killer))
        killer.RoomInfo.PeerId = message.Score.Killer;

      room.GameRuleManager.GameRule.OnScoreKill(killer, null, target, message.Score.Weapon, message.Score.Target,
          message.Score.Killer, null);
    }

    [MessageHandler(typeof(ScoreKillAssistReqMessage))]
    public void CScoreKillAssistReq(GameSession session, ScoreKillAssistReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null || !plr.Room.HasStarted)
        return;

      var room = plr.Room;

      var target = room.Players.GetValueOrDefault(message.Score.Target.AccountId);
      if (target != null && target.RoomInfo.PeerId.EqualSlot(message.Score.Target))
        target.RoomInfo.PeerId = message.Score.Target;

      var killer = room.Players.GetValueOrDefault(message.Score.Killer.AccountId);
      if (killer != null && killer.RoomInfo.PeerId.EqualSlot(message.Score.Killer))
        killer.RoomInfo.PeerId = message.Score.Killer;

      var assist = room.Players.GetValueOrDefault(message.Score.Assist.AccountId);
      if (assist != null && assist.RoomInfo.PeerId.EqualSlot(message.Score.Assist))
        assist.RoomInfo.PeerId = message.Score.Assist;

      room.GameRuleManager.GameRule.OnScoreKill(killer, assist, target, message.Score.Weapon,
          message.Score.Target,
          message.Score.Killer, message.Score.Assist);
    }

    [MessageHandler(typeof(ScoreOffenseReqMessage))]
    public void CScoreOffenseReq(GameSession session, ScoreOffenseReqMessage message)
    {
      var plr = session.Player;
      var room = plr.Room;

      if (plr.Room == null || !plr.Room.HasStarted)
        return;

      var target = room.Players.GetValueOrDefault(message.Score.Target.AccountId);
      if (target != null && target.RoomInfo.PeerId.EqualSlot(message.Score.Target))
        target.RoomInfo.PeerId = message.Score.Target;

      var killer = room.Players.GetValueOrDefault(message.Score.Killer.AccountId);
      if (killer != null && killer.RoomInfo.PeerId.EqualSlot(message.Score.Killer))
        killer.RoomInfo.PeerId = message.Score.Killer;

      switch (room.Options.GameRule)
      {
        case GameRule.Touchdown:
          ((TouchdownGameRule)room.GameRuleManager.GameRule).OnScoreOffense(killer, null, target,
              message.Score.Weapon, message.Score.Target, message.Score.Killer, null);
          break;
        case GameRule.PassTouchdown:
          ((PassTouchdownGameRule)room.GameRuleManager.GameRule).OnScoreOffense(killer, null, target,
              message.Score.Weapon, message.Score.Target, message.Score.Killer, null);
          break;
        case GameRule.CombatTrainingTD:
          ((TouchdownTrainingGameRule)room.GameRuleManager.GameRule).OnScoreOffense(killer, null, target,
              message.Score.Weapon, message.Score.Target, message.Score.Killer, null);
          break;
      }
    }

    [MessageHandler(typeof(ScoreOffenseAssistReqMessage))]
    public void CScoreOffenseAssistReq(GameSession session, ScoreOffenseAssistReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null || !plr.Room.HasStarted)
        return;

      var room = plr.Room;

      var target = room.Players.GetValueOrDefault(message.Score.Target.AccountId);
      if (target != null && target.RoomInfo.PeerId.EqualSlot(message.Score.Target))
        target.RoomInfo.PeerId = message.Score.Target;

      var killer = room.Players.GetValueOrDefault(message.Score.Killer.AccountId);
      if (killer != null && killer.RoomInfo.PeerId.EqualSlot(message.Score.Killer))
        killer.RoomInfo.PeerId = message.Score.Killer;

      var assist = room.Players.GetValueOrDefault(message.Score.Assist.AccountId);
      if (assist != null && assist.RoomInfo.PeerId.EqualSlot(message.Score.Assist))
        assist.RoomInfo.PeerId = message.Score.Assist;

      switch (room.Options.GameRule)
      {
        case GameRule.Touchdown:
          ((TouchdownGameRule)room.GameRuleManager.GameRule).OnScoreOffense(killer, assist, target,
              message.Score.Weapon, message.Score.Target, message.Score.Killer, message.Score.Assist);
          break;
        case GameRule.PassTouchdown:
          ((PassTouchdownGameRule)room.GameRuleManager.GameRule).OnScoreOffense(killer, assist, target,
              message.Score.Weapon, message.Score.Target, message.Score.Killer, message.Score.Assist);
          break;
        case GameRule.CombatTrainingTD:
          ((TouchdownTrainingGameRule)room.GameRuleManager.GameRule).OnScoreOffense(killer, assist, target,
              message.Score.Weapon, message.Score.Target, message.Score.Killer, message.Score.Assist);
          break;
      }
    }

    [MessageHandler(typeof(ScoreDefenseReqMessage))]
    public void CScoreDefenseReq(GameSession session, ScoreDefenseReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null || !plr.Room.HasStarted)
        return;

      var room = plr.Room;

      var target = room.Players.GetValueOrDefault(message.Score.Target.AccountId);
      if (target != null && target.RoomInfo.PeerId.EqualSlot(message.Score.Target))
        target.RoomInfo.PeerId = message.Score.Target;

      var killer = room.Players.GetValueOrDefault(message.Score.Killer.AccountId);
      if (killer != null && killer.RoomInfo.PeerId.EqualSlot(message.Score.Killer))
        killer.RoomInfo.PeerId = message.Score.Killer;

      switch (room.Options.GameRule)
      {
        case GameRule.Touchdown:
          ((TouchdownGameRule)room.GameRuleManager.GameRule).OnScoreDefense(killer, null, target,
              message.Score.Weapon, message.Score.Target, message.Score.Killer, null);
          break;
        case GameRule.PassTouchdown:
          ((PassTouchdownGameRule)room.GameRuleManager.GameRule).OnScoreDefense(killer, null, target,
              message.Score.Weapon, message.Score.Target, message.Score.Killer, null);
          break;
        case GameRule.CombatTrainingTD:
          ((TouchdownTrainingGameRule)room.GameRuleManager.GameRule).OnScoreDefense(killer, null, target,
              message.Score.Weapon, message.Score.Target, message.Score.Killer, null);
          break;
      }
    }

    [MessageHandler(typeof(ScoreDefenseAssistReqMessage))]
    public void CScoreDefenseAssistReq(GameSession session, ScoreDefenseAssistReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null || !plr.Room.HasStarted)
        return;

      var room = plr.Room;

      var target = room.Players.GetValueOrDefault(message.Score.Target.AccountId);
      if (target != null && target.RoomInfo.PeerId.EqualSlot(message.Score.Target))
        target.RoomInfo.PeerId = message.Score.Target;

      var killer = room.Players.GetValueOrDefault(message.Score.Killer.AccountId);
      if (killer != null && killer.RoomInfo.PeerId.EqualSlot(message.Score.Killer))
        killer.RoomInfo.PeerId = message.Score.Killer;

      var assist = room.Players.GetValueOrDefault(message.Score.Assist.AccountId);
      if (assist != null && assist.RoomInfo.PeerId.EqualSlot(message.Score.Assist))
        assist.RoomInfo.PeerId = message.Score.Assist;

      switch (room.Options.GameRule)
      {
        case GameRule.Touchdown:
          ((TouchdownGameRule)room.GameRuleManager.GameRule).OnScoreDefense(killer, assist, target,
              message.Score.Weapon, message.Score.Target, message.Score.Killer, message.Score.Assist);
          break;
        case GameRule.PassTouchdown:
          ((PassTouchdownGameRule)room.GameRuleManager.GameRule).OnScoreDefense(killer, assist, target,
              message.Score.Weapon, message.Score.Target, message.Score.Killer, message.Score.Assist);
          break;
        case GameRule.CombatTrainingTD:
          ((TouchdownTrainingGameRule)room.GameRuleManager.GameRule).OnScoreDefense(killer, assist, target,
              message.Score.Weapon, message.Score.Target, message.Score.Killer, message.Score.Assist);
          break;
      }
    }

    [MessageHandler(typeof(ScoreTeamKillReqMessage))]
    public void CScoreTeamKillReq(GameSession session, ScoreTeamKillReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null || !plr.Room.HasStarted)
        return;

      var room = plr.Room;

      var target = room.Players.GetValueOrDefault(message.Score.Target.AccountId);
      if (target != null && target.RoomInfo.PeerId.EqualSlot(message.Score.Target))
        target.RoomInfo.PeerId = message.Score.Target;

      var killer = room.Players.GetValueOrDefault(message.Score.Killer.AccountId);
      if (killer != null && killer.RoomInfo.PeerId.EqualSlot(message.Score.Killer))
        killer.RoomInfo.PeerId = message.Score.Killer;

      room.GameRuleManager.GameRule.OnScoreKill(killer, null, target, message.Score.Weapon, message.Score.Target,
          message.Score.Killer, null);
    }

    [MessageHandler(typeof(ScoreHealAssistReqMessage))]
    public void CScoreHealAssistReq(GameSession session, ScoreHealAssistReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null || !plr.Room.HasStarted)
        return;
      var room = plr.Room;

      var target = room.Players.GetValueOrDefault(message.Id.AccountId);
      if (target != null && target.RoomInfo.PeerId.EqualSlot(message.Id))
        target.RoomInfo.PeerId = message.Id;

      room.GameRuleManager.GameRule.OnScoreHeal(target, message.Id);
    }

    [MessageHandler(typeof(ScoreSuicideReqMessage))]
    public void CScoreSuicideReq(GameSession session, ScoreSuicideReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null)
      {
        session.SendAsync(new InGamePlayerResponseOfDeathAckMessage());
        return;
      }

      if (!plr.Room.HasStarted)
        return;

      var room = plr.Room;

      var target = room.Players.GetValueOrDefault(message.Id.AccountId);
      if (target != null && target.RoomInfo.PeerId.EqualSlot(message.Id))
        target.RoomInfo.PeerId = message.Id;

      room.GameRuleManager.GameRule.OnScoreSuicide(target, message.Id, (AttackAttribute)message.Icon);
    }

    [MessageHandler(typeof(ScoreReboundReqMessage))]
    public void CScoreReboundReq(GameSession session, ScoreReboundReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null)
      {
        session.SendAsync(new ScoreReboundAckMessage(message.NewId, message.OldId));
        return;
      }

      if (!plr.Room.HasStarted)
        return;

      var room = plr.Room;

      var oldPlr = room.Players.GetValueOrDefault(message.OldId.AccountId);
      if (oldPlr != null && oldPlr.RoomInfo.PeerId.EqualSlot(message.OldId))
        oldPlr.RoomInfo.PeerId = message.OldId;

      var newPlr = room.Players.GetValueOrDefault(message.NewId.AccountId);
      if (newPlr != null && newPlr.RoomInfo.PeerId.EqualSlot(message.NewId))
        newPlr.RoomInfo.PeerId = message.NewId;


      switch (room.Options.GameRule)
      {
        case GameRule.Touchdown:
          ((TouchdownGameRule)room.GameRuleManager.GameRule).OnScoreRebound(newPlr, oldPlr, message.NewId,
              message.OldId);
          break;
        case GameRule.PassTouchdown:
          ((PassTouchdownGameRule)room.GameRuleManager.GameRule).OnScoreRebound(newPlr, oldPlr,
              message.NewId, message.OldId);
          break;
        case GameRule.CombatTrainingTD:
          ((TouchdownTrainingGameRule)room.GameRuleManager.GameRule).OnScoreRebound(newPlr, oldPlr,
              message.NewId, message.OldId);
          break;
      }
    }

    [MessageHandler(typeof(ScoreGoalReqMessage))]
    public void CScoreGoalReq(GameSession session, ScoreGoalReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null)
      {
        session.SendAsync(new ScoreGoalAckMessage(message.PeerId));
        return;
      }

      if (!plr.Room.HasStarted)
        return;

      var room = plr.Room;

      var target = room.Players.GetValueOrDefault(message.PeerId.AccountId);
      if (target != null && target.RoomInfo.PeerId.EqualSlot(message.PeerId))
        target.RoomInfo.PeerId = message.PeerId;

      switch (room.Options.GameRule)
      {
        case GameRule.Touchdown:
          ((TouchdownGameRule)room.GameRuleManager.GameRule).OnScoreGoal(target, message.PeerId);
          break;
        case GameRule.PassTouchdown:
          ((PassTouchdownGameRule)room.GameRuleManager.GameRule).OnScoreGoal(target, message.PeerId);
          break;
        case GameRule.CombatTrainingTD:
          ((TouchdownTrainingGameRule)room.GameRuleManager.GameRule).OnScoreGoal(target, message.PeerId);
          break;
      }
    }

    [MessageHandler(typeof(SlaughterAttackPointReqMessage))]
    public void SlaughterAttackPointReq(GameSession session, SlaughterAttackPointReqMessage message)
    {
      var room = session.Player?.Room;

      if (room?.GameRuleManager.GameRule.GameRule != GameRule.Chaser)
        return;

      ((ChaserGameRule)room.GameRuleManager.GameRule).OnScoreAttack(session.Player, message.Unk2, message.Unk2);
      session.SendAsync(new SlaughterAttackPointAckMessage
      {
        Unk1 = message.Unk1,
        Unk2 = message.Unk2,
        AccountId = message.AccountId
      });
    }

    [MessageHandler(typeof(SlaughterHealPointReqMessage))]
    public void SlaughterHealPointReq(GameSession session, SlaughterHealPointReqMessage message)
    {
      //session.SendAsync(new SlaughterHealPointReqMessage()
      //{
      //    Unk = message.Unk,
      //});
    }

    [MessageHandler(typeof(ScoreMissionScoreReqMessage))]
    public void ScoreMissionScoreReq(GameSession session, ScoreMissionScoreReqMessage message)
    {
      var plr = session.Player;
      if (plr.Room == null || plr.Room.GameRuleManager.GameRule.GameRule != GameRule.Practice)
        return;

      session.SendAsync(
          new ScoreMissionScoreAckMessage { AccountId = session.Player.Account.Id, Score = message.Score });
    }

    #endregion
  }
}