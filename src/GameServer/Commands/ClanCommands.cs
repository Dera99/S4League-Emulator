using NeoNetsphere.Network.Message.Chat;

namespace NeoNetsphere.Commands
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;
  using Dapper.FastCrud;
  using ExpressMapper.Extensions;
  using NeoNetsphere.Database.Auth;
  using NeoNetsphere.Database.Game;
  using NeoNetsphere.Network;
  using NeoNetsphere.Network.Data.Club;
  using NeoNetsphere.Network.Message.Club;

  internal class ClanCommands : ICommand
  {
    public ClanCommands()
    {
      Name = "/clan";
      AllowConsole = false;
      Permission = SecurityLevel.User;
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
        if (plr.Account.SecurityLevel >= SecurityLevel.GameMaster)
        {
          plr.SendConsoleMessage(S4Color.Red + "Wrong Usage, possible usages:");
          plr.SendConsoleMessage(S4Color.Red + "> /clan forcejoin <username> <clan>");
          plr.SendConsoleMessage(S4Color.Red + "> /clan forcekick <username> <clan>");
          plr.SendConsoleMessage(S4Color.Red + "> /clan forcemaster <username> <clan>");
          plr.SendConsoleMessage(S4Color.Red + "> /clan invite <username>");
          plr.SendConsoleMessage(S4Color.Red + "> /clan kick <username>");
        }
        else
        {
          plr?.SendAsync(new MessageChatAckMessage(
              ChatType.Channel,
              plr.Account.Id,
              "ClanMgr",
              "/clan invite <username>"));
          plr?.SendAsync(new MessageChatAckMessage(
              ChatType.Channel,
              plr.Account.Id,
              "ClanMgr",
              "/clan kick <username>"));
        }
        return true;
      }

      var isclanservice = args[0].ToLower() == "kick" && args[0].ToLower() == "invite";

      if (args.Length < 2)
      {
        Array.Resize(ref args, 3);
        args[1] = "none";
        args[2] = "none";
      }

      var nickname = args[1].ToLower();

      var tmp = new string[args.Length - 2];
      Array.Copy(args, 2, tmp, 0, tmp.Length);

      var clanb = new StringBuilder();
      foreach (var text in tmp)
      {
        clanb.Append(" " + text);
      }

      var clan = clanb.ToString().Trim().ToLower();

      Club club;

      if (plr?.Account.SecurityLevel >= SecurityLevel.GameMaster && !isclanservice)
      {
        club = GameServer.Instance.ClubManager.FirstOrDefault(x => x.ClanName.ToLower() == clan);
        if (club == null)
        {
          plr.SendConsoleMessage(S4Color.Red + "Unknown clan " + clan);
          return true;
        }
      }
      else
      {
        club = plr?.Club;
        if (club == null)
        {
          plr?.SendAsync(new MessageChatAckMessage(
              ChatType.Channel,
              plr.Account.Id,
              "ClanMgr",
              "You are not inside a clan"));
          return true;
        }
      }

      var player = GameServer.Instance.PlayerManager
          .FirstOrDefault(x => x.Account?.Nickname?.ToLower() == nickname);
      AccountDto account;

      using (var db = AuthDatabase.Open())
      {
        account = (await DbUtil.FindAsync<AccountDto>(db, statement => statement
                .Include<BanDto>(join => join.LeftOuterJoin())
                .Where($"{nameof(AccountDto.Nickname):C} = @Nickname")
                .WithParameters(new { Nickname = nickname }))).FirstOrDefault();

        if (account == null)
        {
          if (player == null)
          {
            plr.SendConsoleMessage(S4Color.Red + "Unknown player");
            return true;
          }

          account = player.Account.AccountDto;
        }
      }

      switch (args[0].ToLower())
      {
        case "kick":
          {
            var message = "You cannot kick a player";
            var plrrank = plr.Club.GetPlayer(plr.Account.Id).Rank;
            if (plr.Account.Id == (ulong)account.Id)
            {
              message = "You cannot kick yourself";
            }
            else if (plrrank <= ClubRank.Staff)
            {
              if (club.GetPlayer((ulong)account.Id).Rank < plrrank)
              {
                message = "You cannot kick a player with a higher rank";
              }
              else
              {
                if (player != null)
                  Club.LogOff(player);
                await club.RemovePlayer((ulong)account.Id);
                message = "Kicked player from clan";
              }
            }

            await plr.SendAsync(new MessageChatAckMessage(
                ChatType.Channel,
                plr.Account.Id,
                "ClanMgr",
                message));
            return true;
          }

        case "invite":
          {
            var message = "You cannot invite a player";
            if (plr.Club.GetPlayer(plr.Account.Id).Rank <= ClubRank.Staff)
            {
              if (player != null)
              {
                if (club.Players.ContainsKey(player.Account.Id))
                {
                  message = "Player is already in your clan";
                }
                else
                {
                  plr.Club.SendInvite(plr, player);
                  message = "Player has been invited";
                }
              }
              else
              {
                message = "Player is not online";
              }
            }

            await plr.SendAsync(new MessageChatAckMessage(
                ChatType.Channel,
                plr.Account.Id,
                "ClanMgr",
                message));
            return true;
          }

        case "forcejoin":
          {
            if (plr.Account.SecurityLevel <= SecurityLevel.GameMaster)
              goto default;

            if (GameServer.Instance.ClubManager.Any(x => x.Players.ContainsKey((ulong)account.Id)))
            {
              plr.SendConsoleMessage(S4Color.Red + "Player is already in a clan");
              return true;
            }

            if (await club.AddPlayer((ulong)account.Id))
            {
              plr?.SendConsoleMessage(S4Color.Green +
                                      $"Added player {account.Nickname} to clan {club.ClanName}");
            }

            return true;
          }

        case "forcekick":
          {
            if (plr.Account.SecurityLevel <= SecurityLevel.GameMaster)
              goto default;

            if (!GameServer.Instance.ClubManager.Any(x => x.Players.ContainsKey((ulong)account.Id)))
            {
              plr.SendConsoleMessage(S4Color.Red + "Player is not in a clan");
              return true;
            }

            if (!club.Players.ContainsKey((ulong)account.Id))
            {
              plr.SendConsoleMessage(S4Color.Red + "Player is in another clan");
              return true;
            }

            if (await club.RemovePlayer((ulong)account.Id))
            {
              plr?.SendConsoleMessage(S4Color.Green +
                                      $"Removed player {account.Nickname} from clan {club.ClanName}");
            }

            return true;
          }

        case "removestaff":
          {
            if (plr.Account.SecurityLevel <= SecurityLevel.GameMaster)
              goto default;

            if (!GameServer.Instance.ClubManager.Any(x => x.Players.ContainsKey((ulong)account.Id)))
            {
              plr.SendConsoleMessage(S4Color.Red + "Player is not in a clan");
              return true;
            }

            if (!club.Players.ContainsKey((ulong)account.Id))
            {
              plr.SendConsoleMessage(S4Color.Red + "Player is in another clan");
              return true;
            }

            if (await club.ChangeStaffStatus((ulong)account.Id, false))
            {
              plr?.SendConsoleMessage(S4Color.Green +
                                      $"Player {account.Nickname} is now Regular in {club.ClanName}");
            }

            return true;
          }

        case "setstaff":
          {
            if (plr.Account.SecurityLevel <= SecurityLevel.GameMaster)
              goto default;

            if (!GameServer.Instance.ClubManager.Any(x => x.Players.ContainsKey((ulong)account.Id)))
            {
              plr.SendConsoleMessage(S4Color.Red + "Player is not in a clan");
              return true;
            }

            if (!club.Players.ContainsKey((ulong)account.Id))
            {
              plr.SendConsoleMessage(S4Color.Red + "Player is in another clan");
              return true;
            }

            if (await club.ChangeStaffStatus((ulong)account.Id, true))
            {
              plr?.SendConsoleMessage(S4Color.Green +
                                      $"Player {account.Nickname} is now staff in {club.ClanName}");
            }

            return true;
          }

        case "forcemaster":
          {
            if (plr.Account.SecurityLevel <= SecurityLevel.GameMaster)
              goto default;

            if (!GameServer.Instance.ClubManager.Any(x => x.Players.ContainsKey((ulong)account.Id)))
            {
              plr.SendConsoleMessage(S4Color.Red + "Player is not in a clan");
              return true;
            }

            if (!club.Players.ContainsKey((ulong)account.Id))
            {
              plr.SendConsoleMessage(S4Color.Red + "Player is in another clan");
              return true;
            }

            if (await club.ForceChangeMaster((ulong)account.Id))
            {
              plr?.SendConsoleMessage(S4Color.Green +
                                      $"Changed Master from clan {club.ClanName} to player {account.Nickname}");
            }

            return true;
          }

        default:
          {
            if (plr.Account.SecurityLevel >= SecurityLevel.GameMaster && !isclanservice)
            {
              plr.SendConsoleMessage(S4Color.Red + "Wrong Usage, possible usages:");
              plr.SendConsoleMessage(S4Color.Red + "> /clan forcejoin <username> <clan>");
              plr.SendConsoleMessage(S4Color.Red + "> /clan forcekick <username> <clan>");
              plr.SendConsoleMessage(S4Color.Red + "> /clan forcemaster <username> <clan>");
              plr.SendConsoleMessage(S4Color.Red + "> /clan invite <username>");
              plr.SendConsoleMessage(S4Color.Red + "> /clan kick <username>");
            }
            else
            {
              plr?.SendAsync(new MessageChatAckMessage(
                  ChatType.Channel,
                  plr.Account.Id,
                  "ClanMgr",
                  "/clan invite <username>"));
              plr?.SendAsync(new MessageChatAckMessage(
                  ChatType.Channel,
                  plr.Account.Id,
                  "ClanMgr",
                  "/clan kick <username>"));
            }

            return true;
          }
      }
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