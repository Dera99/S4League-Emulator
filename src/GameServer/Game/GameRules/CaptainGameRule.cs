using System.Collections.Concurrent;
using NeoNetsphere.Network;

namespace NeoNetsphere.Game.GameRules
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using NeoNetsphere.Network.Data.GameRule;
  using NeoNetsphere.Network.Message.GameRule;
  using NeoNetsphere.Game;
  using NeoNetsphere.Game.GameRules;

  internal class CaptainGameRule : GameRuleBase
  {
    private static readonly TimeSpan SCaptainNextroundTime = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan SCaptainRoundTime = TimeSpan.FromMinutes(3);

    public uint AlphaWins { get; set; }

    public uint BetaWins { get; set; }

    public uint CurrentRound { get; set; }

    private TimeSpan _nextRoundTime = TimeSpan.Zero;

    private TimeSpan _subRoundTime = TimeSpan.Zero;

    private bool _waitingNextRound = true;

    public readonly ConcurrentDictionary<Player, Team> PlayersCaptain = new ConcurrentDictionary<Player, Team>();

    public IEnumerable<Player> AlphaCaptains => PlayersCaptain.Where(x => x.Value == Team.Alpha).Select(x => x.Key);

    public IEnumerable<Player> BetaCaptains => PlayersCaptain.Where(x => x.Value == Team.Beta).Select(x => x.Key);

    public CaptainGameRule(Room room)
        : base(room)
    {
      Briefing = new CaptainBriefing(this);

      StateMachine.Configure(GameRuleState.Waiting)
          .PermitIf(GameRuleStateTrigger.StartPrepare, GameRuleState.Preparing, CanStartGame);

      StateMachine.Configure(GameRuleState.Preparing)
          .Permit(GameRuleStateTrigger.StartGame, GameRuleState.FullGame);

      StateMachine.Configure(GameRuleState.FullGame)
          .SubstateOf(GameRuleState.Playing)
          .Permit(GameRuleStateTrigger.StartResult, GameRuleState.EnteringResult)
          .OnEntry(NextRound);

      StateMachine.Configure(GameRuleState.EnteringResult)
          .SubstateOf(GameRuleState.Playing)
          .Permit(GameRuleStateTrigger.StartResult, GameRuleState.Result);

      StateMachine.Configure(GameRuleState.Result)
          .SubstateOf(GameRuleState.Playing)
          .Permit(GameRuleStateTrigger.EndGame, GameRuleState.Waiting);
    }

    public override GameRule GameRule => GameRule.Captain;

    public override Briefing Briefing { get; }

    public override bool CountMatch => true;

    public override bool BlockPlaying => _waitingNextRound;

    public CaptainBriefing GetBriefing()
    {
      return (CaptainBriefing)Briefing;
    }

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

    public override void Reload()
    {
      PlayersCaptain.Clear();
      AlphaWins = 0;
      BetaWins = 0;
      CurrentRound = 0;
    }

    public override void OnRoomJoinCompleted(Player plr)
    {
      if (!ScoreIsPlaying())
        return;

      plr.SendAsync(new CaptainCurrentRoundInfoAckMessage(AlphaWins, BetaWins));
      var players = PlayersCaptain.Keys.Select(x => new CaptainLifeDto(plr.Account.Id, 500));
      plr.SendAsync(new CaptainRoundCaptainLifeInfoAckMessage(players.ToArray()));
    }

    public bool ValidPlayer(Player plr)
    {
      if (plr == null)
        return false;

      if (plr.Room != Room)
        return false;

      if (!plr.RoomInfo.HasLoaded)
        return false;

      return true;
    }

    public override void Update(AccurateDelta delta)
    {
      base.Update(delta);

      var teamMgr = Room.TeamManager;
      try
      {
        if (Room.GameState == GameState.Playing &&
            !StateMachine.IsInState(GameRuleState.EnteringResult) &&
            !StateMachine.IsInState(GameRuleState.Result) &&
            RoundTime >= TimeSpan.FromSeconds(5))
        {
          var min = teamMgr.Values.Min(team =>
              team.Keys.Count(plr => plr.RoomInfo.HasLoaded));

          if (min == 0 && !Room.Options.IsFriendly)
            StateMachine.Fire(GameRuleStateTrigger.StartResult);

          if (teamMgr.Values.Any(team => team.Score > Room.Options.ScoreLimit))
            StateMachine.Fire(GameRuleStateTrigger.StartResult);

          if (CurrentRound - 1 > Room.Options.TimeLimit.Minutes)
            StateMachine.Fire(GameRuleStateTrigger.StartResult);

          if (CurrentRound > 0)
          {
            if (_waitingNextRound)
            {
              _nextRoundTime += delta.delta;

              if (_nextRoundTime >= SCaptainNextroundTime)
              {
                NextRound();
              }
            }
            else
            {
              foreach (var plr in PlayersCaptain.Where(x => !ValidPlayer(x.Key)))
              {
                PlayersCaptain.TryRemove(plr.Key, out _);
              }

              if ((!AlphaCaptains.Any() || !BetaCaptains.Any()) /* &&
                                Room.TeamManager.PlayersPlaying.Count() > 1 && !Room.Options.IsFriendly*/)
                SubRoundEnd();

              _subRoundTime += delta.delta;
              if (_subRoundTime >= SCaptainRoundTime)
                SubRoundEnd();
            }
          }
        }
      }
      catch (Exception e)
      {
        Room.Logger.Error(e.ToString());
      }
    }

    private void NextRound()
    {
      if (!_waitingNextRound)
        return;

      PlayersCaptain.Clear();
      foreach (var team in Room.TeamManager)
      {
        foreach (var plr in team.Value.NoSpectatorPlayers)
        {
          PlayersCaptain.TryAdd(plr, team.Key);
        }
      }

      CurrentRound++;

      var players = PlayersCaptain.Keys.Select(plr => new CaptainLifeDto(plr.Account.Id, 500));
      Room.Broadcast(new CaptainRoundCaptainLifeInfoAckMessage(players.ToArray()));
      Room.Broadcast(new GameEventMessageAckMessage(GameEventMessage.ResetRound, 0, 0, 0, ""));
      Room.Broadcast(new CaptainCurrentRoundInfoAckMessage(AlphaWins, BetaWins));
      _waitingNextRound = false;
    }

    private void SubRoundEnd()
    {
      if (!ScoreIsPlaying())
        return;

      _nextRoundTime = TimeSpan.Zero;
      _subRoundTime = TimeSpan.Zero;

      _waitingNextRound = true;

      PlayerTeam winnerTeam = null;

      if (AlphaCaptains.Count() > BetaCaptains.Count())
      {
        winnerTeam = Room.TeamManager[Team.Alpha];
      }
      else if (BetaCaptains.Count() > AlphaCaptains.Count())
      {
        winnerTeam = Room.TeamManager[Team.Beta];
      }
      else
      {
        var scores = new Dictionary<Team, uint>();
        foreach (var team in Room.TeamManager.Values)
        {
          var score = team.PlayersPlaying.Sum(plr => plr.RoomInfo.Stats.TotalScore);
          scores.Add(team.Team, (uint)score);
        }

        var max = scores.Values.Max();
        winnerTeam = Room.TeamManager.Values.FirstOrDefault(t => scores[t.Team] == max);
      }

      if (winnerTeam?.Team == Team.Alpha)
      {
        AlphaWins++;
      }

      if (winnerTeam?.Team == Team.Beta)
      {
        BetaWins++;
      }

      if (winnerTeam?.Team != Team.Neutral)
      {
        winnerTeam.Score++;
        Room.Broadcast(new CaptainSubRoundWinAckMessage((int)Team.Beta, false));
      }

      if (Room.TeamManager.Values.Any(team => team.Score > Room.Options.ScoreLimit))
        return;

      if (CurrentRound - 1 > Room.Options.TimeLimit.Minutes)
        return;

      Room.Broadcast(new GameEventMessageAckMessage(GameEventMessage.NextRoundIn,
          (ulong)SCaptainNextroundTime.TotalMilliseconds, 0, 0, ""));
    }

    public override PlayerRecord GetPlayerRecord(Player plr)
    {
      return new CaptainPlayerRecord(plr);
    }

    private static CaptainPlayerRecord GetRecord(Player plr)
    {
      return (CaptainPlayerRecord)plr.RoomInfo.Stats;
    }

    public override void OnScoreKill(Player killer, Player assist, Player target, AttackAttribute attackAttribute,
        LongPeerId scoreTarget, LongPeerId scoreKiller, LongPeerId scoreAssist)
    {
      base.OnScoreKill(killer, assist, target, attackAttribute, scoreTarget, scoreKiller, scoreAssist);

      if (!ScoreIsPlaying())
        return;

      if (scoreTarget.PeerId.Category == PlayerCategory.Player)
      {
        var targetTeam = target?.RoomInfo?.Team;
        if (targetTeam != null && PlayersCaptain.TryRemove(target, out _))
        {
          var killerTeam = killer?.RoomInfo?.Team;
          if (killerTeam != null)
          {
            GetRecord(killer).KillCaptains++;
            if (assist != null)
              GetRecord(assist).KillAssistCaptains++;
          }

          GetRecord(target).Deaths++;
        }
        else if (targetTeam != null)
        {
          GetRecord(killer).Kills++;
          if (assist != null)
            GetRecord(assist).KillAssists++;
        }
      }
    }

    public override void OnScoreSuicide(Player target, LongPeerId scoreTarget, AttackAttribute icon)
    {
      base.OnScoreSuicide(target, scoreTarget, icon);

      if (!ScoreIsPlaying())
        return;

      if (scoreTarget.PeerId.Category == PlayerCategory.Player)
      {
        var targetTeam = target?.RoomInfo?.Team;
        if (targetTeam != null && PlayersCaptain.TryRemove(target, out _))
        {
          GetPlayerRecord(target).Suicides++;
        }
      }
    }

    private bool CanStartGame()
    {
      if (!StateMachine.IsInState(GameRuleState.Waiting))
        return false;

      if (Room.Options.IsFriendly)
        return true;

      var teams = Room.TeamManager.Values.ToArray();
      if (teams.Any(team => team.Count == 0))
        return false;

      return teams.All(team => team.Players.Any(plr => plr.RoomInfo.IsReady || Room.Master == plr));
    }
  }

  internal class CaptainBriefing : Briefing
  {
    public CaptainBriefing(GameRuleBase ruleBase)
        : base(ruleBase)
    {
    }
  }

  internal class CaptainPlayerRecord : PlayerRecord
  {
    public CaptainPlayerRecord(Player plr)
        : base(plr)
    {
    }

    public override uint TotalScore => 5 * (WinRound + KillCaptains) + 2 * Kills + KillAssists + Heal - Suicides;
    public uint KillCaptains { get; set; }
    public uint KillAssistCaptains { get; set; }
    public uint WinRound { get; set; }
    public uint Heal { get; set; }

    public override void Serialize(BinaryWriter w, bool isResult)
    {
      base.Serialize(w, isResult);

      w.Write(KillCaptains);
      w.Write(KillAssistCaptains);
      w.Write(Kills);
      w.Write(KillAssists);
      w.Write(Heal);
      w.Write(WinRound);
    }

    public override void Reset()
    {
      base.Reset();
      KillCaptains = 0;
      KillAssistCaptains = 0;
      Heal = 0;
    }

    public override int GetExpGain(out int bonusExp)
    {
      return base.GetExpGain(out bonusExp);
      //base.GetExpGain(out bonusExp);

      //var config = Config.Instance.Game.TouchdownExpRates;
      //var place = 1;

      //var plrs = Player.Room.TeamManager.Players
      //    .Where(plr => plr.RoomInfo.State == PlayerState.Waiting &&
      //        plr.RoomInfo.Mode == PlayerGameMode.Normal)
      //    .ToArray();

      //foreach (var plr in plrs.OrderByDescending(plr => plr.RoomInfo.Stats.TotalScore))
      //{
      //    if (plr == Player)
      //        break;

      //    place++;
      //    if (place > 3)
      //        break;
      //}

      //var rankingBonus = 0f;
      //switch (place)
      //{
      //    case 1:
      //        rankingBonus = config.FirstPlaceBonus;
      //        break;

      //    case 2:
      //        rankingBonus = config.SecondPlaceBonus;
      //        break;

      //    case 3:
      //        rankingBonus = config.ThirdPlaceBonus;
      //        break;
      //}

      //return (uint)(TotalScore * config.ScoreFactor +
      //    rankingBonus +
      //    plrs.Length * config.PlayerCountFactor +
      //    Player.RoomInfo.PlayTime.TotalMinutes * config.ExpPerMin);
    }
  }
}