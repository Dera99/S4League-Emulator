using System.Diagnostics;
using System.Threading.Tasks;
using BlubLib;
using NeoNetsphere.Game;
using NeoNetsphere.Network.Message.Relay;
using Stateless;

namespace NeoNetsphere
{
  using System;
  using System.Collections.Concurrent;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using BlubLib.Collections.Concurrent;
  using BlubLib.Threading.Tasks;
  using ExpressMapper.Extensions;
  using NeoNetsphere.Network;
  using NeoNetsphere.Network.Data.Chat;
  using NeoNetsphere.Network.Data.Club;
  using NeoNetsphere.Network.Data.Game;
  using NeoNetsphere.Network.Data.GameRule;
  using NeoNetsphere.Network.Message.Chat;
  using NeoNetsphere.Network.Message.Club;
  using NeoNetsphere.Network.Message.Game;
  using NeoNetsphere.Network.Message.GameRule;
  using ProudNetSrc;
  using Serilog;
  using Serilog.Core;

  internal class Room : IDisposable
  {
    private const uint PingDifferenceForChange = 50;

    // ReSharper disable once InconsistentNaming
    public static readonly ILogger Logger = Log.ForContext(Constants.SourceContextPropertyName, "GameRoomMgr");

    private TimeSpan _changingRulesTime = TimeSpan.FromSeconds(2);

    private TimeSpan _hostUpdateTime = TimeSpan.FromSeconds(30);

    private TimeSpan _voteKickTime = TimeSpan.FromSeconds(10);

    private ConcurrentDictionary<ulong, object> _kickedPlayers = new ConcurrentDictionary<ulong, object>();

    private ConcurrentDictionary<ulong, Player> _players = new ConcurrentDictionary<ulong, Player>();

    private Dictionary<Player, PlayerGameMode> _roomChangePlayers = new Dictionary<Player, PlayerGameMode>();

    private Dictionary<Player, PlayerGameMode> _roomChangeAlphaPlayers = new Dictionary<Player, PlayerGameMode>();

    private Dictionary<Player, PlayerGameMode> _roomChangeBetaPlayers = new Dictionary<Player, PlayerGameMode>();

    public AsyncLock _playerSync = new AsyncLock();

    private TimeSpan _changingRulesTimer;

    private TimeSpan _hostUpdateTimer;

    private TimeSpan _voteKicktimer;

    public bool Disposed { get; private set; }

    public Room(RoomManager roomManager, uint id, RoomCreationOptions options, P2PGroup group, Player creator)
    {
      RoomManager = roomManager;
      Id = id;
      Options = options;
      TimeCreated = DateTime.Now;
      TeamManager = new TeamManager(this);
      GameRuleManager = new GameRuleManager(this);
      VoteKickMgr = new VoteKickManager(this);
      Group = group;
      Creator = creator;
      Master = creator;
      TeamManager.TeamChanged += TeamManager_TeamChanged;

      GameRuleManager.GameRuleChanged += GameRuleManager_OnGameRuleChanged;
      GameRuleManager.MapInfo = GameServer.Instance.ResourceCache.GetMaps()[options.MapId];
      GameRuleManager.GameRule = RoomManager.GameRuleFactory.Get(Options.GameRule, this);
    }

    public void Dispose()
    {
      if (Disposed || _playerSync == null)
        return;

      Disposed = true;
      Id = 0;

      foreach (var plr in Players.Values)
      {
        if (plr == null)
          continue;

        try
        {
          Leave(plr);
        }
        catch { }
      }

      _playerSync.Lock().Dispose();
      _playerSync = null;
      _roomChangePlayers.Clear();
      _roomChangeAlphaPlayers.Clear();
      _roomChangeBetaPlayers.Clear();
      _kickedPlayers.Clear();
      _players.Clear();
      TeamManager.TeamChanged -= TeamManager_TeamChanged;
      GameRuleManager.GameRuleChanged -= GameRuleManager_OnGameRuleChanged;
      GameRuleManager.MapInfo = null;
      GameRuleManager.GameRule = null;
      RoomManager = null;
      Options = null;
      TimeCreated = DateTime.Now;
      TeamManager = null;
      GameRuleManager = null;
      VoteKickMgr = null;
      Group = null;
      Creator = null;
      Master = null;
      Host = null;
      HasStarted = false;
      IsPreparing = false;
    }

