using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NeoNetsphere.Network;

namespace NeoNetsphere.Commands
{
  internal class ServerCommand : ICommand
  {
    public ServerCommand()
    {
      Name = "server";
      AllowConsole = true;
      Permission = SecurityLevel.GameMaster;
      SubCommands = new ICommand[] { new StatusCommand(), new VersionCommand() };
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

    private class VersionCommand : ICommand
    {
      public VersionCommand()
      {
        Name = "version";
        AllowConsole = true;
        Permission = SecurityLevel.GameMaster;
        SubCommands = new ICommand[] { };
      }

      public string Name { get; }
      public bool AllowConsole { get; }
      public SecurityLevel Permission { get; }
      public IReadOnlyList<ICommand> SubCommands { get; }

      public async Task<bool> Execute(GameServer server, Player plr, string[] args)
      {
        var globalVersion = Program.GlobalVersion;
        var message =
            $"Version: {globalVersion.Major}.{(globalVersion.Major / 2) + globalVersion.Minor + globalVersion.Build + globalVersion.Revision}";

        if (plr == null)
        {
          CommandManager.Logger.Information(message);
        }
        else
        {
          plr.SendConsoleMessage(S4Color.Green + message);
        }

        return true;
      }

      public string Help()
      {
        return Name;
      }
    }

    private class StatusCommand : ICommand
    {
      public StatusCommand()
      {
        Name = "status";
        AllowConsole = true;
        Permission = SecurityLevel.GameMaster;
        SubCommands = new ICommand[] { };
      }

      public string Name { get; }
      public bool AllowConsole { get; }
      public SecurityLevel Permission { get; }
      public IReadOnlyList<ICommand> SubCommands { get; }

      public async Task<bool> Execute(GameServer server, Player plr, string[] args)
      {
        // Todo peak
        var ts = DateTime.Now - Process.GetCurrentProcess().StartTime;
        var uptime = new StringBuilder();
        if (ts.Days > 0)
          uptime.AppendFormat("{0} days ", ts.Days);
        if (ts.Hours > 0)
          uptime.AppendFormat("{0} hours ", ts.Hours);
        if (ts.Minutes > 0)
          uptime.AppendFormat("{0} minutes ", ts.Minutes);
        if (ts.Seconds > 0)
          uptime.AppendFormat("{0} seconds ", ts.Seconds);

        var message = $"Uptime: {uptime} " +
                      $"Online: {server.Sessions.Values.Where(c => { var a = (GameSession)c; return a.IsLoggedIn(); }).ToList().Count} " +
                      $"WorkerDelta(~): {Math.Round(server.AverageWorkerDelta.Average())}/~100 ms (cur. max: ~{Math.Round(server.HighWorkerDelta.Keys.Average())} ms) ";

        if (plr == null)
        {
          CommandManager.Logger.Information(message);
        }
        else
        {
          plr.SendConsoleMessage(S4Color.Green + message);
        }

        return true;
      }

      public string Help()
      {
        return Name;
      }
    }
  }
}