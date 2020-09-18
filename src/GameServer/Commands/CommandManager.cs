namespace NeoNetsphere.Commands
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using NeoNetsphere.Network;
  using Serilog;
  using Serilog.Core;

  internal class CommandManager
  {
    // ReSharper disable once InconsistentNaming
    public static readonly ILogger Logger =
        Log.ForContext(Constants.SourceContextPropertyName, nameof(CommandManager));

    private readonly IList<ICommand> _commands = new List<ICommand>();

    public CommandManager(GameServer server)
    {
      Server = server;
    }

    public GameServer Server { get; }

    public CommandManager Add(ICommand cmd)
    {
      if (_commands.Any(c => c.Name.Equals(cmd.Name, StringComparison.InvariantCultureIgnoreCase)))
        throw new Exception("Command " + cmd.Name + "already exists");

      _commands.Add(cmd);
      return this;
    }

    public async Task<bool> Execute(Player plr, string[] args)
    {
      return await ExecuteCommand(plr, _commands, args);
    }

    private async Task<bool> ExecuteCommand(Player plr, IEnumerable<ICommand> cmds, string[] args)
    {
      if (args.Length == 0)
        return false;

      var isConsole = plr == null;
      var cmd = cmds.FirstOrDefault(c => c.Name.Equals(args[0], StringComparison.InvariantCultureIgnoreCase));
      if (cmd == null)
        return false;

      var tmp = new string[args.Length - 1];
      Array.Copy(args, 1, tmp, 0, tmp.Length);

      if (isConsole && !cmd.AllowConsole)
        return false;

      if (!isConsole && plr.Account.SecurityLevel < cmd.Permission)
      {
        Logger.ForAccount(plr).Error("Access denied for command {cmdName} - args: {args}", cmd.Name,
            string.Join(",", args));
        plr.SendConsoleMessage(S4Color.Red + "You don't have a right");
        return false;
      }

      if (plr != null)
      {
        Logger.ForAccount(plr).Warning("Command: {cmdName} - args: {args}",
            cmd.Name, string.Join(",", args));
      }

      if (cmd.SubCommands.Count == 0)
      {
        if (await cmd.Execute(Server, plr, tmp))
          return true;

        if (plr == null)
          Logger.Information(cmd.Help());
        else
          plr.SendConsoleMessage(S4Color.Red + cmd.Help());

        return true;
      }

      if (cmd.SubCommands.Count > 0 && args.Length < 2)
      {
        if (plr == null)
          Logger.Information(cmd.Help());
        else
          plr.SendConsoleMessage(S4Color.Red + cmd.Help());
        return true;
      }

      return await ExecuteCommand(plr, cmd.SubCommands, tmp);
    }
  }
}