    public RoomManager RoomManager { get; private set; }

    public uint Id { get; private set; }

    public RoomCreationOptions Options { get; private set; }

    public DateTime TimeCreated { get; private set; }

    public TeamManager TeamManager { get; private set; }

    public GameRuleManager GameRuleManager { get; private set; }

    public bool HasStarted { get; set; }

    public bool IsPreparing { get; set; }

    public GameState GameState { get; set; } = GameState.Waiting;

    public GameTimeState SubGameState { get; set; }

    public GameRuleState GameRuleState => GameRuleManager.GameRule.StateMachine.State;

    public TimeSpan RoundTime { get; set; } = TimeSpan.Zero;

    public IReadOnlyDictionary<ulong, Player> Players => _players;

    public Player Master { get; private set; }

    public Player Host { get; private set; }

    public Player Creator { get; private set; }

    public P2PGroup Group { get; private set; }

    public VoteKickManager VoteKickMgr { get; private set; }

    public bool IsChangingRules { get; private set; }

    private bool IsChangingRulesCooldown { get; set; }

    public void Update(AccurateDelta delta)
    {
      if (Disposed)
        return;

      try
      {
        if (!Players.Any())
        {
          RoomManager.Remove(this);
          return;
        }

        if (!(Master?.IsLoggedIn() ?? true) || Master?.Room != this)
        {
          ChangeMasterIfNeeded(GetPlayerWithLowestPing(), true);
          ChangeHostIfNeeded(GetPlayerWithLowestPing(), true);
        }

        if (!TeamManager.NoSpectatorPlayers.Any() && TeamManager.Players.Any())
        {
          foreach (var spectator in TeamManager.Spectators)
          {
            TeamManager.ChangeMode(spectator, PlayerGameMode.Normal);
          }
        }

        if (IsChangingRules)
        {
          _changingRulesTimer += delta.delta;
          if (_changingRulesTimer >= _changingRulesTime && !IsChangingRulesCooldown)
          {
            RoomManager.Channel.BroadcastCencored(new RoomChangeRoomInfoAck2Message(GetRoomInfo()));
            Broadcast(new RoomChangeRuleAckMessage(Options.Map<RoomCreationOptions, ChangeRuleDto2>()));
            Broadcast(new GameChangeStateAckMessage(GameState));
            IsChangingRulesCooldown = true;
          }

          foreach (var player in _players.Values)
            player.RoomInfo.LastMapID = (byte)Options.MapId;

          if (_changingRulesTimer >= _changingRulesTime.Add(TimeSpan.FromSeconds(3)))
          {
            IsChangingRules = false;
            IsChangingRulesCooldown = false;
          }
        }
        else
        {
          foreach (var player in Players.Values.Where(x => !TeamManager.Players.Contains(x)))
          {
            TeamManager.Join(player);
          }
        }

        if (VoteKickMgr.State == VoteKickManager.KickState.Execution)
        {
          _voteKicktimer += delta.delta;
          if (_voteKicktimer < _voteKickTime)
          {
            VoteKickMgr.Update();
          }
          else
          {
            _voteKicktimer = TimeSpan.Zero;
            VoteKickMgr.Evaluate();
          }
        }
      }
      catch (Exception e)
      {
        Logger.Error(e.ToString());
      }

      GameRuleManager?.Update(delta);
    }

