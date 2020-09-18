using System.Collections.Concurrent;
using System.Threading.Tasks;
using NeoNetsphere.Network;

namespace NeoNetsphere.Game.GameRules
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using BlubLib.IO;
  using NeoNetsphere.Network.Message.GameRule;
  using NeoNetsphere.Game;
  using NeoNetsphere.Game.GameRules;

  internal class ChaserGameRule : GameRuleBase
  {
    private const uint PlayersNeededToStart = 4;

    private static readonly TimeSpan SNextChaserWaitTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_spanTime = TimeSpan.FromSeconds(2);
    private readonly SecureRandom _random = new SecureRandom();

    private TimeSpan _chaserRoundTime;

    private TimeSpan _chaserTimer;

    private TimeSpan _nextChaserTimer;

    private bool _waitingNextChaser;

    public override bool BlockPlaying => _waitingNextChaser;

    public ChaserGameRule(Room room)
        : base(room)
    {
      Briefing = new ChaserBriefing(this);

      StateMachine.Configure(GameRuleState.Waiting)
          .PermitIf(GameRuleStateTrigger.StartPrepare, GameRuleState.Preparing, CanStartGame);

      StateMachine.Configure(GameRuleState.Preparing)
          .Permit(GameRuleStateTrigger.StartGame, GameRuleState.FullGame);

      StateMachine.Configure(GameRuleState.FullGame)
          .SubstateOf(GameRuleState.Playing)
          .Permit(GameRuleStateTrigger.StartResult, GameRuleState.EnteringResult)
          .OnEntry(() => { NextChaser(); });

      StateMachine.Configure(GameRuleState.EnteringResult)
          .SubstateOf(GameRuleState.Playing)
          .Permit(GameRuleStateTrigger.StartResult, GameRuleState.Result);

      StateMachine.Configure(GameRuleState.Result)
          .SubstateOf(GameRuleState.Playing)
          .Permit(GameRuleStateTrigger.EndGame, GameRuleState.Waiting)
          .OnEntry(() =>
          {
            ChaserTarget = null;
            Chaser = null;
            Room.Broadcast(new SlaughterChangeSlaughterAckMessage(0));
          });
    }

    public override bool CountMatch => true;

    public override GameRule GameRule => GameRule.Chaser;

    public override Briefing Briefing { get; }

    public Player Chaser { get; private set; }

    public Player ChaserTarget { get; private set; }

    public readonly ConcurrentDictionary<Player, bool> PlayersAlive = new ConcurrentDictionary<Player, bool>();

    public IEnumerable<Player> PlayersHunted => PlayersAlive.Keys.Where(x => x != Chaser);

    public override void OnRoomJoinCompleted(Player plr)
    {
      if (ValidPlayer(Chaser))
      {
        plr?.SendAsync(new SlaughterChangeSlaughterAckMessage(Chaser.Account.Id,
            PlayersHunted.Select(x => x.Account.Id).ToArray()));
      }

      if (ValidPlayer(ChaserTarget))
      {
        plr?.SendAsync(new SlaughterChangeBonusTargetAckMessage(ChaserTarget.Account.Id));
      }
    }

    public override void OnIntrudeCompleted(Player plr)
    {
      plr.RoomInfo.State = PlayerState.Dead;
      // Future
      // Room.SetState(plr, PlayerState.Dead); --> GameEventMessage: 9 (StartGame)
      Room.Broadcast(new GameEventMessageAckMessage(GameEventMessage.StartGame, plr.Account.Id, 600, 600, ""));
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

    public bool ValidPlayer(Player plr)
    {
      if (plr == null)
        return false;

      if (plr.Room != Room)
        return false;

      if (!plr.RoomInfo.HasLoaded)
        return false;

      if (plr.RoomInfo.State != PlayerState.Alive)
        return false;

      return true;
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
          if (teamMgr.PlayersPlaying.Count() < PlayersNeededToStart && !Room.Options.IsFriendly)
            StateMachine.Fire(GameRuleStateTrigger.StartResult);

          if (RoundTime >= Room.Options.TimeLimit)
            StateMachine.Fire(GameRuleStateTrigger.StartResult);

          if (_waitingNextChaser)
          {
            _nextChaserTimer += delta.delta;
            if (_nextChaserTimer >= SNextChaserWaitTime)
              NextChaser();
          }
          else
          {
            _chaserTimer += delta.delta;
            if (_chaserTimer >= _chaserRoundTime)
            {
              var diff = Room.Options.TimeLimit - RoundTime;
              if (diff >= _chaserRoundTime + SNextChaserWaitTime)
                ChaserLose();
            }
            else
            {
              if (!ValidPlayer(Chaser))
                ChaserLose();
            }

            if (!ValidPlayer(ChaserTarget) || Chaser == ChaserTarget)
              NextTarget();

            if (_chaserTimer > s_spanTime && !ArePlayersAlive() &&
                Room.TeamManager.PlayersPlaying.Count() > 1 && !Room.Options.IsFriendly)
            {
              ChaserWin();
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
      return new ChaserPlayerRecord(plr);
    }

    private ChaserPlayerRecord GetRecord(Player plr)
    {
      if (plr.RoomInfo.Stats?.GetType() != typeof(ChaserPlayerRecord))
        GetPlayerRecord(plr);

      return (ChaserPlayerRecord)plr.RoomInfo.Stats;
    }

    public void OnScoreAttack(Player plr, float unk1, float unk2)
    {
      var stats = GetRecord(plr);
      stats.Unk17 = unk1;
      stats.Unk18 = unk2;
    }

    public override void OnScoreKill(Player killer, Player assist, Player target, AttackAttribute attackAttribute,
        LongPeerId scoreTarget, LongPeerId scoreKiller, LongPeerId scoreAssist)
    {
      if (!_waitingNextChaser)
      {
        base.OnScoreKill(killer, assist, target, attackAttribute, scoreTarget, scoreKiller, scoreAssist);

        if (!ScoreIsPlaying())
          return;

        if (scoreTarget.PeerId.Category == PlayerCategory.Player)
        {
          var stats = GetRecord(killer);
          stats.Kills++;

          PlayersAlive.TryRemove(target, out _);

          if (killer == Chaser && target == ChaserTarget)
            stats.BonusKills++;

          if (target != Chaser)
            target.RoomInfo.State = PlayerState.Dead;

          if (!ArePlayersAlive())
            ChaserWin();

          if (Chaser == target)
            ChaserLose();

          NextTarget();
        }
      }
    }

    public override void OnScoreSuicide(Player plr, LongPeerId scoreTarget, AttackAttribute icon)
    {
      if (!_waitingNextChaser)
      {
        if (scoreTarget.PeerId.Category == PlayerCategory.Player)
        {
          base.OnScoreSuicide(plr, scoreTarget, icon);

          if (!ScoreIsPlaying())
            return;

          PlayersAlive.TryRemove(plr, out _);

          if (Chaser == plr)
            ChaserLose();
          else
          {
            var stats = GetRecord(Chaser);
            stats.Kills++;

            if (plr == ChaserTarget)
              stats.BonusKills++;
          }

          if (!ArePlayersAlive())
            ChaserWin();

          if (plr != Chaser)
            plr.RoomInfo.State = PlayerState.Dead;

          NextTarget();
        }
      }
    }

    public void NextTarget()
    {
      if (PlayersAlive.Any(x => x.Key == ChaserTarget) && ValidPlayer(ChaserTarget) && ChaserTarget != Chaser)
        return;

      var players = PlayersHunted;
      if (players.Any())
      {
        var maxRatio = 0.0f;
        var targetCandidates = from plr in players
                               let stats = GetRecord(plr)
                               select new
                               {
                                 Player = plr,
                                 Ratio = stats.ChaserCount * 100.0f + stats.TotalScore
                               };

        foreach (var candidate in targetCandidates)
          maxRatio = candidate.Ratio > maxRatio ? maxRatio : candidate.Ratio;

        var newTargetFound = false;
        for (var trys = 0; trys < 10; trys++)
        {
          var index = _random.Next(0, (int)maxRatio);
          var candidate = targetCandidates.FirstOrDefault(plr => plr.Ratio <= index);
          if (candidate == null)
            continue;

          ChaserTarget = candidate.Player;
          newTargetFound = true;
          break;
        }

        if (!newTargetFound)
        {
          var index = _random.Next(0, targetCandidates.Count());
          ChaserTarget = targetCandidates.ElementAt(index).Player;
        }

        if (ChaserTarget != null)
        {
          Room.Broadcast(new SlaughterChangeBonusTargetAckMessage(ChaserTarget.Account.Id));
        }
      }
    }

    public void RoundEnd()
    {
      _waitingNextChaser = true;
      _nextChaserTimer = TimeSpan.Zero;

      var diff = Room.Options.TimeLimit - RoundTime;
      if (diff <= TimeSpan.FromSeconds(10)) // ToDo use const
        return;

      Room.Broadcast(new GameEventMessageAckMessage(GameEventMessage.ChaserIn,
          (ulong)SNextChaserWaitTime.TotalMilliseconds, 0, 0, ""));
    }

    public void NextChaser()
    {
      var diff = Room.Options.TimeLimit - RoundTime;
      if (diff <= TimeSpan.FromSeconds(10)) // ToDo use const
        return;

      _chaserRoundTime = Room.TeamManager.PlayersPlaying.Count() < 7
          ? TimeSpan.FromSeconds(60)
          : TimeSpan.FromSeconds(Room.TeamManager.PlayersPlaying.Count() * 10);
      _chaserRoundTime += TimeSpan.FromSeconds(Chaser != null ? 3.5 : 2);

      var count = Room.TeamManager.PlayersPlaying.Count();
      Chaser = null;

      for (var i = 0; i < count; i++)
      {
        if (!ValidPlayer(Chaser))
          Chaser = Room.TeamManager.PlayersPlaying.ElementAtOrDefault(_random.Next(0, count));
      }

      PlayersAlive.Clear();
      foreach (var plr in Room.TeamManager.PlayersPlaying)
      {
        plr.RoomInfo.State = PlayerState.Alive;
        if (plr != Chaser)
          plr.stats.Chaser.ChasedRounds++;

        PlayersAlive.TryAdd(plr, plr == Chaser);
      }

      GetRecord(Chaser).ChaserCount++;
      Chaser.stats.Chaser.ChaserRounds++;

      Room.Broadcast(new SlaughterChangeSlaughterAckMessage(
          Chaser.Account.Id,
          PlayersHunted.Select(plr => plr.Account.Id).ToArray()
      ));

      NextTarget();
      _waitingNextChaser = false;
      _chaserTimer = TimeSpan.Zero;
    }

    public void ChaserWin()
    {
      if (!ScoreIsPlaying())
        return;

      GetRecord(Chaser).Wins++;
      Chaser.stats.Chaser.ChaserWon++;

      foreach (var plr in Room.TeamManager.PlayersPlaying.Where(plr => plr != Chaser))
        plr.stats.Chaser.ChasedWon++;

      Room.Broadcast(new SlaughterSLRoundWinAckMessage());
      RoundEnd();
    }

    public void ChaserLose()
    {
      if (!ScoreIsPlaying())
        return;

      if (Chaser != null)
        Chaser.stats.Chaser.ChasedWon++;

      foreach (var plr in Room.TeamManager.PlayersPlaying.Where(plr => plr != Chaser))
      {
        GetRecord(plr).Survived++;
        plr.stats.Chaser.ChaserWon++;
        plr.Session.SendAsync(new SlaughterRoundWinAckMessage());
      }

      RoundEnd();
    }

    private bool CanStartGame()
    {
      if (!StateMachine.IsInState(GameRuleState.Waiting))
        return false;

      if (Room.Options.IsFriendly)
        return true;

      var teams = Room.TeamManager.Values.ToArray();

      if (Room.Players.Where(plr => plr.Value.RoomInfo.IsReady).ToArray().Length + 1 < PlayersNeededToStart)
        return false;

      return teams.All(team => team.Players.Any(plr => plr.RoomInfo.IsReady || Room.Master == plr));
    }

    private bool ArePlayersAlive()
    {
      if (PlayersAlive.Any(x => !x.Value && ValidPlayer(x.Key)))
        return true;

      return false;
    }
  }

  internal class ChaserBriefing : Briefing
  {
    public ChaserBriefing(GameRuleBase gameRule)
        : base(gameRule)
    {
      Unk7 = new List<int>();
      Unk8 = new List<ulong>();
      Unk9 = new List<ulong>();
    }

    public ulong CurrentChaser { get; set; }
    public ulong CurrentChaserTarget { get; set; }

    public int Unk3 { get; set; }
    public int Unk4 { get; set; }
    public int Unk5 { get; set; }
    public int Unk6 { get; set; }

    public IList<int> Unk7 { get; set; }
    public IList<ulong> Unk8 { get; set; }
    public IList<ulong> Unk9 { get; set; } //"victims"?

    protected override void WriteData(BinaryWriter w, bool isResult)
    {
      base.WriteData(w, isResult);
      var gameRule = (ChaserGameRule)GameRule;

      CurrentChaser = (gameRule.Chaser?.Account.Id ?? 0);
      CurrentChaserTarget = (gameRule.ChaserTarget?.Account.Id ?? 0);

      Unk8 = new List<ulong>();

      Unk8.Add(CurrentChaser);

      Unk9 = gameRule.PlayersAlive.Where(x => !x.Value).Select(x => x.Key.Account.Id).ToList();
      Unk6 = 1;

      w.Write(CurrentChaser);
      w.Write(CurrentChaserTarget);
      w.Write(Unk3);
      w.Write(Unk4);
      w.Write(Unk5);
      w.Write(Unk6);
      w.Write(Unk7.Count);
      w.Write(Unk7);
      w.Write(Unk8.Count);
      w.Write(Unk8);
      w.Write(Unk9.Count);
      w.Write(Unk9);
    }
  }

  internal class ChaserPlayerRecord : PlayerRecord
  {
    public ChaserPlayerRecord(Player plr)
        : base(plr)
    {
    }

    public override uint TotalScore => GetTotalScore();

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
    public uint Unk3 { get; set; }
    public uint Unk4 { get; set; }
    public uint BonusKills { get; set; }
    public uint Unk5 { get; set; } //Increases points
    public uint Unk6 { get; set; } //Increases points
    public uint Unk7 { get; set; } //Increases points and did at some point instanced a second chaser
    public uint Unk8 { get; set; } //Increases points
    public uint Wins { get; set; } //Wins
    public uint Survived { get; set; }
    public uint Unk9 { get; set; } //Increases points
    public uint Unk10 { get; set; } //Increases points 
    public uint ChaserCount { get; set; }
    public uint Unk11 { get; set; } //Increases points
    public uint Unk12 { get; set; }
    public uint Unk13 { get; set; }
    public uint Unk14 { get; set; }
    public uint Unk15 { get; set; }
    public uint Unk16 { get; set; }

    public float Unk17 { get; set; }
    public float Unk18 { get; set; }
    public float Unk19 { get; set; }
    public float Unk20 { get; set; }

    public byte Unk21 { get; set; }

    public override void Serialize(BinaryWriter w, bool isResult)
    {
      base.Serialize(w, isResult);

      w.Write(Unk1);
      w.Write(Unk2);
      w.Write(Unk3);
      w.Write(Unk4);
      w.Write(Kills);
      w.Write(BonusKills);
      w.Write(Unk5);
      w.Write(Unk6);
      w.Write(Unk7);
      w.Write(Unk8);
      w.Write(Wins);
      w.Write(Survived);
      w.Write(Unk9);
      w.Write(Unk10);
      w.Write(ChaserCount);
      w.Write(Unk11);
      w.Write(Unk12);
      w.Write(Unk13);
      w.Write(Unk14);
      w.Write(Unk15);
      w.Write(Unk16);

      w.Write(Unk17);
      w.Write(Unk18);
      w.Write(Unk19);
      w.Write(Unk20);

      w.Write(Unk21);
    }

    public override void Reset()
    {
      base.Reset();

      Unk1 = 0;
      Unk2 = 0;
      Unk3 = 0;
      Unk4 = 0;
      Kills = 0;
      BonusKills = 0;
      Unk5 = 0;
      Unk6 = 0;
      Unk7 = 0;
      Unk8 = 0;
      Wins = 0;
      Survived = 0;
      Unk9 = 0;
      Unk10 = 0;
      ChaserCount = 0;
      Unk11 = 0;
      Unk12 = 0;
      Unk13 = 0;
      Unk14 = 0;
      Unk15 = 0;
      Unk16 = 0;
      Unk17 = 0;
      Unk18 = 0;
      Unk19 = 0;
      Unk20 = 0;
      Unk21 = 0;
    }

    private uint GetTotalScore()
    {
      return Kills * 2 +
             BonusKills * 4 +
             Wins * 5 +
             Survived * 10
             + (uint)(Unk17 + Unk18);
    }
  }
}