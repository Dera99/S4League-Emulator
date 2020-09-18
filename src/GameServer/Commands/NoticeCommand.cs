using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NeoNetsphere.Network;

namespace NeoNetsphere.Commands
{
  internal class NoticeCommand : ICommand
  {
    public NoticeCommand()
    {
      Name = "/notice";
      AllowConsole = false;
      Permission = SecurityLevel.GameMaster;
      SubCommands = new ICommand[] { };
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
        plr.SendConsoleMessage(S4Color.Red + "> /notice message");
        return true;
      }

      var notice = new StringBuilder();
      foreach (var x in args.ToList())
        notice.Append(" " + x);

      if (plr.Room != null)
        plr.Room.BroadcastNotice(notice.ToString());
      else if (plr.Channel != null) plr.Channel.BroadcastNotice(notice.ToString());
      return true;
    }

    public string Help()
    {
      var sb = new StringBuilder();
      sb.AppendLine(Name);
      foreach (var cmd in SubCommands)
      {
        sb.Append(" ");
        sb.AppendLine(cmd.Help());
      }

      return sb.ToString();
    }
  }

  internal class WholeNoticeCommand : ICommand
  {
    public WholeNoticeCommand()
    {
      Name = "/whole_notice";
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
      if (args.Length < 1)
      {
        plr.SendConsoleMessage(S4Color.Red + "Wrong Usage, possible usages:");
        plr.SendConsoleMessage(S4Color.Red + "> /whole_notice message");
        return true;
      }

      var notice = new StringBuilder();
      foreach (var x in args.ToList())
        notice.Append(" " + x);

      server.BroadcastNotice(notice.ToString().Replace("/whole_notice", ""));
      return true;
    }

    public string Help()
    {
      var sb = new StringBuilder();
      sb.AppendLine(Name);
      foreach (var cmd in SubCommands)
      {
        sb.Append(" ");
        sb.AppendLine(cmd.Help());
      }

      return sb.ToString();
    }
  }
}