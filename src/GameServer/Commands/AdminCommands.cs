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
    using NeoNetsphere.Network.Data.Game;
    using NeoNetsphere.Network.Message.Game;

    internal class AdminCommands : ICommand
    {
        public AdminCommands()
        {
            Name = "admin_cmd";
            AllowConsole = true;
            Permission = SecurityLevel.GameMaster;
            SubCommands = new ICommand[]
            {
                new RenameCommand(), new SecurityCommand(), new LevelCommand(), new GetIDCommand(),
                new KillRoomCommand(), new SetMasterCommand(), new AddAPCommand(), new AddItemCommand(),  new AddPenCommand()
            };
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

        private class AddItemCommand : ICommand
        {
            public AddItemCommand()
            {
                Name = "additem";
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
                if (args.Length < 3)
                {
                    plr.SendConsoleMessage(S4Color.Red + "Wrong Usage, possible usages:");
                    plr.SendConsoleMessage(S4Color.Red + "> additem <nickname> <itemId> <color>");
                    return true;
                }

                if (args.Length >= 2)
                {
                    var player = GameServer.Instance.PlayerManager.Get(args[0]);
                    if (player == null)
                    {
                        plr.SendConsoleMessage(S4Color.Red + "Unknown player");
                        return false;
                    }

                    int.TryParse(args[1], out var item);
                    byte.TryParse(args[2], out var color);
                    PlayerItem resultitem = null;
                    try
                    {
                        resultitem = player.Inventory.Create(item, 0, color, new EffectNumber[0], 1);
                    }
                    catch (Exception)
                    { }

                    if (resultitem == null)
                    {
                        plr.SendConsoleMessage(S4Color.Red + "Invalid item");
                        return false;
                    }

                    plr.SendConsoleMessage($"Added {item}:{color} to {player.Account.Nickname}");
                }

                return true;
            }

            public string Help()
            {
                return Name;
            }
        }

        private class AddAPCommand : ICommand
        {
            public AddAPCommand()
            {
                Name = "addap";
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
                if (args.Length < 2)
                {
                    plr.SendConsoleMessage(S4Color.Red + "Wrong Usage, possible usages:");
                    plr.SendConsoleMessage(S4Color.Red + "> addap <nickname> <amount>");
                    return true;
                }

                if (args.Length >= 2)
                {
                    using (var db = AuthDatabase.Open())
                    {
                        var accountDto = (await DbUtil.FindAsync<AccountDto>(db, statement => statement
                            .Where($"{nameof(AccountDto.Nickname):C} = @Nickname")
                            .WithParameters(new { Nickname = args[0] }))).FirstOrDefault();

                        var playerDto = (await DbUtil.FindAsync<PlayerDto>(db, statement => statement
                            .Where($"{nameof(PlayerDto.Id):C} = @Id")
                            .WithParameters(new { Id = accountDto.Id }))).FirstOrDefault();

                        if (accountDto == null || playerDto == null)
                        {
                            plr.SendConsoleMessage(S4Color.Red + "Unknown player");
                            return false;
                        }

                        int.TryParse(args[1], out var amount);

                        plr.SendConsoleMessage($"Added {args[1]} ap to {accountDto.Nickname}");
                        playerDto.AP += amount;
                        DbUtil.Update(db, playerDto);

                        var player = GameServer.Instance.PlayerManager.Get((ulong)playerDto.Id);
                        if (player != null)
                        {
                            player.AP += (uint)amount;
                            await player.SendAsync(new MoneyRefreshCashInfoAckMessage(player.PEN, player.AP));
                        }
                    }
                }

                return true;
            }

            public string Help()
            {
                return Name;
            }
        }

        private class RenameCommand : ICommand
        {
            public RenameCommand()
            {
                Name = "rename";
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
                if (args.Length < 2)
                {
                    plr.SendConsoleMessage(S4Color.Red + "Wrong Usage, possible usages:");
                    plr.SendConsoleMessage(S4Color.Red + "> rename <nickname> <newname>");
                    return true;
                }

                if (args.Length >= 2)
                {
                    using (var db = AuthDatabase.Open())
                    {
                        var account = (await DbUtil.FindAsync<AccountDto>(db, statement => statement
                            .Include<BanDto>(join => join.LeftOuterJoin())
                            .Where($"{nameof(AccountDto.Nickname):C} = @Nickname")
                            .WithParameters(new { Nickname = args[0] }))).FirstOrDefault();

                        if (account == null)
                        {
                            plr.SendConsoleMessage(S4Color.Red + "Unknown player");
                            return false;
                        }

                        plr.SendConsoleMessage($"Changed {account.Nickname}'s nickname to {args[1]}");
                        account.Nickname = args[1];
                        DbUtil.Update(db, account);

                        var player = GameServer.Instance.PlayerManager.Get((ulong)account.Id);
                        player?.Session?.SendAsync(new ItemUseChangeNickAckMessage { Result = 0 });
                        player?.Session?.SendAsync(new ServerResultAckMessage(ServerResult.CreateNicknameSuccess));
                    }
                }

                return true;
            }

            public string Help()
            {
                return Name;
            }
        }

        private class SecurityCommand : ICommand
        {
            public SecurityCommand()
            {
                Name = "seclevel";
                AllowConsole = false;
                Permission = SecurityLevel.Administrator;
                SubCommands = new ICommand[] { };
            }

            public string Name { get; }
            public bool AllowConsole { get; }
            public SecurityLevel Permission { get; }
            public IReadOnlyList<ICommand> SubCommands { get; }

            public async Task<bool> Execute(GameServer server, Player plr, string[] args)
            {
                if (args.Length < 2)
                {
                    plr.SendConsoleMessage(S4Color.Red + "Wrong Usage, possible usages:");
                    plr.SendConsoleMessage(S4Color.Red + "> seclevel <nickname> <level>");
                    return true;
                }

                if (args.Length >= 2)
                {
                    using (var db = AuthDatabase.Open())
                    {
                        var account = (await DbUtil.FindAsync<AccountDto>(db, statement => statement
                            .Include<BanDto>(join => join.LeftOuterJoin())
                            .Where($"{nameof(AccountDto.Nickname):C} = @Nickname")
                            .WithParameters(new { Nickname = args[0] }))).FirstOrDefault();

                        if (account == null)
                        {
                            plr.SendConsoleMessage(S4Color.Red + "Unknown player");
                            return false;
                        }

                        if (byte.TryParse(args[1], out var level))
                        {
                            if (plr.Account.SecurityLevel <= (SecurityLevel)account.SecurityLevel)
                            {
                                plr.SendConsoleMessage($"You cannot change the seclevel of this player");
                                return true;
                            }

                            if (plr.Account.SecurityLevel <= (SecurityLevel)level)
                            {
                                plr.SendConsoleMessage($"Your cannot use a higher level than you have");
                                return true;
                            }

                            plr.SendConsoleMessage($"Changed {account.Nickname}'s seclevel to {args[1]}");
                            account.SecurityLevel = level;
                            DbUtil.Update(db, account);

                            var player = GameServer.Instance.PlayerManager.Get((ulong)account.Id);
                            player?.Session?.SendAsync(new ItemUseChangeNickAckMessage { Result = 0 });
                            player?.Session?.SendAsync(
                                new ServerResultAckMessage(ServerResult.CreateNicknameSuccess));
                        }
                        else
                        {
                            plr.SendConsoleMessage(S4Color.Red + "Wrong Usage, possible usages:");
                            plr.SendConsoleMessage(S4Color.Red + "> seclevel <username> <level>");
                            plr.SendConsoleMessage(S4Color.Red + "> seclevel <level>");
                        }
                    }
                }

                return true;
            }

            public string Help()
            {
                return Name;
            }
        }

        private class LevelCommand : ICommand
        {
            public LevelCommand()
            {
                Name = "level";
                AllowConsole = false;
                Permission = SecurityLevel.Administrator;
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
                    plr.SendConsoleMessage(S4Color.Red + "> level <nickname> <level>");
                    return true;
                }

                if (args.Length >= 2)
                {
                    AccountDto account;
                    using (var db = AuthDatabase.Open())
                    {
                        account = (await DbUtil.FindAsync<AccountDto>(db, statement => statement
                            .Include<BanDto>(join => join.LeftOuterJoin())
                            .Where($"{nameof(AccountDto.Nickname):C} = @Nickname")
                            .WithParameters(new { Nickname = args[0] }))).FirstOrDefault();

                        if (account == null)
                        {
                            plr.SendConsoleMessage(S4Color.Red + "Unknown player");
                            return true;
                        }

                        var playerdto = (await DbUtil.FindAsync<PlayerDto>(db, statement => statement
                                .Where($"{nameof(PlayerDto.Id):C} = @Id")
                                .WithParameters(new { account.Id }))
                            ).FirstOrDefault();

                        if (playerdto == null)
                        {
                            plr.SendConsoleMessage(S4Color.Red + "Unknown player");
                            return true;
                        }

                        if (byte.TryParse(args[1], out var level))
                        {
                            var expTable = GameServer.Instance.ResourceCache.GetExperience();

                            if (expTable.TryGetValue(level, out var exp))
                            {
                                plr.SendConsoleMessage($"Changed {account.Nickname}'s level to {args[1]}");

                                playerdto.Level = level;
                                playerdto.TotalExperience = exp.TotalExperience;
                                DbUtil.Update(db, playerdto);

                                var player = GameServer.Instance.PlayerManager.Get((ulong)account.Id);
                                if (player != null)
                                {
                                    player.Level = level;
                                    player.TotalExperience = exp.TotalExperience;
                                    player.Session?.SendAsync(new ExpRefreshInfoAckMessage(player.TotalExperience));
                                    player.Session?.SendAsync(
                                        new PlayerAccountInfoAckMessage(player.Map<Player, PlayerAccountInfoDto>()));
                                    player.NeedsToSave = true;
                                }
                            }
                            else
                            {
                                plr.SendConsoleMessage(S4Color.Red + "Invalid Level");
                            }
                        }
                        else
                        {
                            plr.SendConsoleMessage(S4Color.Red + "Wrong Usage, possible usages:");
                            plr.SendConsoleMessage(S4Color.Red + "> level <nickname> <level>");
                        }
                    }
                }

                return true;
            }

            public string Help()
            {
                return Name;
            }
        }

        private class SetMasterCommand : ICommand
        {
            public SetMasterCommand()
            {
                Name = "setmaster";
                AllowConsole = false;
                Permission = SecurityLevel.GameMaster;
                SubCommands = new ICommand[0];
            }

            public string Name { get; }
            public bool AllowConsole { get; }
            public SecurityLevel Permission { get; }
            public IReadOnlyList<ICommand> SubCommands { get; }

            public async Task<bool> Execute(GameServer server, Player plr, string[] args)
            {
                if (args.Length > 0)
                {
                    var searchplr = GameServer.Instance.PlayerManager.Get(args[0]);
                    if (searchplr != null)
                        if (searchplr.Room != null)
                        {
                            searchplr.Room.ChangeMasterIfNeeded(searchplr, true);
                            searchplr.Room.ChangeHostIfNeeded(searchplr, true);
                            plr.SendConsoleMessage($"\"{searchplr.Account.Nickname}\"is master now");
                        }
                        else
                        {
                            plr.SendConsoleMessage(S4Color.Red + "Player is not in a room!");
                        }
                }
                else
                {
                    if (plr.Room == null)
                    {
                        plr.SendConsoleMessage(S4Color.Red + "You are not in a room!");
                    }
                    else
                    {
                        plr.Room.ChangeMasterIfNeeded(plr, true);
                        plr.Room.ChangeHostIfNeeded(plr, true);
                        plr.SendConsoleMessage($"You are master now");
                    }
                }

                return true;
            }

            public string Help()
            {
                return Name;
            }
        }

        private class GetIDCommand : ICommand
        {
            public GetIDCommand()
            {
                Name = "getid";
                AllowConsole = true;
                Permission = SecurityLevel.Administrator;
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
                    plr.SendConsoleMessage(S4Color.Red + "> getid <nickname>");
                    return true;
                }

                var searchplr = GameServer.Instance.PlayerManager.Get(args[0]);
                if (searchplr != null)
                {
                    if (plr != null)
                        plr.SendConsoleMessage(S4Color.Green +
                                               $"> {searchplr.Account.Nickname}'s id is {searchplr.Account.Id}");
                    else
                        CommandManager.Logger.Information(
                            $"> {searchplr.Account.Nickname}'s id is {searchplr.Account.Id}");

                    return true;
                }

                plr.SendConsoleMessage(S4Color.Red + "Unknown player!");

                return false;
            }

            public string Help()
            {
                return Name;
            }
        }

        private class KillRoomCommand : ICommand
        {
            public KillRoomCommand()
            {
                Name = "killroom";
                AllowConsole = true;
                Permission = SecurityLevel.Administrator;
                SubCommands = new ICommand[0];
            }

            public string Name { get; }
            public bool AllowConsole { get; }
            public SecurityLevel Permission { get; }
            public IReadOnlyList<ICommand> SubCommands { get; }

            public async Task<bool> Execute(GameServer server, Player plr, string[] args)
            {
                if (args.Length < 2)
                {
                    plr.SendConsoleMessage(S4Color.Red + "Wrong Usage, possible usages:");
                    plr.SendConsoleMessage(S4Color.Red + "> killroom channelid roomid");
                    return true;
                }

                if (!uint.TryParse(args[0], out var channelId))
                    return false;

                if (!uint.TryParse(args[1], out var roomId))
                    return false;

                var channel = server.ChannelManager[channelId];
                var room = channel?.RoomManager[roomId];

                if (room != null)
                {
                    foreach (var kplr in room.Players.Values)
                        kplr.Room.Leave(kplr, RoomLeaveReason.ModeratorKick);
                    room.RoomManager.Remove(room);
                    return true;
                }

                return false;
            }

            public string Help()
            {
                return Name;
            }
        }
        private class AddPenCommand : ICommand
        {
            public AddPenCommand()
            {
                Name = "addpen";
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
                if (args.Length < 2)
                {
                    plr.SendConsoleMessage(S4Color.Red + "Wrong Usage, possible usages:");
                    plr.SendConsoleMessage(S4Color.Red + "> addpen <nickname> <amount>");
                    return true;
                }

                if (args.Length >= 2)
                {
                    using (var db = AuthDatabase.Open())
                    {
                        var accountDto = (await DbUtil.FindAsync<AccountDto>(db, statement => statement
                            .Where($"{nameof(AccountDto.Nickname):C} = @Nickname")
                            .WithParameters(new { Nickname = args[0] }))).FirstOrDefault();

                        var playerDto = (await DbUtil.FindAsync<PlayerDto>(db, statement => statement
                            .Where($"{nameof(PlayerDto.Id):C} = @Id")
                            .WithParameters(new { accountDto.Id }))).FirstOrDefault();

                        if (accountDto == null || playerDto == null)
                        {
                            plr.SendConsoleMessage(S4Color.Red + "Unknown player");
                            return false;
                        }

                        int.TryParse(args[1], out var amount);

                        plr.SendConsoleMessage($"Added {args[1]} Pen to {accountDto.Nickname}");
                        playerDto.PEN += amount;
                        DbUtil.Update(db, playerDto);

                        var player = GameServer.Instance.PlayerManager.Get((ulong)playerDto.Id);
                        if (player != null)
                        {
                            player.PEN += (uint)amount;
                            await player.SendAsync(new MoneyRefreshCashInfoAckMessage(player.PEN, player.AP));
                        }
                    }
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