    public void Join(Player plr)
    {
      if (Disposed)
        throw new RoomLimitIsNoIntrutionException();

      if (plr.Room != null)
        throw new RoomException("Player is already inside a room");

      if (Options.IsNoIntrusion && GameState != GameState.Waiting)
        throw new RoomLimitIsNoIntrutionException();

      var joinAsSpectator = false;
      if (TeamManager.NoSpectatorPlayers.Count() >= Options.PlayerLimit)
      {
        if (TeamManager.Spectators.Count() >= Options.SpectatorLimit)
          throw new RoomLimitReachedException();

        joinAsSpectator = true;
      }

      if (_kickedPlayers.ContainsKey(plr.Account.Id) && plr.Account.SecurityLevel <= SecurityLevel.Tester)
        throw new RoomAccessDeniedException();

      if (!_players.ContainsKey(plr.Account.Id))
      {
        plr.Channel?.Broadcast(new ChannelLeavePlayerAckMessage(plr.Account.Id));

        if (Config.Instance.ResCheck)
          plr.SendAsync(new LoginReguestAckMessage(GameLoginResult.OK, plr.Account.Id));

        if (Config.Instance.ACMode == 2)
        {
          plr.BE.HB_Last = TimeSpan.FromMinutes(1);
          plr.BE.HB_Last_Issued = true;
        }

     //   using (_playerSync.Lock())
        {
          byte id = 3;
          while (Players.Values.Any(p => p.RoomInfo.Slot == id))
            id++;

          plr.RoomInfo.PeerId = new LongPeerId(plr.Account.Id, new PeerId(0, id, PlayerCategory.Player));
          plr.RoomInfo.Slot = id;
        }

        plr.RoomInfo.Reset();
        plr.RoomInfo.State = PlayerState.Lobby;
        plr.RoomInfo.Mode = joinAsSpectator ? PlayerGameMode.Spectate : PlayerGameMode.Normal;
        plr.RoomInfo.Stats = GameRuleManager.GameRule.GetPlayerRecord(plr);

        plr.Room = this;
        plr.RoomInfo.IsConnecting = true;
        plr.RoomInfo.LastMapID = (byte)Options.MapId;

        _players.TryAdd(plr.Account.Id, plr);
        TeamManager.Join(plr);

        OnPlayerJoining(new RoomPlayerEventArgs(plr));
        plr.stats.OnJoin(GameRuleManager.GameRule);

        var enterinfo = new RoomEnterRoomInfoAck2Message
        {
          RoomId = Id,
          GameRule = Options.GameRule,
          MapId = (byte)Options.MapId,
          PlayerLimit = Options.PlayerLimit,
          GameState = GameState,
          GameTimeState = SubGameState,
          TimeLimit = (uint)Options.TimeLimit.TotalMilliseconds,
          TimeSync = (uint)GameRuleManager.GameRule.RoundTime.TotalMilliseconds,
          ScoreLimit = Options.ScoreLimit,
          RelayEndPoint =
                new IPEndPoint(IPAddress.Parse(Config.Instance.IP), Config.Instance.RelayListener.Port),
          LastMapId = plr.RoomInfo.LastMapID,
        };

        if (Options.GameRule != GameRule.Horde)
        {
          plr.SendAsync(enterinfo);
        }
        else
        {
          plr.SendAsync(enterinfo.Map<RoomEnterRoomInfoAck2Message, RoomEnterRoomInfoAckMessage>());
        }

        plr.SendAsync(new RoomCurrentCharacterSlotAckMessage(1, plr.RoomInfo.Slot));
        BroadcastExcept(plr, new RoomEnterPlayerInfoAckMessage(GetRoomPlrDto(plr, true)));

        plr.SendAsync(new RoomPlayerInfoListForEnterPlayerAckMessage(Players.Values.Select(r => GetRoomPlrDto(r)).ToArray()));
        plr.SendAsync(new RoomPlayerInfoListForEnterPlayerForCollectBookAckMessage());

        if (plr.Club?.Id > 0)
          BroadcastExcept(plr, new RoomEnterClubInfoAckMessage(plr.Map<Player, PlayerClubInfoDto>()));

        plr.SendAsync(new RoomClubInfoListForEnterPlayerAckMessage(Players.Values.Where(p => p.Club?.Id > 0)
            .Select(x => x.Map<Player, PlayerClubInfoDto>()).ToArray()));

        plr.SendAsync(new ItemClearInvalidEquipItemAckMessage());
        plr.SendAsync(new ItemClearEsperChipAckMessage());
        plr.SendAsync(new ClubClubInfoAckMessage(plr.Map<Player, ClubInfoDto>()));
        plr.SendAsync(new ClubClubInfoAck2Message(plr.Map<Player, ClubInfoDto2>()));
        Broadcast(new ChatPlayerInfoAckMessage(plr.Map<Player, PlayerInfoDto>()));
        plr.FriendManager.Broadcast(new ChatPlayerInfoAckMessage(plr.Map<Player, PlayerInfoDto>()));

        Task.Run(async () =>
        {
                  // Anti join bug (stuck in roomlist)
                  await Task.Delay(10000);
          if (plr.RoomInfo.IsConnecting)
          {
            plr.Room.Leave(plr, RoomLeaveReason.AFK);
            await plr.SendAsync(new ServerResultAckMessage(ServerResult.CannotFindRoom));
          }
        });
      }
      else
      {
        plr.Session.SendAsync(new ServerResultAckMessage(ServerResult.DBError));
      }
    }

