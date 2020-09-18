namespace NeoNetsphere.Network.Services
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using BlubLib.DotNetty.Handlers.MessageHandling;
    using BlubLib.IO;
    using Dapper.FastCrud;
    using ExpressMapper.Extensions;
    using NeoNetsphere.Database.Auth;
    using NeoNetsphere.Network.Data.Game;
    using NeoNetsphere.Network.Message.Game;
    using ProudNetSrc.Handlers;
    using Serilog;
    using Serilog.Core;
    using ProudNetSrc;
    using ProudNetSrc.Serialization;

    internal class GeneralService : ProudMessageHandler
    {
        public static readonly ILogger Logger =
            Log.ForContext(Constants.SourceContextPropertyName, nameof(GeneralService));

        public static async Task<bool> ChangeNickname(Player plr, NicknameHistoryDto nicknameHistory, bool restore)
        {
            var toNickname = nicknameHistory.NewNickname;
            try
            {
                using (var db = AuthDatabase.Open())
                {
                    var account = (await DbUtil.FindAsync<AccountDto>(db, statement => statement
                        .Where($"{nameof(AccountDto.Nickname):C} = @Nickname")
                        .WithParameters(new { plr.Account.Nickname }))).FirstOrDefault();

                    if (account == null)
                    {
                        await plr.Session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
                        return false;
                    }

                    if (restore)
                    {
                        var nicknameHistory1 = await DbUtil.FindAsync<NicknameHistoryDto>(db, statement => statement
                            .Where($"{nameof(NicknameHistoryDto.AccountId):C} = @Id")
                            .WithParameters(new { plr.Account.Id }));

                        var firstchange = nicknameHistory1.FirstOrDefault();
                        if (firstchange == null)
                        {
                            await plr.Session.SendAsync(new ServerResultAckMessage(ServerResult.NicknameUnavailable));
                            return false;
                        }

                        account.Nickname = firstchange.OldName;
                        plr.Account.Nickname = firstchange.OldName;

                        foreach (var history in nicknameHistory1)
                        {
                            await DbUtil.DeleteAsync(db, history);
                        }

                        await plr.Session.SendAsync(new ItemUseChangeNickCancelAckMessage(0));
                    }
                    else
                    {
                        if (!await AuthService.IsNickAvailableAsync(toNickname))
                        {
                            await plr.Session.SendAsync(new ServerResultAckMessage(ServerResult.NicknameUnavailable));
                            return false;
                        }

                        account.Nickname = toNickname;
                        plr.Account.Nickname = toNickname;
                        DbUtil.Insert(db, nicknameHistory);
                        await plr.Session.SendAsync(new ItemUseChangeNickAckMessage
                        {
                            Result = 0,
                            Unk2 = 0,
                            Unk3 = toNickname
                        });
                    }

                    DbUtil.Update(db, account);
                    return true;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e.ToString());
                await plr.Session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
                return false;
            }
        }

        [MessageHandler(typeof(TimeSyncReqMessage))]
        public async void TimeSyncHandler(GameSession session, TimeSyncReqMessage message)
        {
            if (message.Time == uint.MaxValue)
                throw new Exception("OutOfRange");

            session?.SendAsync(new TimeSyncAckMessage
            {
                ClientTime = message.Time,
                ServerTime = (uint)Program.AppTime.ElapsedMilliseconds
            });
        }

        [MessageHandler(typeof(CheckHashKeyValueReqMessage))]
        public void CheckHashKeyValueReq(GameSession session, CheckHashKeyValueReqMessage message)
        {
            if (Config.Instance.ResCheck)
            {
                if (session.ValidRes > 0)
                {
                    if (message.ResourceHash.ToLower().Trim() != Config.Instance.ResHash.ToLower().Trim() || (message.ResourceHash.ToLower().Trim() != Config.Instance.ResHash_red.ToLower().Trim()) || (message.ResourceHash.ToLower().Trim() != Config.Instance.ResHash_gold.ToLower().Trim()) || (message.ResourceHash.ToLower().Trim() != Config.Instance.ResHash_blue.ToLower().Trim()) || (message.ResourceHash.ToLower().Trim() != Config.Instance.ResHash_violet.ToLower().Trim()))
                    {
                        var plr = session.Player;

                        if (plr?.Account.SecurityLevel <= SecurityLevel.User)
                        {
                            Logger.ForAccount(plr)
                                .Warning("Invalid Reshash (2)");
                            Logger.ForAccount(plr)
                               .Warning(message.ResourceHash.ToLower().Trim());
                            plr.SendAsync(new LoginReguestAckMessage(GameLoginResult.WrongVersion));
                            plr.Disconnect();
                            return;
                        }
                    }

                    return;
                }

                if (message.ResourceHash.ToLower().Trim() != Config.Instance.ResHash.ToLower().Trim() || (message.ResourceHash.ToLower().Trim() != Config.Instance.ResHash_red.ToLower().Trim()) || (message.ResourceHash.ToLower().Trim() != Config.Instance.ResHash_gold.ToLower().Trim()) || (message.ResourceHash.ToLower().Trim() != Config.Instance.ResHash_blue.ToLower().Trim()) || (message.ResourceHash.ToLower().Trim() != Config.Instance.ResHash_violet.ToLower().Trim()))

                {
                    session.ValidRes = 1;
                    return;
                }

                session.ValidRes = 2;
            }
        }

        [MessageHandler(typeof(NickCheckReqMessage))]
        public async Task CheckNickHandler(GameSession session, NickCheckReqMessage message)
        {
            var plr = session.Player;
            if (plr == null)
                return;

            var ascii = Config.Instance.Game.NickRestrictions.AsciiOnly;

            if (!await AuthService.IsNickAvailableAsync(message.Nickname))
            {
                await plr.Session.SendAsync(new ServerResultAckMessage(ServerResult.NicknameUnavailable));
            }

            if (!Namecheck.IsNameValid(message.Nickname, true) || ascii && message.Nickname.Any(c => c > 127) ||
                !ascii && message.Nickname.Any(c => c > 255))
            {
                await session.SendAsync(new NickCheckAckMessage(true));
                return;
            }

            await session.SendAsync(new NickCheckAckMessage(false));
        }

        [MessageHandler(typeof(ItemUseChangeNickCancelReqMessage))]
        public async Task ItemUseChangeNickCancelReq(GameSession session, ItemUseChangeNickCancelReqMessage message)
        {
            if (session.Player == null)
                return;

            var item = session.Player.Inventory.FirstOrDefault(x => x.ItemNumber == 4000002);
            if (item == null)
            {
                await session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
                return;
            }

            if (await ChangeNickname(session.Player, new NicknameHistoryDto(), true))
            {
                session.Player.Inventory.RemoveOrDecrease(item);
            }
        }

        [MessageHandler(typeof(ItemUseChangeNickReqMessage))]
        public async Task UseChangeNameItem(GameSession session, ItemUseChangeNickReqMessage message)
        {
            var plr = session.Player;
            var item = plr.Inventory[message.ItemId];

            var ascii = Config.Instance.Game.NickRestrictions.AsciiOnly;
            if (!await AuthService.IsNickAvailableAsync(message.Nickname))
            {
                await plr.Session.SendAsync(new ServerResultAckMessage(ServerResult.NicknameUnavailable));
            }

            if (!Namecheck.IsNameValid(message.Nickname, true) || ascii && message.Nickname.Any(c => c > 127) ||
                !ascii && message.Nickname.Any(c => c > 255))
            {
                await session.SendAsync(new NickCheckAckMessage(true));
                return;
            }

            var nickname = new NicknameHistoryDto
            {
                AccountId = (int)plr.Account.Id,
                OldName = plr.Account.Nickname,
                NewNickname = message.Nickname
            };

            switch (item.ItemNumber)
            {
                case 4000001: //Perm Change
                    nickname.ExpireDate = -1;
                    if (await ChangeNickname(session.Player, nickname, false))
                    {
                        plr.Inventory.RemoveOrDecrease(item);
                    }
                    break;
                case 4000003: // 1 Day
                    nickname.ExpireDate = DateTimeOffset.Now.AddDays(1).ToUnixTimeSeconds();
                    if (await ChangeNickname(session.Player, nickname, false))
                    {
                        plr.Inventory.RemoveOrDecrease(item);
                    }
                    break;
                case 4000004: // 7 Day
                    nickname.ExpireDate = DateTimeOffset.Now.AddDays(7).ToUnixTimeSeconds();
                    if (await ChangeNickname(session.Player, nickname, false))
                    {
                        plr.Inventory.RemoveOrDecrease(item);
                    }
                    break;
                case 4000005: // 30 Day
                    nickname.ExpireDate = DateTimeOffset.Now.AddDays(30).ToUnixTimeSeconds();
                    if (await ChangeNickname(session.Player, nickname, false))
                    {
                        plr.Inventory.RemoveOrDecrease(item);
                    }
                    break;

                    
                default:
                    await session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
                    return;
            }
        }

        [MessageHandler(typeof(MoneyRefreshCashInfoReqMessage))]
        public async Task MoneyRefreshCashInfoReq(GameSession session, MoneyRefreshCashInfoReqMessage message)
        {
            if (session.Player == null)
                return;
            await session.SendAsync(new MoneyRefreshCashInfoAckMessage(session.Player.PEN, session.Player.AP));
        }

        [MessageHandler(typeof(BattleyeC2SDataMessage))]
        public async Task BattleyeC2SData(GameSession session, BattleyeC2SDataMessage message)
        {
            var plr = session.Player;

            if (plr == null || string.IsNullOrEmpty(plr.Account.Nickname))
                return;

            if (!plr.BE.HB_Enabled)
                return;

            if (Config.Instance.ACMode != 2)
                return;

            var disconnect = false;
            try
            {
                if (message.DataSize != message.Data.Length)
                {
                    Logger.ForAccount(plr)
                        .Warning("[BE] 0, 0, 0");
                    await session.SendAsync(new ServerResultAckMessage(ServerResult.HackingTrialDetected));
                    await session.CloseAsync();
                    return;
                }

                switch (message.DataSize)
                {
                    case 1:
                        if (message.Data[0] == 0x0B)
                        {
                            Logger.ForAccount(plr)
                                .Warning("[BE] 1, 0, 0");
                            disconnect = true;
                        }

                        break;
                    case 2:
                        if (plr.BE.HB_Info_Received == 0)
                        {
                            if (message.Data[0] != 0)
                            {
                                Logger.ForAccount(plr)
                                    .Warning("[BE] 2, 0, 0");
                                disconnect = true;
                            }

                            plr.BE.HB_Info_Received = 1;
                        }
                        else if (plr.BE.HB_Info_Received == 1)
                        {
                            if (message.Data[0] != 2)
                            {
                                Logger.ForAccount(plr)
                                    .Warning("[BE] 2, 1, 1");
                                disconnect = true;
                            }

                            if (message.Data[1] != 0)
                            {
                                Logger.ForAccount(plr)
                                    .Warning("[BE] 2, 1, 2");
                                disconnect = true;
                            }

                            plr.BE.HB_Info_Received = 2;
                        }

                        break;
                }

                if (message.DataSize > 0)
                {
                    switch (message.Data[0])
                    {
                        case 0x05:
                            if (message.DataSize == 321)
                            {
                                Logger.ForAccount(plr)
                                    .Warning("[BE] Player encountered a Heartbeat error (1)");
                                disconnect = true;
                            }

                            break;
                        case 0x09:
                            var counter = BitConverter.ToUInt16(message.Data, 1);
                            if (counter != plr.BE.HB_Count - 1)
                            {
                                Logger.ForAccount(plr)
                                    .Warning("[BE] Player encountered a Heartbeat error (2)");
                                disconnect = true;
                            }
                            else
                                plr.BE.HB_Last_Issued = true;

                            break;
                    }
                }
            }
            finally
            {
                if (disconnect)
                {
                    Logger.ForAccount(plr)
                        .Warning("[BE] Bad Packet");
                    await session.SendAsync(new ServerResultAckMessage(ServerResult.HackingTrialDetected));
                    await session.CloseAsync();
                }
            }
        }
    }
}