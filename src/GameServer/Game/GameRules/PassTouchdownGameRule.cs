using NeoNetsphere.Network;

namespace NeoNetsphere.Game.GameRules
{
  using System;
  using System.IO;
  using System.Linq;
  using NeoNetsphere.Network.Data.GameRule;
  using NeoNetsphere.Network.Message.GameRule;
  using NeoNetsphere.Game;
  using NeoNetsphere.Game.GameRules;

  internal sealed class PassTouchdownGameRule : GameRuleBase
  {
    private static readonly TimeSpan PassTouchdownWaitTime = TimeSpan.FromSeconds(12);

    private static readonly TimeSpan PassTouchdownAssistTimer = TimeSpan.FromSeconds(10);

    private DateTime LastTime { get; set; }

    private LongPeerId _ballOwner = 0;

    private Player _lastPlayer;

    private LongPeerId _lastOwner = 0;

    private bool IsAssistValid => _lastOwner != 0 && DateTime.Now - LastTime < PassTouchdownAssistTimer &&
                                  !_lastOwner.EqualSlot(_ballOwner);

    private TimeSpan _touchdownTime;

    private bool IsInPassTouchdown { get; set; }

    public override bool BlockPlaying => IsInPassTouchdown;

    public PassTouchdownGameRule(Room room)
        : base(room)
    {
      Briefing = new Briefing(this);

      StateMachine.Configure(GameRuleState.Waiting)
          .PermitIf(GameRuleStateTrigger.StartPrepare, GameRuleState.Preparing, CanStartGame);

      StateMachine.Configure(GameRuleState.Preparing)
          .Permit(GameRuleStateTrigger.StartGame, GameRuleState.FirstHalf);

      StateMachine.Configure(GameRuleState.FirstHalf)
          .SubstateOf(GameRuleState.Playing)
          .Permit(GameRuleStateTrigger.StartHalfTime, GameRuleState.EnteringHalfTime)
          .Permit(GameRuleStateTrigger.StartResult, GameRuleState.EnteringResult);

      StateMachine.Configure(GameRuleState.EnteringHalfTime)
          .SubstateOf(GameRuleState.Playing)
          .Permit(GameRuleStateTrigger.StartHalfTime, GameRuleState.HalfTime)
          .Permit(GameRuleStateTrigger.StartResult, GameRuleState.EnteringResult);

      StateMachine.Configure(GameRuleState.HalfTime)
          .SubstateOf(GameRuleState.Playing)
          .Permit(GameRuleStateTrigger.StartSecondHalf, GameRuleState.SecondHalf)
          .Permit(GameRuleStateTrigger.StartResult, GameRuleState.EnteringResult);

      StateMachine.Configure(GameRuleState.SecondHalf)
          .SubstateOf(GameRuleState.Playing)
          .Permit(GameRuleStateTrigger.StartResult, GameRuleState.EnteringResult);

      StateMachine.Configure(GameRuleState.EnteringResult)
          .SubstateOf(GameRuleState.Playing)
          .Permit(GameRuleStateTrigger.StartResult, GameRuleState.Result);

      StateMachine.Configure(GameRuleState.Result)
          .SubstateOf(GameRuleState.Playing)
          .Permit(GameRuleStateTrigger.EndGame, GameRuleState.Waiting);
    }

    public override bool CountMatch => true;

    public override GameRule GameRule => GameRule.PassTouchdown;

    public override Briefing Briefing { get; }

    public override void Initialize()
    {
      var playerLimit = (uint)Room.Options.PlayerLimit / 2;
      var spectatorLimit = (uint)Room.Options.SpectatorLimit / 2;

      Room.TeamManager.Add(Team.Alpha, playerLimit, spectatorLimit);
      Room.TeamManager.Add(Team.Beta, playerLimit, spectatorLimit);
      base.Initialize();
    }

    public override void Cleanup()
    {
      Room.TeamManager.Remove(Team.Alpha);
      Room.TeamManager.Remove(Team.Beta);
      base.Cleanup();
    }

