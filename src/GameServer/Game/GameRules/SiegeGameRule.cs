// ReSharper disable once Checknamespace 

using NeoNetsphere.Network;

namespace NeoNetsphere.Game.GameRules
{
  using System;
  using System.IO;
  using System.Linq;
  using NeoNetsphere;
  using NeoNetsphere.Network.Message.GameRule;

  internal class SiegeGameRule : GameRuleBase
  {
    private const uint PlayersNeededToStart = 1;

    public SiegeGameRule(Room room)
        : base(room)
    {
      Briefing = new Briefing(this);

      StateMachine.Configure(GameRuleState.Waiting)
          .PermitIf(GameRuleStateTrigger.StartPrepare, GameRuleState.Preparing, CanStartGame);

      StateMachine.Configure(GameRuleState.Preparing)
          .Permit(GameRuleStateTrigger.StartGame, GameRuleState.FirstHalf);

      StateMachine.Configure(GameRuleState.FirstHalf)
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
    public override GameRule GameRule => GameRule.Siege;
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
          if (teamMgr.PlayersPlaying.Count() < PlayersNeededToStart)
            StateMachine.Fire(GameRuleStateTrigger.StartResult);

          // Did we reach ScoreLimit?
          if (teamMgr.PlayersPlaying.Any(plr => plr.RoomInfo.Stats.TotalScore >= Room.Options.ScoreLimit))
            StateMachine.Fire(GameRuleStateTrigger.StartResult);

          // Did we reach round limit?
          var roundTimeLimit = TimeSpan.FromMilliseconds(Room.Options.TimeLimit.TotalMilliseconds);
          if (RoundTime >= roundTimeLimit)
            StateMachine.Fire(GameRuleStateTrigger.StartResult);
        }
      }
      catch (Exception e)
      {
        Room.Logger.Error(e.ToString());
      }
    }

    public override PlayerRecord GetPlayerRecord(Player plr)
    {
      return new SiegePlayerRecord(plr);
    }

    private static SiegePlayerRecord GetRecord(Player plr)
    {
      return (SiegePlayerRecord)plr.RoomInfo.Stats;
    }

    private bool CanStartGame()
    {
      if (!StateMachine.IsInState(GameRuleState.Waiting))
        return false;

      var countReady = Room.TeamManager.Values.Sum(team => team.Keys.Count(plr => plr.RoomInfo.IsReady));
      if (countReady < PlayersNeededToStart - 1) // Sum doesn't include master so decrease players needed by 1
        return false;
      return true;
    }
  }

  internal class SiegePlayerRecord : PlayerRecord
  {
    public SiegePlayerRecord(Player plr)
        : base(plr)
    {
    }

    public override uint TotalScore => GetTotalScore();

    public uint GetTotalScore()
    {
      return 0;
    }

    public override void Serialize(BinaryWriter w, bool isResult)
    {
      base.Serialize(w, isResult);

      //missing
    }
  }
}