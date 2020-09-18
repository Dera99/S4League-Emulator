using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper.FastCrud;
using NeoNetsphere.Database.Auth;
using NeoNetsphere.Network;

namespace NeoNetsphere.Commands
{
  internal class SearchCommand : ICommand
  {
    public SearchCommand()
    {
      Name = "/search";
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
        plr.SendConsoleMessage(S4Color.Red + "> /search <username>");
        return true;
      }

      var nickname = args[0];
      using (var db = AuthDatabase.Open())
      {
        var account = (await DbUtil.FindAsync<AccountDto>(db, statement => statement
                .Include<BanDto>(join => join.LeftOuterJoin())
                .Where($"{nameof(AccountDto.Nickname):C} = @Nickname")
                .WithParameters(new { Nickname = nickname }))
            ).FirstOrDefault();

        if (account == null)
        {
          plr.SendConsoleMessage(S4Color.Red + "Unknown player");
          return true;
        }

        var player = GameServer.Instance.PlayerManager.Get((ulong)account.Id);
        if (player == null)
        {
          plr.SendConsoleMessage(S4Color.Red + "Player is not online");
          return true;
        }

        if (player.Channel?.Id > 0)
        {
          if (player.Room != null)
          {
            plr.SendConsoleMessage(
                $"\"{player.Account.Nickname}\"is connecting to the room \"{player.Room.Id}\"in channel \"{player.Channel.Id}\"now");
          }
          else
          {
            plr.SendConsoleMessage(
                $"\"{player.Account.Nickname}\"is waiting in channel {player.Channel.Id} now");
          }
        }
        else
        {
          plr.SendConsoleMessage(
              $"\"{player.Account.Nickname}\"is waiting in server \"{Config.Instance.Name}\"now");
        }
      }

      return true;
    }

    public string Help()
    {
      return new UserkickCommand().Help();
    }
  }
}