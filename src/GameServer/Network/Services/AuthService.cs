using System.Reflection.Metadata;

namespace NeoNetsphere.Network.Services
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using System.Text;
  using System.Threading.Tasks;
  using BlubLib.DotNetty.Handlers.MessageHandling;
  using BlubLib.Security.Cryptography;
  using Dapper.FastCrud;
  using Data.Chat;
  using Data.Club;
  using Data.Game;
  using Database.Auth;
  using Database.Game;
  using ExpressMapper.Extensions;
  using Message.Chat;
  using Message.Club;
  using Message.Game;
  using Message.Relay;
  using Newtonsoft.Json;
  using ProudNetSrc.Handlers;
  using Resource;
  using Serilog;
  using Serilog.Core;

  internal class AuthService : ProudMessageHandler
  {
    private static readonly Version SVersion = new Version(0, 8, 32, 63353);

    // ReSharper disable once InconsistentNaming
    private static readonly ILogger Logger =
        Log.ForContext(Constants.SourceContextPropertyName, nameof(AuthService));

    [MessageHandler(typeof(LoginRequestReqMessage))]
    public async Task LoginHandler(GameSession session, LoginRequestReqMessage message)
    {
      #region IPINFO

      var ipInfo = new IpInfo2();
      try
      {
        var info = new WebClient().DownloadString("http://ip-api.com/json/" + session.RemoteEndPoint.Address);
        ipInfo = JsonConvert.DeserializeObject<IpInfo2>(info);
        if (string.IsNullOrWhiteSpace(ipInfo.countryCode) || string.IsNullOrEmpty(ipInfo.countryCode))
        {
          ipInfo.countryCode = "UNK";
        }
      }
      catch (Exception)
      {
        ipInfo.countryCode = "UNK";
      }

      #endregion

      #region Validate Login

      AccountDto accountDto;
      using (var db = AuthDatabase.Open())
      {
        accountDto = (await DbUtil.FindAsync<AccountDto>(db, statement => statement
                .Include<BanDto>(join => join.LeftOuterJoin())
                .Where($"{nameof(AccountDto.Id):C} = @Id")
                .WithParameters(new { Id = message.AccountId })))
            .FirstOrDefault();
      }

      if (accountDto == null)
      {
        await session.SendAsync(new LoginReguestAckMessage(GameLoginResult.SessionTimeout));
        await session.CloseAsync();
        return;
      }

      message.AccountId = (ulong)accountDto.Id;
      message.Username = accountDto.Username;

      Logger.ForAccount(accountDto)
          .Information("GameServer login from {remoteEndPoint} : Country: {country}", session.RemoteEndPoint,
              ipInfo.countryCode);

      if (Config.Instance.BlockedCountries.ToList().Contains(ipInfo.countryCode))
      {
        Logger.ForAccount(accountDto)
            .Warning("Denied connection from client in blocked country {country}", ipInfo.countryCode);

        await session.SendAsync(new ServerResultAckMessage(ServerResult.IPLocked));
        await session.CloseAsync();
        return;
      }

      if (Config.Instance.BlockedAddresses.ToList().Contains(session.RemoteEndPoint.Address.ToString()))
      {
        Logger.ForAccount(accountDto)
            .Warning("Denied connection from client of blocked ip {adress}", session.RemoteEndPoint);

        await session.SendAsync(new ServerResultAckMessage(ServerResult.IPLocked));
        await session.CloseAsync();
        return;
      }

      // if (message.Version != s_version)
      // {
      //     Logger.ForAccount(message.AccountId, message.Username)
      //         .Warning("Invalid client version {version}", message.Version);
      //
      //     session.SendAsync(new LoginReguestAckMessage(GameLoginResult.WrongVersion));
      //     return;
      // }

      if (GameServer.Instance.PlayerManager.Count >= Config.Instance.PlayerLimit)
      {
        Logger.ForAccount(accountDto)
            .Warning("Server is full");

        await session.SendAsync(new LoginReguestAckMessage(GameLoginResult.ServerFull));
        await session.CloseAsync();
        return;
      }

      var sessionId = Hash.GetUInt32<CRC32>($"<{accountDto.Username}+{accountDto.Password}+{session.RemoteEndPoint.Address.MapToIPv4()}+{Config.Instance.AuthAPI.ApiKey}>");
      var authsessionId = Hash.GetString<CRC32>($"<{accountDto.Username}+{sessionId}+{message.Datetime}>");
      if (authsessionId != message.AuthToken)
      {
        Logger.ForAccount(accountDto)
            .Warning("Wrong sessionid(2)");

        await session.SendAsync(new LoginReguestAckMessage(GameLoginResult.SessionTimeout));
        await session.CloseAsync();
        return;
      }

      var newsessionId = Hash.GetString<CRC32>($"<{authsessionId}+{sessionId}>");
      if (newsessionId != message.newToken)
      {
        Logger.ForAccount(accountDto)
            .Warning("Wrong sessionid(3)");

        await session.SendAsync(new LoginReguestAckMessage(GameLoginResult.SessionTimeout));
        await session.CloseAsync();
        return;
      }

      var now = DateTimeOffset.Now.ToUnixTimeSeconds();
      var ban = accountDto.Bans.FirstOrDefault(b => b.Date + (b.Duration ?? 0) > now);
      if (ban != null)
      {
        var unbanDate = DateTimeOffset.FromUnixTimeSeconds(ban.Date + (ban.Duration ?? 0));
        Logger.ForAccount(accountDto)
            .Warning("Banned until {unbanDate}", unbanDate);

        await session.SendAsync(new LoginReguestAckMessage(GameLoginResult.SessionTimeout));
        await session.CloseAsync();
        return;
      }

      var account = new Account(accountDto);

      #endregion

      if (account.SecurityLevel < Config.Instance.SecurityLevel)
      {
        Logger.ForAccount(account).Warning("No permission to enter this server({securityLevel} or above required)",
            Config.Instance.SecurityLevel);

        await session.SendAsync(new LoginReguestAckMessage(GameLoginResult.AuthenticationFailed));
        await session.CloseAsync();
        return;
      }

      if (GameServer.Instance.PlayerManager.Contains(account.Id))
      {
        Logger.ForAccount(account)
            .Information("Kicking old connection");

        var oldPlr = GameServer.Instance.PlayerManager.Get(account.Id);
        GameServer.Instance.PlayerManager.Remove(oldPlr);
        oldPlr?.Session.CloseAsync();
      }

      Logger.ForAccount(account)
          .Information("Login success");

      await Task.Run(async () =>
      {
        using (var db = GameDatabase.Open())
        {
          var plrDto = (await DbUtil.FindAsync<PlayerDto>(db, statement => statement
                        .Include<PlayerCharacterDto>(join => join.LeftOuterJoin())
                        .Include<PlayerDenyDto>(join => join.LeftOuterJoin())
                        .Include<PlayerFriendDto>(join => join.LeftOuterJoin())
                        .Include<PlayerSettingDto>(join => join.LeftOuterJoin())
                        .Include<PlayerItemDto>(join => join.LeftOuterJoin())
                        .Include<PlayerMailDto>(join => join.LeftOuterJoin())
                        .Where($"{nameof(PlayerDto.Id):C} = @Id")
                        .WithParameters(new { Id = message.AccountId })))
                    .FirstOrDefault();

          var plrStatsDto = (await DbUtil.FindAsync<PlayerDto>(db, statement => statement
                        .Include<PlayerDeathMatchDto>(join => join.LeftOuterJoin())
                        .Include<PlayerTouchDownDto>(join => join.LeftOuterJoin())
                        .Include<PlayerChaserDto>(join => join.LeftOuterJoin())
                        .Include<PlayerBattleRoyalDto>(join => join.LeftOuterJoin())
                        .Include<PlayerCaptainDto>(join => join.LeftOuterJoin())
                        .Where($"{nameof(PlayerDto.Id):C} = @Id")
                        .WithParameters(new { Id = message.AccountId })))
                    .FirstOrDefault();

          var expTable = GameServer.Instance.ResourceCache.GetExperience();
          Experience expValue;

          if (plrDto == null)
          {
                  // first time connecting to this server
                  if (!expTable.TryGetValue(Config.Instance.Game.StartLevel, out expValue))
            {
              expValue = new Experience { TotalExperience = 0 };
              Logger.Warning("Given start level is not found in the experience table");
            }

            plrDto = new PlayerDto
            {
              Id = (int)account.Id,
              PlayTime = TimeSpan.FromSeconds(0).ToString(),
              Level = Config.Instance.Game.StartLevel,
              PEN = Config.Instance.Game.StartPEN,
              AP = Config.Instance.Game.StartAP,
              Coins1 = Config.Instance.Game.StartCoins1,
              Coins2 = Config.Instance.Game.StartCoins2,
              TotalExperience = expValue.TotalExperience,
            };

            try
            {
              await DbUtil.InsertAsync(db, plrDto);
            }
            catch (Exception e)
            {
              session.Channel.Pipeline.FireExceptionCaught(e);
              return;
            }
          }
          else
          {
            if (!TimeSpan.TryParse(plrDto.PlayTime, out _))
            {
              plrDto.PlayTime = TimeSpan.FromSeconds(0).ToString();
            }

            if (!expTable.TryGetValue(plrDto.Level, out expValue))
            {
              expValue = new Experience { TotalExperience = 0 };
              Logger.Warning("Given level is not found in the experience table");
            }

                  // Adjust total exp to correct range
                  if (plrDto.TotalExperience < expValue.TotalExperience)
            {
              plrDto.TotalExperience = expValue.TotalExperience;
              await DbUtil.UpdateAsync(db, plrDto);
            }

                  // Adjust total exp to minexp of current level
                  if (plrDto.Level > 0 && plrDto.TotalExperience == 0)
            {
              plrDto.TotalExperience = expValue.TotalExperience - 1;
              await DbUtil.UpdateAsync(db, plrDto);
            }
          }

          if (plrStatsDto != null)
          {
            plrDto.DeathMatchInfo = plrStatsDto.DeathMatchInfo;
            plrDto.TouchDownInfo = plrStatsDto.TouchDownInfo;
            plrDto.ChaserInfo = plrStatsDto.ChaserInfo;
            plrDto.BattleRoyalInfo = plrStatsDto.BattleRoyalInfo;
            plrDto.CaptainInfo = plrStatsDto.CaptainInfo;
          }

          session.Player = new Player(session, account, plrDto);
        }

        if (session.Player == null)
        {
          Logger.ForAccount(account)
                    .Error("PlayerInfo failed - Missing playerInstance");
          return;
        }

        GameServer.Instance.PlayerManager.Add(session.Player);

        var result = string.IsNullOrWhiteSpace(account.Nickname)
                  ? GameLoginResult.ChooseNickname
                  : GameLoginResult.OK;

        result = session.Player.CharacterManager.Any() ? result : GameLoginResult.ChooseNickname;

        if (session.UpdateShop)
        {
          await ShopService.ShopUpdateMsg(session, false);
          session.UpdateShop = false;
        }

        await session.SendAsync(new LoginReguestAckMessage(result, session.Player.Account.Id));

        if (result == GameLoginResult.OK)
        {
          await LoginAsync(session);
        }
      });
    }

    public static async Task<bool> IsNickAvailableAsync(string nickname)
    {
      var minLength = Config.Instance.Game.NickRestrictions.MinLength;
      var maxLength = Config.Instance.Game.NickRestrictions.MaxLength;
      var whitespace = Config.Instance.Game.NickRestrictions.WhitespaceAllowed;
      var ascii = Config.Instance.Game.NickRestrictions.AsciiOnly;

      if (ascii)
      {
        if (nickname.Any(c => c > 127))
          return false;
      }
      else
      {
        if (nickname.Any(c => c > 255))
          return false;
      }

      if (!Namecheck.IsNameValid(nickname))
      {
        return false;
      }

      if (nickname.Length < minLength || nickname.Length > maxLength ||
          ascii && Encoding.UTF8.GetByteCount(nickname) != nickname.Length)
      {
        return false;
      }

      // check for repeating chars example: (AAAHello, HeLLLLo)
      var maxRepeat = Config.Instance.Game.NickRestrictions.MaxRepeat;
      if (maxRepeat > 0)
      {
        var counter = 1;
        var current = nickname[0];
        for (var i = 1; i < nickname.Length; i++)
        {
          if (current != nickname[i])
          {
            if (counter > maxRepeat)
            {
              return false;
            }

            counter = 0;
            current = nickname[i];
          }

          counter++;
        }
      }

      var now = DateTimeOffset.Now.ToUnixTimeSeconds();
      using (var db = AuthDatabase.Open())
      {
        var nickExists = (await DbUtil.FindAsync<AccountDto>(db, statement => statement
                .Where($"{nameof(AccountDto.Nickname):C} = @{nameof(nickname)}")
                .WithParameters(new { nickname })))
            .Any();

        var nickReserved = (await DbUtil.FindAsync<NicknameHistoryDto>(db, statement => statement
                .Where(
                    $"{nameof(NicknameHistoryDto.OldName):C} = @{nameof(nickname)} AND ({nameof(NicknameHistoryDto.ExpireDate):C} = -1 OR {nameof(NicknameHistoryDto.ExpireDate):C} > @{nameof(now)})")
                .WithParameters(new { nickname, now })))
            .Any();

        return !nickExists && !nickReserved;
      }
    }

    public static async Task LoginAsync(GameSession session)
    {
      var plr = session.Player;
      plr.LoggedIn = true;

      try
      {
        plr?.SendAsync(new MoneyRefreshCashInfoAckMessage(plr.PEN, plr.AP));
        plr?.SendAsync(new CharacterCurrentSlotInfoAckMessage
        {
          ActiveCharacter = plr.CharacterManager.CurrentSlot,
          CharacterCount = (byte)plr.CharacterManager.Count,
          MaxSlots = 3
        });

        plr?.SendAsync(new MoenyRefreshCoinInfoAckMessage(plr.Coins1, plr.Coins2));
        plr?.SendAsync(new ShoppingBasketListInfoAckMessage());

        foreach (var data in ArraySplitter.Split(
            plr.Inventory.Select(i => i.Map<PlayerItem, ItemDto>()).ToArray(),
            300))
        {
          plr?.SendAsync(new ItemInventoryInfoAckMessage(data));
        }

        plr?.SendAsync(new PlayeArcadeMapInfoAckMessage());
        plr?.SendAsync(new PlayerArcadeStageInfoAckMessage());
        plr?.SendAsync(new ClubMyInfoAckMessage(plr.Map<Player, ClubMyInfoDto>()));

        foreach (var @char in plr.CharacterManager)
        {
          plr?.SendAsync(new CharacterCurrentInfoAckMessage
          {
            Slot = @char.Slot,
            Style = new CharacterStyle(@char.Gender, @char.Slot)
          });

          plr?.SendAsync(new CharacterCurrentItemInfoAckMessage
          {
            Slot = @char.Slot,
            Weapons = @char.Weapons.GetItems().Select(i => i?.Id ?? 0).ToArray(),
            Skills = new[] { @char.Skills.GetItem(SkillSlot.Skill)?.Id ?? 0 },
            Clothes = @char.Costumes.GetItems().Select(i => i?.Id ?? 0).ToArray()
          });
        }
      }
      finally
      {
        plr?.SendAsync(new ItemEquipBoostItemInfoAckMessage());
        plr?.SendAsync(new EspherChipLv5Message());
        plr?.SendAsync(new ItemClearInvalidEquipItemAckMessage());
        plr?.SendAsync(new ItemClearEsperChipAckMessage());
        plr?.SendAsync(new MapOpenInfosMessage());
        await plr.SendAsync(new PlayerAccountInfoAckMessage(plr.Map<Player, PlayerAccountInfoDto>()));
        await plr.SendAsync(new ServerResultAckMessage(ServerResult.WelcomeToS4World));
      }
    }

    [MessageHandler(typeof(LoginReqMessage))]
    public async Task ChatLoginHandler(ChatSession session, LoginReqMessage message)
    {
      var plr = GameServer.Instance.PlayerManager[message.AccountId];
      if (plr == null)
      {
        await session.SendAsync(new LoginAckMessage(3));
        await session.CloseAsync();
        return;
      }

      Logger.ForAccount(plr)
          .Information("ChatServer login from {remoteEndPoint}", session.RemoteEndPoint);

      var ip = session.RemoteEndPoint;
      var gameIp = plr.Session.RemoteEndPoint;
      if (!gameIp.Address.Equals(ip.Address))
      {
        Logger.ForAccount(plr).Warning("Suspicious login");
        await session.SendAsync(new LoginAckMessage(4));
        await session.CloseAsync();
        return;
      }

      if (Config.Instance.ResCheck)
      {
        if (plr.Session?.ValidRes == 0)
        {
          Logger.ForAccount(plr).Warning("Missing Reshash");
          await plr.SendAsync(new LoginReguestAckMessage(GameLoginResult.WrongVersion));
          await session.CloseAsync();
          return;
        }

        if (plr.Session?.ValidRes == 1)
        {
          Logger.ForAccount(plr)
              .Warning("Invalid Reshash (1)");

          if (plr.Account.SecurityLevel <= SecurityLevel.User)
          {
            await plr.SendAsync(new LoginReguestAckMessage(GameLoginResult.WrongVersion));
            await session.CloseAsync();
            return;
          }
        }

        if (plr.Session?.ValidRes == 2)
        {
          Logger.ForAccount(plr).Information("Valid Reshash");
        }
      }

      if (plr.ChatSession != null)
      {
        Logger.ForAccount(plr).Warning("Already online");
        await session.SendAsync(new LoginAckMessage(5));
        await session.CloseAsync();
        return;
      }

      session.GameSession = plr.Session;
      plr.ChatSession = session;

      Logger.ForAccount(plr)
          .Information("Login success");

      try
      {
        await session.SendAsync(new LoginAckMessage(0));
        try
        {
          await session.SendAsync(new DenyListAckMessage(plr.DenyManager.Select(d => d.Map<Deny, DenyDto>()).ToArray()));
          await session.SendAsync(
              new FriendListAckMessage(plr.FriendManager.Select(d => d.GetFriend())
                  .Where(x => x.State != 0).ToArray()));

          var playerInfoList = plr.FriendManager.Select(d => d.Map<Friend, PlayerInfoDto>()).ToList();
          if (plr.Club?.Id > 0)
          {
            foreach (var newPlr in plr.Club.Players.Select(d => d.Value.Map<ClubPlayerInfo, PlayerInfoDto>()))
            {
              if (!playerInfoList.Contains(newPlr))
                playerInfoList.Add(newPlr);
            }

            Club.LogOn(plr);
          }

          await session.SendAsync(new ChatPlayerInfoListAckMessage(playerInfoList.ToArray()));
        }
        finally
        {
          await session.SendAsync(new ChatPlayerInfoAckMessage(plr.Map<Player, PlayerInfoDto>()));
        }
      }
      catch (Exception ex)
      {
        session.Channel.Pipeline.FireExceptionCaught(ex);
      }
    }

    [MessageHandler(typeof(CRequestLoginMessage))]
    public async Task RelayLoginHandler(RelaySession session, CRequestLoginMessage message)
    {
      var plr = GameServer.Instance.PlayerManager[message.AccountId];
      if (plr == null)
      {
        await session.SendAsync(new SNotifyLoginResultMessage(1));
        await session.CloseAsync();
        return;
      }

      var ip = session.RemoteEndPoint;
      Logger.ForAccount(plr).Information("RelayServer login from {remoteAddress}", ip);

      var gameIp = plr.Session.RemoteEndPoint;
      if (!gameIp.Address.Equals(ip.Address))
      {
        Logger.ForAccount(plr).Error("Suspicious login");
        await plr.SendAsync(new SNotifyLoginResultMessage(1));
        await session.CloseAsync();
        return;
      }

      var inChannel = plr.Channel != null && plr.Channel.Id > 0;
      var inRoom = plr.Room != null && plr.Room.Id > 0;
      if (!inChannel || !inRoom)
      {
        if (!inChannel)
          Logger.ForAccount(plr).Error("Suspicious login (Not inside a channel)");
        else
          Logger.ForAccount(plr).Error("Suspicious login (Not inside a room)");

        plr?.Room?.Leave(plr);
        await plr.SendAsync(new SNotifyLoginResultMessage(1));
        await session.CloseAsync();
        return;
      }

      var roomExists = plr.Channel.RoomManager.Any(x => x.Id == message.RoomLocation.RoomId);
      var unequalRoom = plr.Room?.Id != message.RoomLocation.RoomId;
      if (unequalRoom || !roomExists)
      {
        Logger.ForAccount(plr).Error($"Suspicious login (Invalid roomId: {message.RoomLocation.RoomId})");

        plr?.Room.Leave(plr);
        await plr.SendAsync(new SNotifyLoginResultMessage(1));
        await session.CloseAsync();
        return;
      }

      if (plr.RelaySession != null && plr.RelaySession != session)
      {
        await plr.RelaySession.CloseAsync();
        plr.RelaySession = null;
      }

      session.GameSession = plr.Session;
      plr.RelaySession = session;

      Logger.ForAccount(plr)
          .Information("Login success");

      await plr.SendAsync(new SEnterLoginPlayerMessage(plr.RelaySession.HostId, plr.Account.Id, plr.Account.Nickname));

      foreach (var p in plr.Room.Players.Values)
      {
        if (p?.RelaySession == null)
          continue;

        await p.SendAsync(new SEnterLoginPlayerMessage(plr.RelaySession.HostId, plr.Account.Id, plr.Account.Nickname));
        await plr.SendAsync(new SEnterLoginPlayerMessage(p.RelaySession.HostId, p.Account?.Id ?? 0, p.Account?.Nickname ?? "n/A"));
      }

      plr.Room.Group?.Join(session.HostId);
      plr.RoomInfo.IsConnecting = false;
      plr.Room.OnPlayerJoined(new RoomPlayerEventArgs(plr));
      await plr.SendAsync(new SNotifyLoginResultMessage(0));
    }
  }
}