    public override void Update(AccurateDelta delta)
    {
      base.Update(delta);

      try
      {
        var teamMgr = Room.TeamManager;

        if (Room.GameState == GameState.Playing &&
            !StateMachine.IsInState(GameRuleState.EnteringResult) &&
            !StateMachine.IsInState(GameRuleState.Result) &&
            RoundTime >= TimeSpan.FromSeconds(5))
        {
          // Still have enough players?
          var min = teamMgr.Values.Min(team =>
              team.Keys.Count(plr =>
                  plr.RoomInfo.State != PlayerState.Lobby &&
                  plr.RoomInfo.State != PlayerState.Spectating));

          if (min == 0 && !Room.Options.IsFriendly)
            StateMachine.Fire(GameRuleStateTrigger.StartResult);

          var isFirstHalf = StateMachine.IsInState(GameRuleState.FirstHalf);
          var isSecondHalf = StateMachine.IsInState(GameRuleState.SecondHalf);
          if (isFirstHalf || isSecondHalf)
          {
            var scoreLimit = isFirstHalf ? Room.Options.ScoreLimit / 2 : Room.Options.ScoreLimit;
            var trigger = isFirstHalf
                ? GameRuleStateTrigger.StartHalfTime
                : GameRuleStateTrigger.StartResult;

            // Did we reach ScoreLimit?
            if (teamMgr.Values.Any(team => team.Score >= scoreLimit) &&
                StateMachine.CanFire(trigger))
              StateMachine.Fire(trigger);

            // Did we reach round limit?
            var roundTimeLimit = TimeSpan.FromMilliseconds(Room.Options.TimeLimit.TotalMilliseconds / 2);
            if (RoundTime >= roundTimeLimit &&
                StateMachine.CanFire(trigger))
              StateMachine.Fire(trigger);
          }

          if (IsInPassTouchdown)
          {
            _touchdownTime += delta.delta;
            if (!StateMachine.IsInState(GameRuleState.EnteringHalfTime) &&
                !StateMachine.IsInState(GameRuleState.HalfTime) &&
                !StateMachine.IsInState(GameRuleState.EnteringResult) &&
                !StateMachine.IsInState(GameRuleState.Result))
            {
              if (_touchdownTime >= PassTouchdownWaitTime)
              {
                IsInPassTouchdown = false;
                _touchdownTime = TimeSpan.Zero;
                Room.Broadcast(new GameEventMessageAckMessage(GameEventMessage.ResetRound, 0, 0, 0, ""));
              }
            }
            else
            {
              IsInPassTouchdown = false;
            }
          }
        }
      }
      catch (Exception e)
      {
        Room.Logger.Error(e.ToString());
      }
    }

    public override PlayerRecord GetPlayerRecord(Player plr)
    {
      return new PassTouchdownPlayerRecord(plr);
    }

    private static PassTouchdownPlayerRecord GetRecord(Player plr)
    {
      return (PassTouchdownPlayerRecord)plr.RoomInfo.Stats;
    }

    public static TDStats GetStats(Player plr)
    {
      return plr.stats.GetTDStats();
    }

    public void OnScoreOffense(Player killer, Player assist, Player target, AttackAttribute attackAttribute,
        LongPeerId scoreTarget, LongPeerId scoreKiller, LongPeerId scoreAssist)
    {
      var realplayerTarget = (target?.RoomInfo.PeerId.EqualSlot(scoreTarget) ?? false) &&
                             target.RoomInfo.PeerId.IsPlayer();
      var realplayerAssist = (assist?.RoomInfo.PeerId.EqualSlot(scoreAssist) ?? false) &&
                             assist.RoomInfo.PeerId.IsPlayer();

      if (realplayerTarget)
        Respawn(target);

      if (!ScoreIsPlaying())
        return;

      if (realplayerTarget)
      {
        if (killer != null)
        {
          GetRecord(killer).OffenseScore++;
          GetStats(killer).Offense++;
        }
      }

      if (realplayerAssist)
      {
        GetRecord(assist).OffenseAssistScore++;
        GetStats(assist).OffenseAssist++;
      }

      if (scoreAssist != null)
        Room.Broadcast(new ScoreOffenseAssistAckMessage(new ScoreAssistDto(scoreKiller, scoreAssist,
            scoreTarget, attackAttribute)));
      else
        Room.Broadcast(new ScoreOffenseAckMessage(new ScoreDto(scoreKiller, scoreTarget, attackAttribute)));
    }

