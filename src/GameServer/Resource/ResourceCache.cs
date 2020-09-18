using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlubLib.Caching;
using Dapper.FastCrud;
using NeoNetsphere.Database.Auth;
using NeoNetsphere.Database.Game;
using NeoNetsphere.Network;
using Serilog;
using Serilog.Core;

namespace NeoNetsphere.Resource
{
  internal class ResourceCache
  {
    // ReSharper disable once InconsistentNaming
    private static readonly ILogger Logger =
        Log.ForContext(Constants.SourceContextPropertyName, nameof(ResourceCache));

    private readonly ICache _cache = new MemoryCache();
    public readonly ResourceLoader _loader;

    public ResourceCache()
    {
      var path = AppDomain.CurrentDomain.BaseDirectory;
      path = Path.Combine(path, "data");
      _loader = new ResourceLoader(path);
    }

        public void PreCache()
        {
            Logger.Information("Caching: Effects");
            GetEffects();
            Logger.Information("Cached Effects");

            Logger.Information("Caching: Items");
            GetItems();
            Logger.Information("Cached Items");

            Logger.Information("Caching: DefaultItems");
            GetDefaultItems();
            Logger.Information("Cached DefaultItems");

            Logger.Information("Caching: Shop");
            GetShop();
            Logger.Information("Cached Shop");

            Logger.Information("Caching: Experience");
            GetExperience();
            Logger.Information("Cached Experience");

            Logger.Information("Caching: Maps");
            GetMaps();
            Logger.Information("Cached Maps");

            Logger.Information("Caching: GameTempos");
            GetGameTempos();
            Logger.Information("Cached GameTempos");

            Logger.Information("Caching: Capsules");
            GetCapsules();
            Logger.Information("Cached Capsules");

            Logger.Information("Caching: Enchant Effects");
            GetEnchantSystem();
            Logger.Information("Cached Enchant Effects");

            Logger.Information("Caching: LevelRewards");
            GetLevelRewards();
            Logger.Information("Cached LevelRewards");

        }

        public IReadOnlyList<ChannelDto> GetChannels()
        {
            var value = _cache.Get<IReadOnlyList<ChannelDto>>(ResourceCacheType.Channels);
            if (value == null)
            {
                Logger.Information("Caching: Channels");

                using (var db = GameDatabase.Open())
                {
                    value = DbUtil.Find<ChannelDto>(db).ToList();
                }

                _cache.Set(ResourceCacheType.Channels, value);
            }
            Logger.Information("Cached Channels");
            return value;
        }

        public IReadOnlyList<DBClubInfoDto> GetClubs()
        {
            var value = _cache.Get<IReadOnlyList<DBClubInfoDto>>(ResourceCacheType.Clubs);
            if (value == null)
            {
                Logger.Information("Caching: Clubs");

                using (var db = GameDatabase.Open())
                {
                    var clubs = DbUtil.Find<ClubDto>(db).ToList();
                    var clubPlayers = DbUtil.Find<ClubPlayerDto>(db).ToList();

                    var dbClubInfoList = new List<DBClubInfoDto>();
                    foreach (var clubDto in clubs)
                    {
                        var clubInfo = new DBClubInfoDto { ClubDto = clubDto };
                        var dbPlayerInfoList = new List<ClubPlayerInfo>();
                        foreach (var playerInfoDto in clubPlayers.Where(p => p.ClubId == clubDto.Id))
                        {
                            using (var dbC = AuthDatabase.Open())
                            {
                                var account = DbUtil.Find<AccountDto>(dbC, statement => statement
                                        .Where($"{nameof(AccountDto.Id):C} = @{nameof(playerInfoDto.PlayerId)}")
                                        .WithParameters(new { playerInfoDto.PlayerId }))
                                    .FirstOrDefault();

                                dbPlayerInfoList.Add(new ClubPlayerInfo
                                {
                                    AccountId = (ulong)playerInfoDto.PlayerId,
                                    State = (ClubState)playerInfoDto.State,
                                    Rank = (ClubRank)playerInfoDto.Rank,
                                    Account = account
                                });
                            }
                        }

                        clubInfo.PlayerDto = dbPlayerInfoList.ToArray();
                        dbClubInfoList.Add(clubInfo);
                    }

                    value = dbClubInfoList.ToArray();
                }

                _cache.Set(ResourceCacheType.Clubs, value);
            }
            Logger.Information("Cached Clubs");
            return value;
        }

        public IReadOnlyDictionary<int, LevelReward> GetLevelRewards()
        {
            var value = _cache.Get<IReadOnlyDictionary<int, LevelReward>>(ResourceCacheType.LevelRewards);
            if (value == null)
            {
                value = _loader.LoadLevelRewards().ToDictionary(t => t.Level);
                _cache.Set(ResourceCacheType.GameTempo, value);
            }

            return value;
        }

