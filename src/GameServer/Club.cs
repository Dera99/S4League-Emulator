using NeoNetsphere.Network.Data.Chat;

namespace NeoNetsphere
{
  using System.Collections.Concurrent;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using Dapper.FastCrud;
  using ExpressMapper.Extensions;
  using NeoNetsphere.Database.Auth;
  using NeoNetsphere.Database.Game;
  using NeoNetsphere.Network;
  using NeoNetsphere.Network.Data.Club;
  using NeoNetsphere.Network.Data.Game;
  using NeoNetsphere.Network.Message.Chat;
  using NeoNetsphere.Network.Message.Club;
  using NeoNetsphere.Network.Message.Game;
  using Serilog;
  using Serilog.Core;

  internal class DBClubInfoDto
  {
    public ClubDto ClubDto { get; set; }

    public ClubPlayerInfo[] PlayerDto { get; set; }
  }

  internal class ClubPlayerInfo
  {
    public ulong AccountId { get; set; }

    public ClubState State { get; set; }

    public ClubRank Rank { get; set; }

    public AccountDto Account { get; set; }
  }

  internal class Club
  {
    // ReSharper disable once InconsistentNaming
    private static readonly ILogger Logger = Log.ForContext(Constants.SourceContextPropertyName, "GameClubMgr");

    public Club(ClubDto dto, IEnumerable<ClubPlayerInfo> player)
    {
      Players = new ConcurrentDictionary<ulong, ClubPlayerInfo>(player.ToDictionary(playerinfo =>
          playerinfo.AccountId));
      Id = dto.Id;
      ClanName = dto.Name;
      ClanIcon = dto.Icon;

      CheckMaster();
    }

    public static bool operator !=(Club a, Club b)
    {
      return a?.Id != b?.Id;
    }

    public static bool operator ==(Club a, Club b)
    {
      return a?.Id == b?.Id;
    }

    public ConcurrentDictionary<ulong, ClubPlayerInfo> Players { get; }

    public ClubPlayerInfo this[ulong id] => GetPlayer(id);

    public int Count => Players.Count;

    public uint Id { get; }

    public string ClanIcon { get; } = "1-1-1";

    public string ClanName { get; } = "S4Plus";

    public ClubPlayerInfo GetPlayer(ulong id)
    {
      Players.TryGetValue(id, out var returnval);
      return returnval;
    }

    public async Task CheckMaster()
    {
      if (Players.Any(x => x.Value.Rank == ClubRank.Master))
        return;

      var nextMaster = Players.Values.OrderBy(x => x.Rank).FirstOrDefault(x => x.Rank <= ClubRank.Regular);
      if (nextMaster != null)
      {
        nextMaster.Rank = ClubRank.Master;
        using (var db = GameDatabase.Open())
        {
          var nextMasterDto = new ClubPlayerDto
          {
            PlayerId = nextMaster.Account.Id,
            ClubId = Id,
            Rank = (byte)ClubRank.Master,
            State = (int)ClubState.Joined
          };
          await DbUtil.UpdateAsync(db, nextMasterDto);
        }

        Logger.Information("Auto MasterChange in Clan {0} to player {1}", ClanName,
            nextMaster.Account.Nickname);
      }
    }

    public async Task<bool> ChangeStaffStatus(string nickname, bool isStaff)
    {
      AccountDto account;
      using (var db = AuthDatabase.Open())
      {
        account = (await DbUtil.FindAsync<AccountDto>(db, statement => statement
                .Include<BanDto>(join => join.LeftOuterJoin())
                .Where($"{nameof(AccountDto.Nickname):C} = @Nickname")
                .WithParameters(new { Nickname = nickname }))
            ).FirstOrDefault();

        if (account == null)
          return false;
      }

      return await ChangeStaffStatus((ulong)account.Id, isStaff);
    }

    public async Task<bool> ChangeStaffStatus(ulong target, bool isStaff)
    {
      if (!Players.TryGetValue(target, out var clubPlr))
        return false;

      clubPlr.Rank = isStaff ? ClubRank.Staff : ClubRank.Regular;
      var plrDto = new ClubPlayerDto
      {
        PlayerId = clubPlr.Account.Id,
        ClubId = Id,
        Rank = (byte)(isStaff ? ClubRank.Staff : ClubRank.Regular),
        State = (int)ClubState.Joined
      };

      using (var db = GameDatabase.Open())
      {
        await DbUtil.UpdateAsync(db, plrDto);
      }

      return true;
    }