    public void OnScoreDefense(Player killer, Player assist, Player target, AttackAttribute attackAttribute,
        LongPeerId scoreTarget, LongPeerId scoreKiller, LongPeerId scoreAssist)
    {
      var realplayerTarget = (target?.RoomInfo.PeerId.EqualSlot(scoreTarget) ?? false) &&
                             target.RoomInfo.PeerId.IsPlayer();
      var realplayerAssist = (assist?.RoomInfo.PeerId.EqualSlot(scoreAssist) ?? false) &&
                             assist.RoomInfo.PeerId.IsPlayer();

      if (realplayerTarget)
        Respawn(target);

      if (!ScoreIsPlaying())
        return;

      if (realplayerTarget)
      {
        if (killer != null)
        {
          GetRecord(killer).DefenseScore++;
          GetStats(killer).Defense++;
        }
      }

      if (realplayerAssist)
      {
        GetRecord(assist).DefenseAssistScore++;
        GetStats(assist).DefenseAssist++;
      }

      if (scoreAssist != null)
        Room.Broadcast(new ScoreDefenseAssistAckMessage(new ScoreAssistDto(scoreKiller, scoreAssist,
            scoreTarget, attackAttribute)));
      else
        Room.Broadcast(new ScoreDefenseAckMessage(new ScoreDto(scoreKiller, scoreTarget, attackAttribute)));
    }

    // Todo ---!! Not used in PassTouchdown!
    public void OnScoreRebound(Player newPlr, Player oldPlr, LongPeerId newid, LongPeerId oldId)
    {
      if (!ScoreIsPlaying())
        return;

      if (oldPlr != null)
      {
        _lastPlayer = oldPlr;
      }

      _lastOwner = _ballOwner;
      _ballOwner = newid;
      LastTime = DateTime.Now;

      Room.Broadcast(new ScoreReboundAckMessage(newid, oldId));
    }

    public void OnScoreGoal(Player plr, LongPeerId scoreTarget)
    {
      if (!ScoreIsPlaying())
        return;

      IsInPassTouchdown = true;

      var realPlayer = (plr?.RoomInfo.PeerId.EqualSlot(scoreTarget) ?? false) && plr.RoomInfo.PeerId.IsPlayer();
      var realPlayerAssist = (_lastPlayer?.RoomInfo.PeerId.EqualSlot(_lastOwner) ?? false) &&
                             _lastPlayer.RoomInfo.PeerId.IsPlayer();

      if (realPlayer)
      {
        plr.RoomInfo.Team.Score++;
        GetRecord(plr).TDScore++;
        GetStats(plr).TD++;
      }

      var validTeam = false;
      if (realPlayerAssist && _lastPlayer.RoomInfo.Team != plr?.RoomInfo.Team)
      {
        var assist = _lastPlayer;
        GetRecord(assist).TDAssistScore++;
        GetStats(assist).TDAssist++;
        validTeam = true;
      }

      if (IsAssistValid && validTeam)
      {
        // Todo ---!! Not used in PassTouchdown!
        Room.Broadcast(new ScoreGoalAssistAckMessage(_ballOwner, _lastOwner));
      }
      else
      {
        Room.Broadcast(new ScoreGoalAckMessage(scoreTarget));
      }

      _ballOwner = 0;
      _lastOwner = 0;
      _lastPlayer = null;

      var halfTime = TimeSpan.FromSeconds(Room.Options.TimeLimit.TotalSeconds / 2);
      var diff = halfTime - RoundTime;
      if (diff <= TimeSpan.FromSeconds(10)) // ToDo use const
        return;

      Room.Broadcast(new GameEventMessageAckMessage(GameEventMessage.NextRoundIn,
          (ulong)PassTouchdownWaitTime.TotalMilliseconds, 0, 0, ""));
      _touchdownTime = TimeSpan.Zero;
    }

    public override void OnScoreHeal(Player plr, LongPeerId scoreTarget)
    {
      if (!ScoreIsPlaying())
        return;

      base.OnScoreHeal(plr, scoreTarget);

      var realPlayer = (plr?.RoomInfo.PeerId.EqualSlot(scoreTarget) ?? false) && plr.RoomInfo.PeerId.IsPlayer();
      if (realPlayer)
        GetRecord(plr).HealScore++;
    }

