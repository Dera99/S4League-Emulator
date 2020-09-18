// ReSharper disable once Checknamespace 

using NeoNetsphere.Network;

namespace NeoNetsphere.Game.GameRules
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using NeoNetsphere;
  using NeoNetsphere.Network.Data.GameRule;
  using NeoNetsphere.Network.Message.GameRule;
  using Serilog;
  using Serilog.Core;
  using Stateless;

  internal abstract class GameRuleBase
  {
    private static readonly TimeSpan PreHalfTimeWaitTime = TimeSpan.FromSeconds(9);
    private static readonly TimeSpan PreResultWaitTime = TimeSpan.FromSeconds(9);
    private static readonly TimeSpan HalfTimeWaitTime = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan ResultWaitTime = TimeSpan.FromSeconds(14);
    private static readonly TimeSpan GameLoadMaxTime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan GameStartWaitTime = TimeSpan.FromMilliseconds(3500);

    private static readonly ILogger Logger =
        Log.ForContext(Constants.SourceContextPropertyName, nameof(GameRuleBase));

    protected GameRuleBase(Room room)
    {
      Room = room;
      StateMachine = new StateMachine<GameRuleState, GameRuleStateTrigger>(GameRuleState.Waiting);
      StateMachine.OnTransitioned(StateMachine_OnTransition);
      Reload();
    }

    public abstract GameRule GameRule { get; }

    public abstract bool CountMatch { get; }

    public Room Room { get; }

    public abstract Briefing Briefing { get; }

    public StateMachine<GameRuleState, GameRuleStateTrigger> StateMachine { get; }

    public TimeSpan RoundTime { get; private set; }

    public TimeSpan GameStartTime { get; set; }

    public GameStartState PrepareState { get; set; }

    public virtual bool BlockPlaying => false;

    public virtual void Initialize()
    {
    }

    public virtual void Cleanup()
    {
    }

    public virtual void Reload()
    {
    }

    public virtual void OnRoomJoinCompleted(Player plr)
    {
    }

    public virtual void OnIntrudeCompleted(Player plr)
    {
    }

    public void RoomJoinCompleted(Player plr)
    {
      OnRoomJoinCompleted(plr);
    }

    public void IntrudeCompleted(Player plr)
    {
      UpdateTime(plr);
      plr.Session.SendAsync(new RoomGameStartAckMessage());
      OnIntrudeCompleted(plr);
    }

    public virtual void UpdateTime(Player plr)
    {
      plr?.Session?.SendAsync(new GameRefreshGameRuleInfoAckMessage(Room.GameState, Room.SubGameState,
          Room.RoundTime.TotalMilliseconds));
    }

    public void UpdateTime(TimeSpan elapsed = default(TimeSpan))
    {
      var time = Room.RoundTime.TotalMilliseconds;

      if (elapsed != default(TimeSpan))
        time = elapsed.TotalMilliseconds;

      foreach (var member in Room.Players.Values)
      {
        if (member.RoomInfo.HasLoaded)
        {
          member.Session.SendAsync(new GameRefreshGameRuleInfoAckMessage(Room.GameState,
              Room.SubGameState, time));
        }
      }
    }

    public virtual void Update(AccurateDelta delta)
    {
      RoundTime += delta.delta;
      Room.RoundTime = RoundTime;

      #region PrepareGame

      if (StateMachine.IsInState(GameRuleState.Preparing))
      {
        switch (PrepareState)
        {
          case GameStartState.Loading:
            if (Room.RoundTime > GameLoadMaxTime)
            {
              foreach (var player in Room.Players.Values.Where(x =>
                  (x.RoomInfo.IsReady || Room.Master == x) && !x.RoomInfo.HasLoaded))
              {
                player.RoomInfo.IsReady = false;
                player.Room?.Leave(player);
              }
            }

            if (Room.Players.Values.Count(x => x.RoomInfo.HasLoaded) >=
                Room.Players.Values.Count(x => x.RoomInfo.IsReady || Room.Master == x) ||
                Room.RoundTime > GameLoadMaxTime)
            {
              GameStartTime = Room.RoundTime;

              if (GameRule == GameRule.Chaser ||
                  GameRule == GameRule.Practice ||
                  GameRule == GameRule.CombatTrainingDM ||
                  GameRule == GameRule.CombatTrainingTD)
              {
                PrepareState = GameStartState.ReadyToStart;
              }
              else
              {
                PrepareState = GameStartState.Countdown;

                foreach (var member in Room.Players.Values)
                {
                  if (member.RoomInfo.HasLoaded)
                  {
                    member.Session.SendAsync(
                        new RoomGamePlayCountDownAckMessage(
                            (int)GameStartWaitTime.TotalMilliseconds));
                    member.Session.SendAsync(new GameRefreshGameRuleInfoAckMessage(Room.GameState,
                        GameTimeState.StartGameCounter,
                        Room.Options.TimeLimit.TotalMilliseconds));
                  }
                }
              }
            }

            break;

          case GameStartState.Countdown:
            if ((Room.RoundTime - GameStartTime).TotalMilliseconds >
                GameStartWaitTime.TotalMilliseconds + 500)
              PrepareState = GameStartState.ReadyToStart;
            break;

          case GameStartState.ReadyToStart:
            if (StateMachine.CanFire(GameRuleStateTrigger.StartGame))
            {
              Room.IsPreparing = false;
              RoundTime = TimeSpan.Zero;
              PrepareState = GameStartState.Playing;
              StateMachine.Fire(GameRuleStateTrigger.StartGame);
            }

            break;
        }
      }

      #endregion

      #region Playing

      if (StateMachine.IsInState(GameRuleState.Playing))
      {
        foreach (var plr in Room.TeamManager.PlayersPlaying)
        {
          plr.RoomInfo.PlayTime += delta.delta;
          plr.RoomInfo.CharacterPlayTime[plr.CharacterManager.CurrentSlot] += delta.delta;
        }
      }

      #endregion

      #region HalfTime

      if (StateMachine.IsInState(GameRuleState.EnteringHalfTime))
      {
        if (RoundTime >= PreHalfTimeWaitTime)
        {
          if (StateMachine.CanFire(GameRuleStateTrigger.StartHalfTime))
          {
            RoundTime = TimeSpan.Zero;
            StateMachine.Fire(GameRuleStateTrigger.StartHalfTime);
          }
        }
        else
        {
          foreach (var plr in Room.TeamManager.PlayersPlaying)
          {
            var message = ((int)(PreHalfTimeWaitTime - RoundTime).TotalSeconds + 1).ToString();
            plr.Session.SendAsync(new GameEventMessageAckMessage(GameEventMessage.HalfTimeIn, 2, 0, 0,
                message));
          }
        }
      }

      if (StateMachine.IsInState(GameRuleState.HalfTime) &&
          RoundTime >= HalfTimeWaitTime)
      {
        StateMachine.Fire(GameRuleStateTrigger.StartSecondHalf);
      }

      #endregion

      #region Result

      if (StateMachine.IsInState(GameRuleState.EnteringResult))
      {
        if (RoundTime >= PreResultWaitTime)
        {
          RoundTime = TimeSpan.Zero;
          StateMachine.Fire(GameRuleStateTrigger.StartResult);
        }
        else
        {
          foreach (var plr in Room.TeamManager.PlayersPlaying)
          {
            var message = (int)(PreResultWaitTime - RoundTime).TotalSeconds + 1 + " second(s)";
            plr.Session?.SendAsync(new GameEventMessageAckMessage(GameEventMessage.ResultIn, 3, 0, 0,
                message));
          }
        }
      }

      if (StateMachine.IsInState(GameRuleState.Result) &&
          RoundTime >= ResultWaitTime)
      {
        if (StateMachine.CanFire(GameRuleStateTrigger.EndGame))
        {
          RoundTime = TimeSpan.Zero;
          StateMachine.Fire(GameRuleStateTrigger.EndGame);
        }
      }

      #endregion
      #region CoinBuff
      if (this.StateMachine.IsInState(GameRuleState.Playing))
      {
        foreach (var plr in this.Room.TeamManager.PlayersPlaying)
        {
          plr.PlayerCoinBuff.Update(1);
        }
      }
      #endregion
    }

    public abstract PlayerRecord GetPlayerRecord(Player plr);

    private void StateMachine_OnTransition(
        StateMachine<GameRuleState, GameRuleStateTrigger>.Transition transition)
    {
      RoundTime = TimeSpan.Zero;

      try
      {
        switch (transition.Trigger)
        {
          case GameRuleStateTrigger.StartPrepare:
            Room.IsPreparing = true;

            foreach (var plr in Room.TeamManager.Players.Where(plr =>
                plr.RoomInfo.IsReady || Room.Master == plr ||
                plr.RoomInfo.Mode == PlayerGameMode.Spectate))
            {
              plr.Session.SendAsync(new RoomGameLoadingAckMessage());
              plr.Session.SendAsync(new RoomBeginRoundAckMessage());
              plr.RoomInfo.State = PlayerState.Waiting;
            }

            PrepareState = GameStartState.Loading;
            Room.GameState = GameState.Loading;
            Room.Broadcast(new GameChangeStateAckMessage(Room.GameState));
            return;
        }

                switch (transition.Destination)
                {
                    case GameRuleState.FullGame:
                        Room.HasStarted = true;
                        Room.GameState = GameState.Playing;

                        foreach (var team in Room.TeamManager.Values)
                            team.Score = 0;

                        foreach (var plr in Room.TeamManager.PlayersPlaying)
                        {
                            plr.Session.SendAsync(new RoomGameStartAckMessage());
                            plr.RoomInfo.State = plr.RoomInfo.Mode == PlayerGameMode.Spectate
                                ? PlayerState.Spectating
                                : PlayerState.Alive;
                        }

                        Room.Broadcast(new GameChangeStateAckMessage(Room.GameState));
                        break;
                    case GameRuleState.FirstHalf:
                        Room.HasStarted = true;
                        Room.GameState = GameState.Playing;
                        Room.SubGameState = GameTimeState.FirstHalf;

                        UpdateTime(TimeSpan.FromMilliseconds(-5));
                        foreach (var team in Room.TeamManager.Values)
                            team.Score = 0;

                        foreach (var plr in Room.TeamManager.PlayersPlaying)
                        {
                            plr.Session.SendAsync(new RoomGameStartAckMessage());
                            plr.RoomInfo.State = plr.RoomInfo.Mode == PlayerGameMode.Spectate
                                ? PlayerState.Spectating
                                : PlayerState.Alive;
                        }

                        Room.Broadcast(new GameChangeStateAckMessage(Room.GameState));
                        break;
                    case GameRuleState.HalfTime:
                        foreach (var plr in Room.TeamManager.PlayersPlaying)
                        {
                            plr.RoomInfo.State = PlayerState.Waiting;
                        }

                        Room.SubGameState = GameTimeState.HalfTime;
                        Room.Broadcast(new GameChangeSubStateAckMessage(Room.SubGameState));
                        break;
                    case GameRuleState.SecondHalf:
                        foreach (var plr in Room.TeamManager.PlayersPlaying)
                        {
                            plr.RoomInfo.State = plr.RoomInfo.Mode == PlayerGameMode.Spectate
                                ? PlayerState.Spectating
                                : PlayerState.Alive;
                        }

                        Room.SubGameState = GameTimeState.SecondHalf;
                        Room.Broadcast(new GameChangeSubStateAckMessage(Room.SubGameState));
                        break;
                    case GameRuleState.Result:
                        foreach (var plr in Room.TeamManager.Players.Where(plr =>
                            plr.RoomInfo.State != PlayerState.Lobby))
                            plr.RoomInfo.State = PlayerState.Waiting;

                        var winners = new List<Player>();
                        foreach (var plr in Room.GameRuleManager.GameRule.Briefing.GetWinnerTeam().Keys)
                        {
                            if (CountMatch) plr.stats.Won++;

                            winners.Add(plr);
                        }
                        if (CountMatch)
                        {
                            foreach (Player item7 in Room.TeamManager.PlayersPlaying)
                            {
                                if (!winners.Contains(item7))
                                {
                                    item7.stats.Loss++;
                                }
                                foreach (Character item8 in item7.CharacterManager)
                                {
                                    int loss = (int)(item7.RoomInfo.CharacterPlayTime[item8.Slot].TotalMinutes * (double)Config.Instance.Game.DurabilityLossPerMinute + (double)(item7.RoomInfo.Stats.Deaths * Config.Instance.Game.DurabilityLossPerDeath));
                                    item8.CharacterManager.DecreaseDurability(loss);
                                }
                                MPFill(item7);
                            }
                        }
                

            Room.HasStarted = false;
            Room.GameState = GameState.Result;
            Room.SubGameState = GameTimeState.None;
            Room.Broadcast(new GameChangeStateAckMessage(Room.GameState));
            Room.BroadcastBriefing(true);
            break;
          case GameRuleState.Waiting:
            foreach (var team in Room.TeamManager.Values)
              team.Score = 0;

            foreach (var plr in Room.TeamManager.Players)
            {
              plr.RoomInfo.Reset();
              plr.RoomInfo.State = PlayerState.Lobby;
              OnRoomJoinCompleted(plr);
            }

            Reload();

            Room.HasStarted = false;
            Room.GameState = GameState.Waiting;
            Room.SubGameState = GameTimeState.None;
            Room.Broadcast(new GameChangeStateAckMessage(Room.GameState));
            PrepareState = GameStartState.Waiting;
            Room.BroadcastBriefing();
            break;
        }
      }
      catch (Exception e)
      {
        Logger.Error(e.ToString());
      }
    }

        #region Scores
        public virtual void MPFill(Player plr)
        {
            foreach (Character item in plr.CharacterManager)
            {
                int num = (int)plr.RoomInfo.CharacterPlayTime[item.Slot].TotalSeconds;
                uint[] array = new uint[25]
                {
                    168,
                    168,
                    336,
                    336,
                    504,
                    504,
                    672,
                    672,
                    840,
                    840,
                    1176,
                    1176,
                    1512,
                    1680,
                    2016,
                    2688,
                    3360,
                    4032,
                    4872,
                    6048,
                    6048,
                    6048,
                    6048,
                    6048,
                    6048
                };
                foreach (PlayerItem item2 in item.Weapons.GetItems().Where(delegate (PlayerItem item1)
                {
                    return item1!= null && item1.Durability != -1;
                }))
                {
                    // item2.EnchantMP += (uint)num / Config.Instance.Game.WeaponEnchantMPFill;
                    item2.EnchantMP += (uint)num / 1;
                    if (item2.EnchantMP > array[item2.EnchantLvl])
                    {
                        item2.EnchantMP = array[item2.EnchantLvl];
                    }
                    plr.Inventory.Update(item2);
                }
                /* //MPFill For Costumes
                foreach (PlayerItem item2 in item.Costumes.GetItems().Where(delegate (PlayerItem item1)
                {
                    return item1 != null && item1.Durability != -1;
                }))
                {
                    item2.EnchantMP += (uint)num / Config.Instance.Game.CostumeEnchantMPFill;
                    if (item2.EnchantMP > array[item2.EnchantLvl])
                    {
                        item2.EnchantMP = array[item2.EnchantLvl];
                    }
                    plr.Inventory.Update(item2);
                }*/
            }
        }
    public virtual void Respawn(Player victim)
    {
      if (victim == null)
        return;

      victim.RoomInfo.State = PlayerState.Dead;
      victim.Session.SendAsync(new InGamePlayerResponseOfDeathAckMessage());
    }

    public virtual void OnScoreKill(Player killer, Player assist, Player target, AttackAttribute attackAttribute,
        LongPeerId scoreTarget, LongPeerId scoreKiller, LongPeerId scoreAssist)
    {
      var realplayerKiller = (killer?.RoomInfo.PeerId.EqualSlot(scoreKiller) ?? false) &&
                             killer.RoomInfo.PeerId.IsPlayer();
      var realplayerTarget = (target?.RoomInfo.PeerId.EqualSlot(scoreTarget) ?? false) &&
                             target.RoomInfo.PeerId.IsPlayer();
      var realplayerAssist = (assist?.RoomInfo.PeerId.EqualSlot(scoreAssist) ?? false) &&
                             assist.RoomInfo.PeerId.IsPlayer();

      if (realplayerTarget)
        Respawn(target);

      if (!ScoreIsPlaying())
        return;

      if (realplayerKiller)
      {
        killer.RoomInfo.Stats.Kills++;
        killer.stats.Kills++;
      }

      if (realplayerTarget)
      {
        target.RoomInfo.Stats.Deaths++;
        target.stats.Deaths++;
      }

      if (realplayerAssist)
      {
        assist.RoomInfo.Stats.KillAssists++;
        assist.stats.KillAssists++;
        Room.Broadcast(new ScoreKillAssistAckMessage(new ScoreAssistDto(scoreKiller, scoreAssist, scoreTarget,
            attackAttribute)));
      }
      else
      {
        Room.Broadcast(new ScoreKillAckMessage(new ScoreDto(scoreKiller, scoreTarget, attackAttribute)));
      }
      if (killer.PlayerCoinBuff.FindBuff(BuffType.PEN).IsEnabled)
      {
        killer.LuckyShot.TryShot(LuckyShotType.PEN);
      }

      if (killer.PlayerCoinBuff.FindBuff(BuffType.EXP).IsEnabled)
      {
        killer.LuckyShot.TryShot(LuckyShotType.EXP);
      }
    }

    public virtual void OnScoreTeamKill(Player killer, Player target, AttackAttribute attackAttribute,
        LongPeerId scoreKiller, LongPeerId scoreTarget)
    {
      var realplayerTarget = (target?.RoomInfo.PeerId.EqualSlot(scoreTarget) ?? false) &&
                             target.RoomInfo.PeerId.IsPlayer();

      if (realplayerTarget)
        Respawn(target);

      if (!ScoreIsPlaying())
        return;

      if (realplayerTarget)
      {
        Respawn(target);
        target.RoomInfo.Stats.Deaths++;
        target.stats.Deaths++;
      }

      Room.Broadcast(new ScoreTeamKillAckMessage(new Score2Dto(scoreKiller, scoreTarget, attackAttribute)));
    }

    public virtual void OnScoreHeal(Player plr, LongPeerId scorePlr)
    {
      if (!ScoreIsPlaying())
        return;

      var realplayer = (plr?.RoomInfo.PeerId.EqualSlot(scorePlr) ?? false) && plr.RoomInfo.PeerId.IsPlayer();

      if (realplayer)
      {
        plr.stats.Heal++;
      }

      Room.Broadcast(new ScoreHealAssistAckMessage(scorePlr));
    }

    public virtual void OnScoreSuicide(Player plr, LongPeerId scorePlr, AttackAttribute icon)
    {
      var realplayer = (plr?.RoomInfo.PeerId.EqualSlot(scorePlr) ?? false) && plr.RoomInfo.PeerId.IsPlayer();

      if (realplayer)
        Respawn(plr);

      if (!ScoreIsPlaying())
        return;

      if (realplayer)
      {
        plr.RoomInfo.Stats.Deaths++;
        plr.stats.Deaths++;
      }

      Room.Broadcast(new ScoreSuicideAckMessage(scorePlr, icon));
    }

    #endregion

    public bool ScoreIsPlaying()
    {
      var statePlaying = StateMachine.IsInState(GameRuleState.FirstHalf) ||
                         StateMachine.IsInState(GameRuleState.SecondHalf) ||
                         StateMachine.IsInState(GameRuleState.FullGame);

      return statePlaying && !BlockPlaying;
    }
  }
}