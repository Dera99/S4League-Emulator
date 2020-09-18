// ReSharper disable once Checknamespace 

using System.Runtime.InteropServices;
using NeoNetsphere.Network;

namespace NeoNetsphere.Game.GameRules //placeholder for real practice, c&p of deathmatch&br
{
  using System;
  using System.IO;
  using System.Linq;
  using NeoNetsphere;
  using NeoNetsphere.Network.Data.GameRule;
  using NeoNetsphere.Network.Message.GameRule;

  internal class PracticeGameRule : GameRuleBase
  {
    private const uint PlayersNeededToStart = 1;
    public uint KillCount;

    public PracticeGameRule(Room room)
        : base(room)
    {
      Briefing = new PracticeBriefing(this);

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
    public override GameRule GameRule => GameRule.Practice;
    public override Briefing Briefing { get; }

    public override void Initialize()
    {
      var playerLimit = (uint)Room.Options.PlayerLimit;

      Room.TeamManager.Add(Team.Alpha, playerLimit, 0);
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
      return new PracticePlayerRecord(plr);
    }

    private static PracticePlayerRecord GetRecord(Player plr)
    {
      return (PracticePlayerRecord)plr.RoomInfo.Stats;
    }

    private bool CanPrepareGame()
    {
      return true;
    }

    private bool CanStartGame()
    {
      return true;
    }
  }

  internal class PracticeBriefing : Briefing
  {
    public PracticeBriefing(GameRuleBase gameRule)
        : base(gameRule)
    {
    }

    public uint Kills => ((PracticeGameRule)GameRule).KillCount;

    protected override void WriteData(BinaryWriter w, bool isResult)
    {
      base.WriteData(w, isResult);

      w.Write(Kills);
      w.Write(0);

      w.Write((byte)0);
      w.Write((byte)0);
      w.Write((byte)0);

      var gamerule = (PracticeGameRule)GameRule;
    }
  }

  internal class PracticePlayerRecord : PlayerRecord
  {
    public PracticePlayerRecord(Player plr)
        : base(plr)
    {
    }

    public override uint TotalScore
    {
      get
      {
        if (Player.Room != null && Player.Room.GameRuleManager.GameRule.GameRule == GameRule.Practice)
          return ((PracticeGameRule)Player.Room?.GameRuleManager.GameRule).KillCount;
        return 0;
      }
    }

    public override void Serialize(BinaryWriter w, bool isResult)
    {
      base.Serialize(w, isResult);
      w.Write(TotalScore);
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