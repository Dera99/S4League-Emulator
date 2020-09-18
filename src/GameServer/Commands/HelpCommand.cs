using NeoNetsphere;
using NeoNetsphere.Commands;
using NeoNetsphere.Network;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

internal class HelpCommand : ICommand
{
    public string Name { get; }
    public bool AllowConsole { get; }
    public SecurityLevel Permission { get; }
    public IReadOnlyList<ICommand> SubCommands { get; }

    // Todo
    public HelpCommand()
    {
        Name = "/help";
        AllowConsole = false;
        Permission = SecurityLevel.GameMaster;
        SubCommands = new ICommand[] { };
    }

    public async Task<bool> Execute(GameServer server, Player plr, string[] args)
    {
        string item = "Lazy to add the commands xD";
        plr.SendConsoleMessage(S4Color.Green + item);
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
