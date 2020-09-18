// ReSharper disable once Checknamespace 

using NeoNetsphere.Network;

namespace NeoNetsphere.Game.GameRules //placeholder for real Conquest, c&p of deathmatch&br
{
  using System;
  using System.IO;
  using System.Linq;
  using NeoNetsphere;
  using NeoNetsphere.Network.Data.GameRule;
  using NeoNetsphere.Network.Message.GameRule;

  internal class ConquestGameRule : GameRuleBase
  {
    public uint DropCount = 0;

    public ConquestGameRule(Room room)
        : base(room)
    {
      Briefing = new ConquestBriefing(this);

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

    public override bool CountMatch => false;
    public override GameRule GameRule => GameRule.Horde;
    public override Briefing Briefing { get; }

    public override void Initialize()
    {
      var playerLimit = (uint)Room.Options.PlayerLimit;
      var spectatorLimit = (uint)0;

      Room.TeamManager.Add(Team.Alpha, playerLimit, spectatorLimit);
      Room.TeamManager.Add(Team.Beta, 0, 0);
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
      return new ConquestPlayerRecord(plr);
    }

    private static ConquestPlayerRecord GetRecord(Player plr)
    {
      return (ConquestPlayerRecord)plr.RoomInfo.Stats;
    }

    private bool CanStartGame()
    {
      //if (Room.TeamManager.Players.ToList().Count > 1)
      //    return false;
      return true;
    }
  }

  internal class ConquestBriefing : Briefing
  {
    public ConquestBriefing(GameRuleBase gameRule)
        : base(gameRule)
    {
    }

    protected override void WriteData(BinaryWriter w, bool isResult)
    {
      base.WriteData(w, isResult);
    }
  }

  internal class ConquestPlayerRecord : PlayerRecord
  {
    public ConquestPlayerRecord(Player plr)
        : base(plr)
    {
    }

    public override uint TotalScore => GetTotalScore();

    public override void Serialize(BinaryWriter w, bool isResult)
    {
      base.Serialize(w, isResult);
      w.Write(0);
      w.Write(0);
      w.Write(0);
      w.Write(0);
      w.Write(0);
      w.Write(0);
      w.Write(0);
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