    public RoomPlayerDto GetRoomPlrDto(Player plr, bool newPlr = false)
    {
      var dto = new RoomPlayerDto
      {
        ClanId = plr.Club?.Id ?? 0,
        AccountId = plr.Account?.Id ?? 0,
        Nickname = plr.Account?.Nickname ?? "n/A",
        IsGM = plr.Account?.SecurityLevel > SecurityLevel.Tester
      };

      if (newPlr)
      {
        dto.Unk1 = 1;
        dto.Pos = (byte)(plr.Room?.Players.Values.ToList().IndexOf(plr) ?? 0);
#if LATESTS4
                dto.Unk3 = 93;
#endif
      }
      else
      {
#if LATESTS4
                dto.Unk1 = 154;
                dto.Pos = (byte) (plr.Room?.Players.Values.ToList().IndexOf(plr) ?? 0);
                dto.Unk3 = 39;
#endif
      }

      return dto;
    }

    public void Leave(Player plr, RoomLeaveReason roomLeaveReason = RoomLeaveReason.Left)
    {
      if (plr == null)
        return;

      if (plr.Room != this)
        return;

      if (_players.ContainsKey(plr.Account?.Id ?? 0))
      {
        if (roomLeaveReason == RoomLeaveReason.Kicked ||
            roomLeaveReason == RoomLeaveReason.ModeratorKick ||
            roomLeaveReason == RoomLeaveReason.VoteKick)
        {
          _kickedPlayers.TryAdd(plr.Account.Id, null);
        }

        plr.RelaySession?.P2PGroup?.Leave(plr.RelaySession.HostId);
        Broadcast(new RoomLeavePlayerAckMessage(plr.Account.Id, plr.Account.Nickname, roomLeaveReason));

        _players.Remove(plr.Account.Id, out _);
        TeamManager.Leave(plr);

        plr.RoomInfo.PeerId = 0;
        plr.Room = null;

        plr.SendAsync(new RoomLeavePlayerInfoAckMessage(plr.Account.Id));
        plr.SendAsync(new ItemClearInvalidEquipItemAckMessage());
        plr.SendAsync(new ItemClearEsperChipAckMessage());

        plr.Channel?.BroadcastExcept(plr,
            new ChannelEnterPlayerAckMessage(plr.Map<Player, PlayerInfoShortDto>()));
        plr.Channel?.SendPlayerlist(plr);

        if (TeamManager.Players.Any())
        {
          ChangeMasterIfNeeded(GetPlayerWithLowestPing());
          ChangeHostIfNeeded(GetPlayerWithLowestPing());
          OnPlayerLeft(new RoomPlayerEventArgs(plr));
        }
        else
        {
          RoomManager?.Remove(this);
        }
      }
    }

