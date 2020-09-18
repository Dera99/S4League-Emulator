using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BlubLib.Threading.Tasks;
using Dapper.FastCrud;
using NeoNetsphere.Database.Auth;
using NeoNetsphere.Network;
using NeoNetsphere.Network.Message.Game;

namespace NeoNetsphere.Commands
{
  internal class AllkickCommand : ICommand
  {
    public AllkickCommand()
    {
      Name = "/allkick";
      AllowConsole = true;
      Permission = SecurityLevel.GameMaster;
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
        plr.SendConsoleMessage(S4Color.Red + "> /allkick room");
        plr.SendConsoleMessage(S4Color.Red + "> /allkick server");
        return true;
      }

      switch (args[0])
      {
        case "room":
          {
            if (plr?.Room != null)
            {
              var count = plr.Room.Players.Count() - 1;
              foreach (var roomplayer in plr.Room.Players.Values)
              {
                if (roomplayer == plr)
                  continue;

                plr.Room.Leave(roomplayer);
              }

              plr.SendConsoleMessage($"\"{count}\"players have been kicked forcefully out of room");
            }
            else
            {
              plr.SendConsoleMessage(S4Color.Red + "You are not in a room");
            }

            break;
          }

        case "server":
          {
            if (plr.Account.SecurityLevel <= SecurityLevel.GameMaster)
            {
              plr.SendConsoleMessage(S4Color.Red + "You don't have a right");
              return true;
            }

            plr.SendConsoleMessage($"Please wait..");

            foreach (var session in GameServer.Instance.Sessions.Values.Cast<GameSession>())
            {
              if (session.Player == plr)
                continue;
              session.Player?.Room?.Leave(session.Player);
            }

            await Task.Delay(1000);

            foreach (var session in GameServer.Instance.Sessions.Values.Cast<GameSession>())
            {
              if (session.Player == plr)
                continue;

              session.SendAsync(new ItemUseChangeNickAckMessage { Result = 0 });
              session.SendAsync(new ServerResultAckMessage(ServerResult.CreateNicknameSuccess));
            }

            await Task.Delay(1000);

            var count = 0;
            foreach (var session in GameServer.Instance.Sessions.Values.Cast<GameSession>())
            {
              if (session.Player == plr)
                continue;

              count++;
              session.CloseAsync();
            }

            plr.SendConsoleMessage($"\"{count}\"players have been kicked forcefully");
            break;
          }
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

  internal class RoomkickCommand : ICommand
  {
    public RoomkickCommand()
    {
      Name = "/roomkick";
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
        plr.SendConsoleMessage(S4Color.Red + "> /roomkick <username>");
        return true;
      }

      var nickname = args[0];

      var targetplr = GameServer.Instance.PlayerManager.Get(nickname);
      if (targetplr?.Room == null)
      {
        if (targetplr == null)
          plr.SendConsoleMessage(S4Color.Red + "Unknown Player");
        else
          plr.SendConsoleMessage(S4Color.Red + "Player is not in a Room");
        return true;
      }

      targetplr.Room.Leave(targetplr, RoomLeaveReason.ModeratorKick);
      plr.SendConsoleMessage(S4Color.Green +
                             $"Player {targetplr.Account.Nickname} has been kicked forcefully out of room");
      return true;
    }

    public string Help()
    {
      return new UserkickCommand().Help();
    }
  }

  internal class UserkickCommand : ICommand
  {
    public UserkickCommand()
    {
      Name = "/userkick";
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
        plr.SendConsoleMessage(S4Color.Red + "> /userkick <username>");
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
          if (plr != null)
            plr.SendConsoleMessage(S4Color.Red + "Unknown player");
          else
            CommandManager.Logger.Information("Unknown player");
          return true;
        }

        var targetplr = GameServer.Instance.PlayerManager.Get((ulong)account.Id);
        if (targetplr == null)
        {
          if (plr != null)
            plr.SendConsoleMessage(S4Color.Red + "Player is not online");
          else
            CommandManager.Logger.Information("Unknown player");
          return true;
        }

        targetplr.Disconnect();
        if (plr != null)
          plr.SendConsoleMessage(S4Color.Green + $"Player {account.Nickname} has been kicked forcefully");
        else
          CommandManager.Logger.Information("Unknown player");
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

  internal class KickCommand : ICommand
  {
    public KickCommand()
    {
      Name = "/kick";
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
        plr.SendConsoleMessage(S4Color.Red + "> /userkick <username>");
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
          if (plr != null)
            plr.SendConsoleMessage(S4Color.Red + "Unknown player");
          else
            CommandManager.Logger.Information("Unknown player");
          return true;
        }

        var targetplr = GameServer.Instance.PlayerManager.Get((ulong)account.Id);
        if (targetplr == null)
        {
          if (plr != null)
            plr.SendConsoleMessage(S4Color.Red + "Player is not online");
          else
            CommandManager.Logger.Information("Unknown player");
          return true;
        }

        targetplr.Disconnect();
        if (plr != null)
          plr.SendConsoleMessage(S4Color.Green + $"Player {account.Nickname} has been kicked forcefully");
        else
          CommandManager.Logger.Information("Unknown player");
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