    public async Task<bool> ForceChangeMaster(string nickname)
    {
      AccountDto account;
      using (var db = AuthDatabase.Open())
      {
        account = (await DbUtil.FindAsync<AccountDto>(db, statement => statement
                .Include<BanDto>(join => join.LeftOuterJoin())
                .Where($"{nameof(AccountDto.Nickname):C} = @Nickname")
                .WithParameters(new { Nickname = nickname }))
            ).FirstOrDefault();

        if (account == null)
          return false;
      }

      return await ForceChangeMaster((ulong)account.Id);
    }

    public async Task<bool> ForceChangeMaster(ulong target)
    {
      if (!Players.TryGetValue(target, out var clubPlr))
        return false;

      var clubMaster = Players.FirstOrDefault(x => x.Value.Rank == ClubRank.Master).Value;
      if (clubMaster != null)
      {
        clubMaster.Rank = ClubRank.Regular;
        using (var db = GameDatabase.Open())
        {
          var oldMasterDto = new ClubPlayerDto
          {
            PlayerId = clubMaster.Account.Id,
            ClubId = Id,
            Rank = (byte)ClubRank.Regular,
            State = (int)ClubState.Joined
          };
          await DbUtil.UpdateAsync(db, oldMasterDto);
        }
      }

      clubPlr.Rank = ClubRank.Master;
      var plrDto = new ClubPlayerDto
      {
        PlayerId = clubPlr.Account.Id,
        ClubId = Id,
        Rank = (byte)ClubRank.Master,
        State = (int)ClubState.Joined
      };

      using (var db = GameDatabase.Open())
      {
        await DbUtil.UpdateAsync(db, plrDto);
      }

      return true;
    }

    public async Task<bool> ChangeMaster(Player plr, ulong target)
    {
      if (Players.TryGetValue(plr.Account.Id, out var clubMaster))
      {
        if (Players.TryGetValue(target, out var clubPlr))
        {
          if (clubMaster.Rank == ClubRank.Master)
          {
            clubPlr.Rank = ClubRank.Master;
            clubMaster.Rank = ClubRank.Regular;

            var plrDto = new ClubPlayerDto
            {
              PlayerId = clubPlr.Account.Id,
              ClubId = Id,
              Rank = (byte)ClubRank.Master,
              State = (int)ClubState.Joined
            };

            var oldMasterDto = new ClubPlayerDto
            {
              PlayerId = clubMaster.Account.Id,
              ClubId = Id,
              Rank = (byte)ClubRank.Regular,
              State = (int)ClubState.Joined
            };

            using (var db = GameDatabase.Open())
            {
              await DbUtil.UpdateAsync(db, plrDto);
              await DbUtil.UpdateAsync(db, oldMasterDto);
            }

            return true;
          }
        }
      }

      return false;
    }

    public async Task<bool> RemovePlayer(ulong target)
    {
      AccountDto account;
      using (var db = AuthDatabase.Open())
      {
        account = (await DbUtil.FindAsync<AccountDto>(db, statement => statement
                .Include<BanDto>(join => join.LeftOuterJoin())
                .Where($"{nameof(AccountDto.Id):C} = @Id")
                .WithParameters(new { Id = target }))
            ).FirstOrDefault();

        if (account == null)
          return false;
      }

      using (var db = GameDatabase.Open())
      {
        var clubPlrDto = new ClubPlayerDto
        {
          PlayerId = account.Id,
          ClubId = Id
        };

        DbUtil.Delete(db, clubPlrDto);
      }

      var player = GameServer.Instance.PlayerManager[target];

      Players.Remove(target, out _);

      if (player != null)
      {
        LogOff(player);
        player.Club = null;
        await player.Session.SendAsync(new ClubMyInfoAckMessage(player.Map<Player, ClubMyInfoDto>()));
      }

      CheckMaster();
      return true;
    }