    public void BeginRound(Player plr)
    {
      if (Disposed)
        return;

      if (GameState != GameState.Waiting)
        return;

      if (plr.RoomInfo.State != PlayerState.Lobby)
        return;

      if (plr != Master)
        return;

      if (IsChangingRules)
      {
        plr.SendAsync(new GameEventMessageAckMessage(GameEventMessage.RoomModeIsChanging, 0, 0, 0, ""));
        return;
      }

      var stateMachine = plr.Room.GameRuleManager.GameRule.StateMachine;

      if (stateMachine.CanFire(GameRuleStateTrigger.StartPrepare))
      {
        stateMachine.Fire(GameRuleStateTrigger.StartPrepare);
        return;
      }

      plr.SendAsync(new GameEventMessageAckMessage(GameEventMessage.CantStartGame, 0, 0, 0, ""));
    }

    public void ChangeReadyStatus(Player plr)
    {
      if (Disposed)
        return;

      if (plr.Room != this)
        return;

      if (plr == Master)
        return;

      if (IsChangingRules)
      {
        plr.SendAsync(new GameEventMessageAckMessage(GameEventMessage.RoomModeIsChanging, 0, 0, 0, ""));
        return;
      }

      if (HasStarted)
        plr.SendAsync(new RoomGameLoadingAckMessage());

      if (GameState != GameState.Waiting)
        return;

      if (GameState != GameState.Waiting)
      {
        plr.SendAsync(new GameEventMessageAckMessage(GameEventMessage.CantStartGame, 0, 0, 0, ""));
      }
      else
      {
        plr.RoomInfo.IsReady = !plr.RoomInfo.IsReady;
        Broadcast(new RoomReadyRoundAckMessage(plr.Account.Id, plr.RoomInfo.IsReady));
      }
    }

    public void IntrudeRoom(Player plr)
    {
      if (Disposed)
        return;

      if (plr.Room != this)
        return;

      if (GameState != GameState.Waiting)
      {
        if (IsChangingRules)
        {
          plr.SendAsync(new GameEventMessageAckMessage(GameEventMessage.RoomModeIsChanging, 0, 0, 0, ""));
          return;
        }

        if (GameState == GameState.Result || GameRuleState == GameRuleState.EnteringResult)
        {
          // Todo, find proper result Id
          plr.SendAsync(new GameEventMessageAckMessage(GameEventMessage.RoomModeIsChanging, 0, 0, 0, ""));
          return;
        }

        if (IsPreparing || !HasStarted)
        {
          plr.SendAsync(new GameEventMessageAckMessage(GameEventMessage.CantStartGame, 0, 0, 0, ""));
          return;
        }

        plr.SendAsync(new RoomGameLoadingAckMessage());
      }
    }

    public void SetCreator(Player plr)
    {
      Master = plr;
      Host = plr;
    }

    public bool ChangeMasterIfNeeded(Player plr, bool force = false)
    {
      if (Disposed)
        return false;

      if (Master == null)
        force = true;

      if (plr == Master || (Master?.IsLoggedIn() ?? false) && !force || !plr.IsLoggedIn())
        return false;

      Master = plr;
      Broadcast(new RoomChangeMasterAckMessage(Master.Account.Id));

      return true;
    }

    public bool ChangeHostIfNeeded(Player plr, bool force = false)
    {
      if (Disposed)
        return false;

      if (Host == null)
        force = true;

      if (Host == plr || (Host?.IsLoggedIn() ?? false) && !force || !plr.IsLoggedIn())
        return false;

      Logger.ForAccount(plr).Information("<Room {roomId}> New RoomHost - Ping:{ping} ms || force: {f}", Id,
          plr.Session.UnreliablePing, force.ToString());

      Host = plr;
      Broadcast(new RoomChangeRefereeAckMessage(Host.Account.Id));
      return true;
    }

    public void ChangeRules(ChangeRuleDto options)
    {
      ChangeRules2(options.Map<ChangeRuleDto, ChangeRuleDto2>());
    }

