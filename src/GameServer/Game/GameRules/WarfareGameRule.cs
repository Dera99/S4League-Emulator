// ReSharper disable once Checknamespace 

using NeoNetsphere.Network;

namespace NeoNetsphere.Game.GameRules //placeholder for real Warfare, c&p of deathmatch&br
{
  using System;
  using System.IO;
  using NeoNetsphere;
  using NeoNetsphere.Network.Data.GameRule;
  using NeoNetsphere.Network.Message.GameRule;

  internal class WarfareGameRule : GameRuleBase
  {
    private const uint PlayersNeededToStart = 1;

    public WarfareGameRule(Room room)
        : base(room)
    {
      Briefing = new WarfareBriefing(this);

      StateMachine.Configure(GameRuleState.Waiting)
          .PermitIf(GameRuleStateTrigger.StartPrepare, GameRuleState.Preparing, CanStartGame);

      StateMachine.Configure(GameRuleState.Preparing)
          .Permit(GameRuleStateTrigger.StartGame, GameRuleState.FullGame);

      StateMachine.Configure(GameRuleState.FullGame)
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
    public override GameRule GameRule => GameRule.Warfare;
    public override Briefing Briefing { get; }

    public override void Initialize()
    {
      var teamMgr = Room.TeamManager;
      teamMgr.Add(Team.Alpha, 1, 0);

      base.Initialize();
    }

    public override void Cleanup()
    {
      var teamMgr = Room.TeamManager;
      teamMgr.Remove(Team.Alpha);

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
      return new WarfarePlayerRecord(plr);
    }

    private bool CanStartGame()
    {
      return true;
    }
  }

  internal class WarfareBriefing : Briefing
  {
    public WarfareBriefing(GameRuleBase gameRule)
        : base(gameRule)
    {
    }

    protected override void WriteData(BinaryWriter w, bool isResult)
    {
      base.WriteData(w, isResult);

      var gamerule = (WarfareGameRule)GameRule;
    }
  }

  internal class WarfarePlayerRecord : PlayerRecord
  {
    public WarfarePlayerRecord(Player plr)
        : base(plr)
    {
    }

    public override uint TotalScore => GetTotalScore();

    public override void Serialize(BinaryWriter w, bool isResult)
    {
      //base.Serialize(w, isResult);
    }

    public override void Reset()
    {
      base.Reset();
    }

    private uint GetTotalScore()
    {
      return 0;
    }

    public override int GetExpGain(out int bonusExp)
    {
      bonusExp = 0;
      return 0;
    }
  }
}