    private bool CanStartGame()
    {
      if (!StateMachine.IsInState(GameRuleState.Waiting))
        return false;

      var teams = Room.TeamManager.Values.ToArray();
      if (Room.Options.IsFriendly)
        return true;
      if (teams.Any(team => team.Count == 0)) // Do we have enough players?
        return false;

      // Is atleast one player per team ready?
      return teams.All(team => team.Players.Any(plr => plr.RoomInfo.IsReady || Room.Master == plr));
    }
  }

  internal class PassTouchdownPlayerRecord : PlayerRecord
  {
    public PassTouchdownPlayerRecord(Player plr)
        : base(plr)
    {
    }

    public override uint TotalScore => GetTotalScore();

    public uint TDScore { get; set; }
    public uint TDAssistScore { get; set; }
    public uint OffenseScore { get; set; }
    public uint OffenseAssistScore { get; set; }
    public uint DefenseScore { get; set; }
    public uint DefenseAssistScore { get; set; }
    public uint HealScore { get; set; }
    public uint HealAssistScore { get; set; }
    public uint Unk2 { get; set; }
    public uint Unk3 { get; set; }
    public uint OffenseReboundScore { get; set; }
    public uint Unk4 { get; set; } // increases total score x*4
    public uint Unk5 { get; set; }

    public override void Serialize(BinaryWriter w, bool isResult)
    {
      base.Serialize(w, isResult);

      w.Write(TDScore);
      w.Write(TDAssistScore);
      w.Write(Kills);
      w.Write(KillAssists);
      w.Write(OffenseScore);
      w.Write(OffenseAssistScore);
      w.Write(DefenseScore);
      w.Write(DefenseAssistScore);
      w.Write(HealScore);
      w.Write(HealAssistScore);
      w.Write(Unk2);
      w.Write(Unk3);
      w.Write(OffenseReboundScore);
      w.Write(Unk4);
      w.Write(Unk5);
    }

    public override void Reset()
    {
      base.Reset();
      TDScore = 0;
      TDAssistScore = 0;
      OffenseScore = 0;
      OffenseAssistScore = 0;
      DefenseScore = 0;
      DefenseAssistScore = 0;
      HealScore = 0;
      OffenseReboundScore = 0;

      HealAssistScore = 0;
      Unk2 = 0;
      Unk3 = 0;
      Unk4 = 0;
      Unk5 = 0;
    }

    private uint GetTotalScore()
    {
      return TDScore * 10 + TDAssistScore * 5
                          + Kills * 2 + KillAssists
                          + OffenseScore * 4 + OffenseAssistScore * 2
                          + DefenseScore * 4 + DefenseAssistScore * 2
                          + HealScore * 2
                          + OffenseReboundScore * 2;
    }

    public override int GetExpGain(out int bonusExp)
    {
      base.GetExpGain(out bonusExp);

      var config = Config.Instance.Game.TouchdownExpRates;
      var place = 1;

      var plrs = Player.Room.TeamManager.Players
          .Where(plr => plr.RoomInfo.State == PlayerState.Waiting &&
                        plr.RoomInfo.Mode == PlayerGameMode.Normal)
          .ToArray();

      foreach (var plr in plrs.OrderByDescending(plr => plr.RoomInfo.Stats.TotalScore))
      {
        if (plr == Player)
          break;

        place++;
        if (place > 3)
          break;
      }

      var rankingBonus = 0f;
      switch (place)
      {
        case 1:
          rankingBonus = config.FirstPlaceBonus;
          break;

        case 2:
          rankingBonus = config.SecondPlaceBonus;
          break;

        case 3:
          rankingBonus = config.ThirdPlaceBonus;
          break;
      }

      var newgain = TotalScore * config.ScoreFactor +
                    rankingBonus +
                    plrs.Length * config.PlayerCountFactor +
                    Player.RoomInfo.PlayTime.TotalMinutes * config.ExpPerMin;

      return (int)newgain > 5000 ? 5000 : (int)newgain;
    }
  }
}