    public void ChangeRules2(ChangeRuleDto2 options)
    {
      if (Disposed)
        return;

      if (IsChangingRules)
      {
        Master?.SendAsync(new ServerResultAckMessage(ServerResult.RoomChangingRules));
        return;
      }

      if (options.PlayerLimit < Players.Count)
      {
        Logger.ForAccount(Master).Error("Room has more players than new limit");
        Master?.SendAsync(new MessageChatAckMessage(ChatType.Channel, Master.Account.Id, "SYSTEM",
            "Room has more players than new limit"));
        Master?.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
        return;
      }

      if (options.SpectatorLimit < (byte)TeamManager.Spectators.Count())
      {
        Logger.ForAccount(Master).Error("Room has more spectators than new limit");
        Master?.SendAsync(new MessageChatAckMessage(ChatType.Channel, Master.Account.Id, "SYSTEM",
            "Room has more players than new limit"));
        Master?.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
        return;
      }

      var gameRule = (GameRule)options.GameRule;
      if (!RoomManager.GameRuleFactory.Contains((GameRule)options.GameRule))
      {
        Logger.ForAccount(Master).Error("Game rule {0} does not exist", options.GameRule);
        Master?.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
        return;
      }

      if (string.IsNullOrWhiteSpace(options.Name) || string.IsNullOrEmpty(options.Name))
      {
        Master?.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
        return;
      }

      var israndom = false;
      var maps = GameServer.Instance.ResourceCache.GetMaps();
      var map = maps.FirstOrDefault(x => x.Value.byteId == options.MapId && x.Value.GameRule == options.GameRule)
          .Value;

      if (!Master.Channel?.RoomManager.GameRuleFactory.Contains(options.GameRule) ?? false)
      {
        Logger.ForAccount(Master)
            .Error("Game rule {gameRule} does not exist", options.GameRule);
        Master.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
        return;
      }

      if (map == null)
      {
        Logger.ForAccount(Master)
            .Error("Map {map} does not exist", options.MapId);
        Master.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
        return;
      }

      if (map.IsRandom && map.GameRule == options.GameRule)
      {
        israndom = true;
        var modeMaps = maps.Where(x => x.Value.GameRule == options.GameRule && !x.Value.IsRandom);
        var selmap = modeMaps.ElementAtOrDefault(new SecureRandom().Next(0, modeMaps.Count()));
        options.MapId = (byte)selmap.Key;
      }

      map = maps.GetValueOrDefault(options.MapId);

      Logger.ForAccount(Master).Information("ChangeRoom || Id: {id} Room: {mode}, {mapid}", Id, options.GameRule,
          options.MapId);

      if (options.GameRule != GameRule.Practice &&
          options.GameRule != GameRule.CombatTrainingTD &&
          options.GameRule != GameRule.CombatTrainingDM)
      {
        if (map.GameRule != options.GameRule)
        {
          Logger.ForAccount(Master).Error("Map {mapId}({mapName}) is not available for game rule {gameRule}",
              map.Id, map.Name, options.GameRule);
          Master.SendAsync(new ServerResultAckMessage(ServerResult.FailedToCreateRoom));
          return;
        }

        if (options.GameRule == GameRule.Practice)
        {
          if (!Namecheck.IsNameValid(options.Name, true))
          {
            Master.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
            return;
          }
        }
      }

      if (options.PlayerLimit > map.MaxPlayers)
      {
        Logger.ForAccount(Master).Error("Wrong playerLimit for Map {0}", map.Id);
        Master.SendAsync(new ServerResultAckMessage(ServerResult.FailedToCreateRoom));
        return;
      }

      var isfriendly = false;
      var isburning = false;
      var isWithoutStats = false;

      switch (options.FMBurnMode)
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

      _changingRulesTimer = TimeSpan.Zero;
      IsChangingRules = true;

      Options.ChangeRuleId = options.ChangeRuleId;
      Options.Name = options.Name;
      Options.MapId = options.MapId;
      Options.PlayerLimit = options.PlayerLimit;
      Options.GameRule = gameRule;
      Options.TimeLimit = TimeSpan.FromMinutes(options.Time);
      Options.ScoreLimit = options.Points;
      Options.Password = options.Password;
      Options.IsFriendly = isfriendly;
      Options.IsBurning = isburning;
      Options.IsRandom = israndom;
      Options.ItemLimit = (byte)options.ItemLimit;
      Options.HasSpectator = options.HasSpectator;
      Options.SpectatorLimit = options.SpectatorLimit;
      Options.IsWithoutStats = isWithoutStats;

      Players.Values.ToList().ForEach(playr => { playr.RoomInfo.IsReady = false; });

      foreach (var plr in Players.Values)
      {
        _roomChangePlayers.Add(plr, plr.RoomInfo.Mode);

        if (TeamManager.ContainsKey(Team.Alpha) && plr.RoomInfo.Team.Team == Team.Alpha)
          _roomChangeAlphaPlayers.Add(plr, plr.RoomInfo.Mode);

        if (TeamManager.ContainsKey(Team.Beta) && plr.RoomInfo.Team.Team == Team.Beta)
          _roomChangeBetaPlayers.Add(plr, plr.RoomInfo.Mode);
      }

      GameRuleManager.MapInfo = GameServer.Instance.ResourceCache.GetMaps()[Options.MapId];
      GameRuleManager.GameRule = RoomManager.GameRuleFactory.Get(Options.GameRule, this);
      BroadcastExcept(Master,
          new RoomChangeRuleNotifyAck2Message(Options.Map<RoomCreationOptions, ChangeRuleDto2>()));
    }

