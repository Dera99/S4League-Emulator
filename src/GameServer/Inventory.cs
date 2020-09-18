using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using BlubLib.Collections.Concurrent;
using Dapper.FastCrud;
using ExpressMapper.Extensions;
using NeoNetsphere.Database.Game;
using NeoNetsphere.Network;
using NeoNetsphere.Network.Data.Game;
using NeoNetsphere.Network.Message.Game;
using NeoNetsphere.Resource;
using NeoNetsphere.Shop;


namespace NeoNetsphere
{
  internal class Inventory : IReadOnlyCollection<PlayerItem>
  {
    private readonly ConcurrentDictionary<ulong, PlayerItem> _items = new ConcurrentDictionary<ulong, PlayerItem>();
    private readonly ConcurrentStack<PlayerItem> _itemsToDelete = new ConcurrentStack<PlayerItem>();

    internal Inventory(Player plr, PlayerDto dto)
    {
      Player = plr;

      foreach (var item in dto.Items.Select(i => new PlayerItem(this, i)))
      {
        if (!item.IsInvalid)
          _items.TryAdd(item.Id, item);
        else if (item.ExpireDate != 0)
          _items.TryAdd(item.Id, item);
        else
          _itemsToDelete.Push(item);
      }
    }

    public Player Player { get; }

    /// <summary>
    ///     Returns the item with the given id or null if not found
    /// </summary>
    public PlayerItem this[ulong id] => GetItem(id);

    public int Count => _items.Count;

    public IEnumerator<PlayerItem> GetEnumerator()
    {
      return _items.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
      return GetEnumerator();
    }

    /// <summary>
    ///     Returns the item with the given id or null if not found
    /// </summary>
    public PlayerItem GetItem(ulong id)
    {
      PlayerItem item;
      _items.TryGetValue(id, out item);
      return item;
    }

    /// <summary>
    ///     Returns the item with the given id or null if not found
    /// </summary>
    public PlayerItem GetItemByShopInfoId(uint id)
    {
      try
      {
        var item = _items.Values.Where(item_ => item_.GetShopItemInfo().Id == id).ToList();
        if (item.Count < 1)
          return null;

        return item.LastOrDefault();
      }
      catch (Exception ex)
      {
        return null;
      }
    }

    /// <summary>
    ///     Creates a new item
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public PlayerItem Create(ItemNumber itemNumber, ItemPriceType priceType, ItemPeriodType periodType,
        ushort period, byte color, EffectNumber[] effects, uint count)
    {
      var shop = GameServer.Instance.ResourceCache.GetShop();

      var shopItemInfo = shop.GetItemInfo(itemNumber, priceType);
      if (shopItemInfo == null)
        throw new ArgumentException($"Item not found : {itemNumber.Id}");

      var price = shopItemInfo.PriceGroup.GetPrice(periodType, period);
      if (price == null)
        throw new ArgumentException($"Price not found : {priceType}");
      return Create(shopItemInfo, price, color, effects, count);
    }

    /// <summary>
    ///     Creates a new item
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public PlayerItem Create(ItemNumber itemNumber,
        ushort period, byte color, EffectNumber[] effects, uint count)
    {
      var shop = GameServer.Instance.ResourceCache.GetShop();

      var shopItemInfo = shop.GetFirstItemInfo(itemNumber);
      if (shopItemInfo == null)
        throw new ArgumentException($"Item not found : {itemNumber.Id}");

      var itemEffects = new List<EffectNumber>();
      foreach (var effect in shopItemInfo.EffectGroup.Effects)
      {
        itemEffects.Add(effect.Effect);
      }

      var priceType = shopItemInfo.PriceGroup.PriceType;
      var periodType = shopItemInfo.PriceGroup.Prices.FirstOrDefault().PeriodType;
      var periodNr = shopItemInfo.PriceGroup.Prices.FirstOrDefault().Period;
      return Create(itemNumber, priceType, periodType, periodNr, color, itemEffects.ToArray(), count);
    }

    /// <summary>
    ///     Creates a new item
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public PlayerItem CreateSilent(ItemNumber itemNumber,
        ushort period, byte color, uint count)
    {
      var shop = GameServer.Instance.ResourceCache.GetShop();

      var shopItemInfo = shop.GetFirstItemInfo(itemNumber);
      if (shopItemInfo == null)
        throw new ArgumentException($"Item not found : {itemNumber.Id}");

      if (shopItemInfo == null)
        throw new ArgumentException($"Item not found : {itemNumber.Id}");

      var price = shopItemInfo.PriceGroup.Prices.FirstOrDefault();
      if (price == null)
        throw new ArgumentException($"Item has no price");

      var effects = shopItemInfo.EffectGroup.Effects.Select(x => (EffectNumber)x.Effect).ToArray();
      return CreateSilent(shopItemInfo, price, color, effects, count);
    }

    /// <summary>
    ///     Creates a new item
    /// </summary>
    /// <exception cref="CharacterException"></exception>
    public PlayerItem Create(ShopItemInfo shopItemInfo, ShopPrice price, byte color, EffectNumber[] effects,
        uint count)
    {
      if (effects.Length == 0)
        effects = new EffectNumber[] { 0 };
      var item = new PlayerItem(this, shopItemInfo, price, color, effects, DateTimeOffset.Now, count);
      _items.TryAdd(item.Id, item);
      Player.Session.SendAsync(
          new ItemUpdateInventoryAckMessage(InventoryAction.Add, item.Map<PlayerItem, ItemDto>()));
      return item;
    }