        public IReadOnlyDictionary<uint, EnchantSys> GetEnchantSystem()
        {
              IReadOnlyDictionary<uint, EnchantSys> readOnlyDictionary = _cache.Get<IReadOnlyDictionary<uint, EnchantSys>>(ResourceCacheType.EnchantSystem);
            if (readOnlyDictionary == null)
            {
                readOnlyDictionary = _loader.LoadEnchantSystem().ToDictionary((Func<EnchantSys, uint>)((EnchantSys t) => t.Level));
                _cache.Set(ResourceCacheType.EnchantSystem, readOnlyDictionary);
            }

            return readOnlyDictionary;
        }

        public IReadOnlyDictionary<uint, ItemEffect> GetEffects()
    {
      var value = _cache.Get<IReadOnlyDictionary<uint, ItemEffect>>(ResourceCacheType.Effects);
      if (value == null)
      {
        
        value = _loader.LoadEffects().ToDictionary(effect => effect.Id);
        _cache.Set(ResourceCacheType.Effects, value);
      }

      return value;
    }

    public IReadOnlyDictionary<ItemNumber, ItemInfo> GetItems()
    {
      var value = _cache.Get<IReadOnlyDictionary<ItemNumber, ItemInfo>>(ResourceCacheType.Items);
      if (value == null)
      {
        
        value = _loader.LoadItems_3().ToDictionary(item => item.ItemNumber);
        _cache.Set(ResourceCacheType.Items, value);
      }
      return value;
    }
        
    public IReadOnlyList<DefaultItem> GetDefaultItems()
    {
      var value = _cache.Get<IReadOnlyList<DefaultItem>>(ResourceCacheType.DefaultItems);
      if (value == null)
      {
        
        value = _loader.LoadDefaultItems().ToList();
        _cache.Set(ResourceCacheType.DefaultItems, value);
      }

      return value;
    }

    public ShopResources GetShop()
    {
      var value = _cache.Get<ShopResources>(ResourceCacheType.Shop);
      if (value == null)
      {
        
        value = new ShopResources();
        _cache.Set(ResourceCacheType.Shop, value);
      }

      if (string.IsNullOrWhiteSpace(value.Version))
        value.Load();

      return value;
    }

    public IReadOnlyDictionary<int, Experience> GetExperience()
    {
      var value = _cache.Get<IReadOnlyDictionary<int, Experience>>(ResourceCacheType.Exp);
      if (value == null)
      {
        
        value = _loader.LoadExperience().ToDictionary(e => e.Level);
        _cache.Set(ResourceCacheType.Exp, value);
      }

      return value;
    }

    public IReadOnlyDictionary<int, MapInfo> GetMaps()
    {
      var value = _cache.Get<IReadOnlyDictionary<int, MapInfo>>(ResourceCacheType.Maps);
      if (value == null)
      {
        
        value = _loader.LoadMaps().ToDictionary(map => map.Id);
        _cache.Set(ResourceCacheType.Maps, value);
      }

      return value;
    }

    public IReadOnlyDictionary<string, GameTempo> GetGameTempos()
    {
      var value = _cache.Get<IReadOnlyDictionary<string, GameTempo>>(ResourceCacheType.GameTempo);
      if (value == null)
      {
        

        value = _loader.LoadGameTempos().ToDictionary(t => t.Name);
        _cache.Set(ResourceCacheType.GameTempo, value);
      }

      return value;
    }

    public IReadOnlyDictionary<ItemNumber, AddCapsule> GetCapsules()
    {
      var value = _cache.Get<IReadOnlyDictionary<ItemNumber, AddCapsule>>(ResourceCacheType.Capsules);
      if (value == null)
      {
        value = _loader.LoadCapsules().ToDictionary(t => t.CapsuleItemId);
        _cache.Set(ResourceCacheType.Capsules, value);
      }

      return value;
    }

    public IReadOnlyDictionary<ulong, CapsuleRewards> GetItemRewards()
    {
      var value = _cache.Get<IReadOnlyDictionary<ulong, CapsuleRewards>>(ResourceCacheType.ItemRewards);
      if (value == null)
      {
        

        value = _loader.LoadItemRewards().ToDictionary(t => (ulong)t.Item);
        _cache.Set(ResourceCacheType.ItemRewards, value);
      }

      return value;
    }
    public void Clear()
    {
      Logger.Information("Clearing cache");
      _cache.Clear();
    }

    public void Clear(ResourceCacheType type)
    {
      Logger.Information($"Clearing cache for {type}");

      if (type == ResourceCacheType.Shop)
      {
        GetShop().Clear();
        return;
      }

      _cache.Remove(type.ToString());
    }
  }

  internal static class ResourceCacheExtensions
  {
    public static T Get<T>(this ICache cache, ResourceCacheType type)
        where T : class
    {
      return cache.Get<T>(type.ToString());
    }

    public static void Set(this ICache cache, ResourceCacheType type, object value)
    {
      cache.Set(type.ToString(), value);
    }

    public static void Set(this ICache cache, ResourceCacheType type, object value, TimeSpan ts)
    {
      cache.Set(type.ToString(), value, ts);
    }
  }
}