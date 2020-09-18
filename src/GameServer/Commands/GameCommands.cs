using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NeoNetsphere.Network;
using NeoNetsphere.Network.Message.Game;


namespace NeoNetsphere.Commands
{
  internal class GameCommands : ICommand
  {
    public GameCommands()
    {
      Name = "game";
      AllowConsole = false;
      Permission = SecurityLevel.Developer;
      SubCommands = new ICommand[] { new StateCommand(), new TimeCommand() };
    }

    public string Name { get; }
    public bool AllowConsole { get; }
    public SecurityLevel Permission { get; }
    public IReadOnlyList<ICommand> SubCommands { get; }

    public async Task<bool> Execute(GameServer server, Player plr, string[] args)
    {
      return true;
    }

    public string Help()
    {
      var sb = new StringBuilder();
      sb.AppendLine(Name);
      foreach (var cmd in SubCommands)
      {
        sb.Append("");
        sb.AppendLine(cmd.Help());
      }

      return sb.ToString();
    }

    private class TimeCommand : ICommand
    {
      public TimeCommand()
      {
        Name = "time";
        AllowConsole = false;
        Permission = SecurityLevel.Developer;
        SubCommands = new ICommand[0];
      }

      public string Name { get; }
      public bool AllowConsole { get; }
      public SecurityLevel Permission { get; }
      public IReadOnlyList<ICommand> SubCommands { get; }

      public async Task<bool> Execute(GameServer server, Player plr, string[] args)
      {
        if (plr.Room == null)
        {
          plr.SendConsoleMessage(S4Color.Red + "You're not inside a room");
          return true;
        }

        if (plr.Room.GameRuleState == GameRuleState.FirstHalf ||
            plr.Room.GameRuleState == GameRuleState.SecondHalf)
        {
          plr.SendConsoleMessage($"Current Time: {plr.Room.RoundTime}/{plr.Room.Options.TimeLimit / 2}");
        }
        else
        {
          plr.SendConsoleMessage($"Current Time: {plr.Room.RoundTime}/{plr.Room.Options.TimeLimit}");
        }

        return true;
      }

      public string Help()
      {
        return Name + "[trigger]";
      }
    }

    private class StateCommand : ICommand
    {
      public StateCommand()
      {
        Name = "state";
        AllowConsole = false;
        Permission = SecurityLevel.Developer;
        SubCommands = new ICommand[0];
      }

      public string Name { get; }
      public bool AllowConsole { get; }
      public SecurityLevel Permission { get; }
      public IReadOnlyList<ICommand> SubCommands { get; }

      public async Task<bool> Execute(GameServer server, Player plr, string[] args)
      {
        if (plr.Room == null)
        {
          plr.SendConsoleMessage(S4Color.Red + "You're not inside a room");
          return true;
        }

        var stateMachine = plr.Room.GameRuleManager.GameRule.StateMachine;
        if (args.Length == 0)
        {
          plr.SendConsoleMessage($"Current state: {plr.Room.GameRuleState}");
        }
        else
        {
          if (!Enum.TryParse(args[0], out GameRuleStateTrigger trigger))
          {
            plr.SendConsoleMessage(
                $"{S4Color.Red}Invalid trigger! Available triggers: {string.Join(", ", stateMachine.PermittedTriggers)}");
          }
          else
          {
            if (!stateMachine.CanFire(trigger))
            {
              plr.SendConsoleMessage($"{S4Color.Red}This state cant be triggered now");
              return true;
            }

            stateMachine.Fire(trigger);
            plr.Room.Broadcast(
                new NoticeAdminMessageAckMessage(
                    $"Current game state has been changed by {plr.Account.Nickname}"));
          }
        }

        return true;
      }

      public string Help()
      {
        return Name + "[trigger]";
      }
    }
  }
}