    private Player GetPlayerWithLowestPing()
    {
      return TeamManager.Players.OrderBy(x => x.Session?.UnreliablePing ?? double.MaxValue).FirstOrDefault();
    }

    private void TeamManager_TeamChanged(object sender, TeamChangedEventArgs e)
    {
      // RoomManager.Channel.Broadcast(new SUserDataAckMessage(e.Player.Map<Player, UserDataDto>()));
    }

    private void GameRuleManager_OnGameRuleChanged(object sender, EventArgs e)
    {
      if (Disposed)
        return;

      GameRuleManager.GameRule.StateMachine.OnTransitioned(t => OnStateChanged());

      try
      {
        if (TeamManager.ContainsKey(Team.Alpha))
        {
          foreach (var plrI in _roomChangeAlphaPlayers)
          {
            var plr = plrI.Key;

            TeamManager.JoinDirectly(plr, Team.Alpha);
            TeamManager.ChangeMode(plr, plrI.Value);
          }
        }

        if (TeamManager.ContainsKey(Team.Beta))
        {
          foreach (var plrI in _roomChangeBetaPlayers)
          {
            var plr = plrI.Key;

            TeamManager.JoinDirectly(plr, Team.Beta);
            TeamManager.ChangeMode(plr, plrI.Value);
          }
        }

        foreach (var plr in Players.Values)
        {
          plr.RoomInfo.Stats = GameRuleManager.GameRule.GetPlayerRecord(plr);
        }
      }
      catch (Exception exception)
      {
        Logger.Error(exception.ToString());
      }
      finally
      {
        _roomChangePlayers.Clear();
        _roomChangeAlphaPlayers.Clear();
        _roomChangeBetaPlayers.Clear();
        BroadcastBriefing();
      }
    }

    #region Events

    public event EventHandler<RoomPlayerEventArgs> PlayerJoining;
    public event EventHandler<RoomPlayerEventArgs> PlayerJoined;
    public event EventHandler<RoomPlayerEventArgs> PlayerLeft;
    public event EventHandler StateChanged;

    internal virtual byte GetFMBurnModeInfo()
    {
      byte fmBurnMode = 0;
      if (Options.IsFriendly && Options.IsWithoutStats)
        fmBurnMode = 5;
      else if (Options.IsWithoutStats)
        fmBurnMode = 4;
      else if (Options.IsFriendly && Options.IsBurning)
        fmBurnMode = 3;
      else if (Options.IsBurning)
        fmBurnMode = 2;
      else if (Options.IsFriendly)
        fmBurnMode = 1;
      else if (!Options.IsFriendly && !Options.IsBurning)
        fmBurnMode = 0;
      return fmBurnMode;
    }

