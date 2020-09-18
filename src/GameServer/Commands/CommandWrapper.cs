using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NeoNetsphere.Network;

namespace NeoNetsphere.Commands
{
  internal class CommandWrapper : ICommand
  {
    public CommandWrapper()
    {
      Name = "/chg";
      AllowConsole = true;
      Permission = SecurityLevel.GameSage;
      SubCommands = new ICommand[0];
    }

    public string Name { get; }
    public bool AllowConsole { get; }
    public SecurityLevel Permission { get; }
    public IReadOnlyList<ICommand> SubCommands { get; }

    public async Task<bool> Execute(GameServer server, Player plr, string[] args)
    {
      if (args.Length < 1)
      {
        plr.SendConsoleMessage(S4Color.Red + "Wrong Usage, possible usages:");
        plr.SendConsoleMessage(S4Color.Red + "> /chg command");
        return true;
      }

      if (!await GameServer.Instance.CommandManager.Execute(plr, args))
      {
        if (plr == null)
          CommandManager.Logger.Information("Unknown command");
        else
          plr.SendConsoleMessage(S4Color.Red + "Command is not implemented.");
      }

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
  }
}