    /// <summary>
    ///     Creates a new item
    /// </summary>
    /// <exception cref="CharacterException"></exception>
    public PlayerItem CreateSilent(ShopItemInfo shopItemInfo, ShopPrice price, byte color, EffectNumber[] effects,
        uint count)
    {
      if (effects.Length == 0)
        effects = new EffectNumber[] { 0 };

      var item = new PlayerItem(this, shopItemInfo, price, color, effects, DateTimeOffset.Now, count);
      _items.TryAdd(item.Id, item);
      return item;
    }

    /// <summary>
    ///     Removes the item from the inventory
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public void Remove(PlayerItem item)
    {
      Remove(item.Id);
    }

    /// <summary>
    ///     Removes or decreases the count of the item from/in the inventory
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public void RemoveOrDecrease(PlayerItem item)
    {
      if (item.PeriodType == ItemPeriodType.Units)
      {
        item.Count--;
        if (item.Count <= 0)
        {
          Remove(item.Id);
        }
        else
        {
          Player.SendAsync(new ItemUpdateInventoryAckMessage(InventoryAction.Update,
              item.Map<PlayerItem, ItemDto>()));
        }
      }
      else
      {
        Remove(item.Id);
      }
    }

    /// <summary>
    ///     Removes the item from the inventory
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public void Remove(ulong id)
    {
      var item = GetItem(id);
      if (item == null)
        throw new ArgumentException($"Item {id} not found", nameof(id));

      _items.Remove(item.Id);
      if (item.ExistsInDatabase)
        _itemsToDelete.Push(item);

      Player.Session.SendAsync(new ItemInventroyDeleteAckMessage(item.Id));
    }

    public void RemoveInvalid(PlayerItem item)
    {
      _items.Remove(item.Id);
      if (item.ExistsInDatabase)
        _itemsToDelete.Push(item);
    }

    internal void Save(IDbConnection db)
    {
      if (Player.Room == null)
      {
        var ExpireItems = (from it in _items
                           where it.Value.ExpireDate == 0
                           select it.Value).ToList();

        foreach (var it in ExpireItems)
          Remove(it);
      }
      if (!_itemsToDelete.IsEmpty)
      {
        var idsToRemove = new StringBuilder();
        var firstRun = true;
        PlayerItem itemToDelete;
        while (_itemsToDelete.TryPop(out itemToDelete))
        {
          if (firstRun)
            firstRun = false;
          else
            idsToRemove.Append(',');
          idsToRemove.Append(itemToDelete.Id);
        }

        DbUtil.BulkDelete<PlayerItemDto>(db, statement => statement
            .Where($"{nameof(PlayerItemDto.Id):C} IN ({idsToRemove})"));
      }

      foreach (var item in _items.Values)
      {
        var rawEffects = item.Effects.ToList();
        var dtoEffects = "";
        try
        {
          dtoEffects = string.Join(",", rawEffects);
        }
        catch (Exception ex)
        {
          dtoEffects = "0";
        }

        if (!item.ExistsInDatabase)
        {
          DbUtil.Insert(db, new PlayerItemDto
          {
            Id = (int)item.Id,
            PlayerId = (int)Player.Account.Id,
            ShopItemInfoId = item.GetShopItemInfo().Id,
            ShopPriceId = item.GetShopItemInfo().PriceGroup.GetPrice(item.PeriodType, item.Period).Id,
            DaysLeft = item.Period,
            Period = item.Period,
            Effects = dtoEffects,
            Color = item.Color,
            PurchaseDate = item.PurchaseDate.ToUnixTimeSeconds(),
            Durability = item.Durability,
            Count = (int)item.Count,
            EnchantMP = 0,
            EnchantLvl = 0
          });
          item.ExistsInDatabase = true;
        }
        else
        {
          if (!item.NeedsToSave)
            continue;

          DbUtil.Update(db, new PlayerItemDto
          {
            Id = (int)item.Id,
            PlayerId = (int)Player.Account.Id,
            ShopItemInfoId = item.GetShopItemInfo().Id,
            ShopPriceId = item.GetShopPrice().Id,
            Period = item.Period,
            DaysLeft = item.DaysLeft,
            Effects = dtoEffects,
            Color = item.Color,
            PurchaseDate = item.PurchaseDate.ToUnixTimeSeconds(),
            Durability = item.Durability,
            Count = (int)item.Count,
            EnchantLvl = item.EnchantLvl,
            EnchantMP = item.EnchantMP
          });
          item.NeedsToSave = false;
        }
      }
    }

    public bool Contains(ulong id)
    {
      return _items.ContainsKey(id);
    }


        internal void Update(PlayerItem item)
        {
            if (item == null)
            {
                throw new ArgumentException(string.Format("{0} Not Found", item));
            }
            Player.Session.SendAsync(new ItemUpdateInventoryAckMessage(InventoryAction.Update, item.Map<PlayerItem, ItemDto>()));
            return;
        }
    }
}