    public async Task<bool> AddPlayer(ulong target)
    {
      AccountDto account;
      using (var db = AuthDatabase.Open())
      {
        account = (await DbUtil.FindAsync<AccountDto>(db, statement => statement
                .Include<BanDto>(join => join.LeftOuterJoin())
                .Where($"{nameof(AccountDto.Id):C} = @Id")
                .WithParameters(new { Id = target }))
            ).FirstOrDefault();

        if (account == null)
          return false;
      }

      var plrDto = new ClubPlayerDto
      {
        PlayerId = account.Id,
        ClubId = Id,
        Rank = (byte)ClubRank.Regular,
        State = (int)ClubState.Joined
      };

      using (var db = GameDatabase.Open())
      {
        await DbUtil.InsertAsync(db, plrDto);
      }

      var plrInfo = new ClubPlayerInfo
      {
        Account = account,
        AccountId = (ulong)account.Id,
        State = ClubState.Joined,
        Rank = ClubRank.Regular
      };

      Players.TryAdd(target, plrInfo);

      CheckMaster();

      var player = GameServer.Instance.PlayerManager[target];
      if (player != null)
      {
        player.Club = this;
        await player.Session.SendAsync(new ClubMyInfoAckMessage(player.Map<Player, ClubMyInfoDto>()));
        LogOn(player);
      }

      return true;
    }

    public void SendInvite(Player sender, Player plr)
    {
      if (plr == null)
        return;

      sender?.Mailbox.SendAsync(
          plr.Account.Nickname,
          $"<Note Key =\"3\"Cnt =\"1\"Param1=\"{ClanName}\" />",
          $"<Note Key =\"4\"Srl =\"{Id}\"Cnt =\"2\"Param1=\"{ClanName}\"Param2=\"{sender.Account.Nickname}\" />",
          true);
    }

    public static void LogOn(Player plr, bool noRooms = false)
    {
      if (plr.Club?.Id > 0)
      {
        plr.Club?.Broadcast(new ClubSystemMessageMessage(plr.Account.Id, $"<Chat Key =\"1\"Cnt =\"2\"Param1=\"{plr.Account.Nickname}\"Param2=\"1\" />"));
        plr.Club?.Broadcast(new ClubMemberLoginStateAckMessage(1, plr.Account.Id));
        plr.SendAsync(new ClanMemberListAckMessage(GameServer.Instance.PlayerManager.Where(x => x.Club == plr.Club).Select(x => x.Map<Player, PlayerInfoDto>()).ToArray()));
        plr.SendAsync(new ClubNewsRemindMessage(-1, 0));

        if (!noRooms)
        {
          plr.Room?.Broadcast(new RoomPlayerInfoListForEnterPlayerAckMessage(plr.Room.TeamManager.Players
              .Select(r => r.Room.GetRoomPlrDto(r)).ToArray()));
          plr.Room?.Broadcast(new RoomEnterClubInfoAckMessage(plr.Map<Player, PlayerClubInfoDto>()));
        }
      }
    }

    public static void LogOff(Player plr, bool noRooms = false)
    {
      plr.Club?.Broadcast(new ClubSystemMessageMessage(plr.Account.Id, $"<Chat Key =\"1\"Cnt =\"2\"Param1=\"{plr.Account.Nickname}\"Param2=\"2\" />"));
      plr.Club?.Broadcast(new ClubMemberLoginStateAckMessage(0, plr.Account.Id));

      if (!noRooms)
      {
        plr.Room?.Broadcast(new RoomPlayerInfoListForEnterPlayerAckMessage(plr.Room.TeamManager.Players
            .Select(r => r.Room.GetRoomPlrDto(r)).ToArray()));
        plr.Room?.Broadcast(new RoomEnterClubInfoAckMessage(plr.Map<Player, PlayerClubInfoDto>()));
      }
    }

    public void Broadcast(IClubMessage message)
    {
      foreach (var member in GameServer.Instance.PlayerManager.Where(x => x.Club == this))
        member.Session?.SendAsync(message);
    }

    public void Broadcast(IGameMessage message)
    {
      foreach (var member in GameServer.Instance.PlayerManager.Where(x => x.Club == this))
        member.Session?.SendAsync(message);
    }

    public void Broadcast(IChatMessage message)
    {
      foreach (var member in GameServer.Instance.PlayerManager.Where(x => x.Club == this))
        member.ChatSession?.SendAsync(message);
    }
  }
}