    internal virtual RoomDto GetRoomInfo()
    {
      var roomDto = new RoomDto
      {
        RoomId = (byte)Id,
        PlayerCount = (byte)Players.Count,
        PlayerLimit = Options.PlayerLimit,
        State = (byte)GameRuleManager.GameRule.StateMachine.State,
        GameRule = (int)Options.GameRule,
        Map = (byte)Options.MapId,
        WeaponLimit = Options.ItemLimit,
        Name = Options.Name,
        Password = Options.Password,
        FMBURNMode = GetFMBurnModeInfo(),
        HasSpectator = Options.HasSpectator,
        IsRandom = Options.IsRandom ? 1 : 0,
        CreationId = Options.UniqueId
      };
      return roomDto;
    }

    internal virtual void OnPlayerJoining(RoomPlayerEventArgs e)
    {
      if (Disposed)
        return;
      PlayerJoining?.Invoke(this, e);
      RoomManager.Channel.BroadcastCencored(new RoomChangeRoomInfoAck2Message(GetRoomInfo()));
    }

    internal virtual void OnPlayerJoined(RoomPlayerEventArgs e)
    {
      if (Disposed)
        return;
      PlayerJoined?.Invoke(this, e);
      RoomManager.Channel.BroadcastCencored(new RoomChangeRoomInfoAck2Message(GetRoomInfo()));
    }

    protected virtual void OnPlayerLeft(RoomPlayerEventArgs e)
    {
      if (Disposed)
        return;
      PlayerLeft?.Invoke(this, e);
      RoomManager.Channel.BroadcastCencored(new RoomChangeRoomInfoAck2Message(GetRoomInfo()));
    }

    protected virtual void OnStateChanged()
    {
      if (Disposed)
        return;
      StateChanged?.Invoke(this, EventArgs.Empty);
      RoomManager.Channel.BroadcastCencored(new RoomChangeRoomInfoAck2Message(GetRoomInfo()));
    }

    #endregion

    #region Broadcast

    public void BroadcastNotice(string message)
    {
      Broadcast(new NoticeAdminMessageAckMessage(message));
    }

    public void Broadcast(IGameMessage message)
    {
      foreach (var plr in Players.Values)
        plr.SendAsync(message);
    }

    public void Broadcast(IGameRuleMessage message)
    {
      foreach (var plr in Players.Values)
        plr.SendAsync(message);
    }

    public void BroadcastExcept(Player blacklisted, IGameRuleMessage message)
    {
      foreach (var plr in Players.Values.Where(x => x != blacklisted))
        plr.SendAsync(message);
    }

    public void BroadcastExcept(Player blacklisted, IGameMessage message)
    {
      foreach (var plr in Players.Values.Where(x => x != blacklisted))
        plr.SendAsync(message);
    }

    public void BroadcastExcept(Player blacklisted, IChatMessage message)
    {
      foreach (var plr in Players.Values.Where(x => x != blacklisted))
        plr.SendAsync(message);
    }

    public void BroadcastExcept(List<Player> blacklist, IGameMessage message)
    {
      foreach (var plr in Players.Values.Where(x => !blacklist.Contains(x)))
        plr.SendAsync(message);
    }

    public void BroadcastExcept(List<Player> blacklist, IGameRuleMessage message)
    {
      foreach (var plr in Players.Values.Where(x => !blacklist.Contains(x)))
        plr.SendAsync(message);
    }

    public void BroadcastExcept(List<Player> blacklist, IChatMessage message)
    {
      foreach (var plr in Players.Values.Where(x => !blacklist.Contains(x)))
        plr.SendAsync(message);
    }

    public void Broadcast(IChatMessage message)
    {
      foreach (var plr in TeamManager.Players)
        plr.SendAsync(message);
    }

    public void SendBriefing(Player plr, bool isResult = false)
    {
      var gameRule = GameRuleManager.GameRule;
      plr.SendAsync(new GameBriefingInfoAckMessage(isResult, false, gameRule.Briefing.ToArray(isResult)));
    }

    public void BroadcastBriefing(bool isResult = false)
    {
      var gameRule = GameRuleManager.GameRule;
      Broadcast(new GameBriefingInfoAckMessage(isResult, false, gameRule.Briefing.ToArray(isResult)));
    }

    #endregion
  }
}