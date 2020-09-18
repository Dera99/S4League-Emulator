using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlubLib.IO;
using Dapper.FastCrud;
using NeoNetsphere.Database.Game;
using NeoNetsphere.Shop;

namespace NeoNetsphere.Resource
{
  internal class ShopResources
  {
    private Dictionary<int, ShopEffectGroup> _effects;
    private Dictionary<ItemNumber, ShopItem> _items;
    private Dictionary<int, ShopPriceGroup> _prices;

    public IReadOnlyDictionary<ItemNumber, ShopItem> Items => _items;

    public IReadOnlyDictionary<int, ShopEffectGroup> Effects => _effects;

    public IReadOnlyDictionary<int, ShopPriceGroup> Prices => _prices;

    public byte[] ShopPrices = Array.Empty<byte>();
    public byte[] ShopEffects = Array.Empty<byte>();
    public byte[] ShopItems = Array.Empty<byte>();

    public string Version { get; private set; }

    public void Load()
    {
      using (var db = GameDatabase.Open())
      {
        _effects = DbUtil.Find<ShopEffectGroupDto>(db, statement => statement
                .Include<ShopEffectDto>(join => join.LeftOuterJoin()))
            .ToArray()
            .Select(dto => new ShopEffectGroup(dto))
            .ToDictionary(x => x.Id);

        _prices = DbUtil.Find<ShopPriceGroupDto>(db, statement => statement
                .Include<ShopPriceDto>(join => join.LeftOuterJoin()))
            .ToArray()
            .Select(dto => new ShopPriceGroup(dto))
            .ToDictionary(x => x.Id);

        _items = DbUtil.Find<ShopItemDto>(db, statement => statement
                .Include<ShopItemInfoDto>(join => join.LeftOuterJoin()))
            .ToArray()
            .Select(dto => new ShopItem(dto, this))
            .ToDictionary(x => x.ItemNumber);

        using (var w = new BinaryWriter(new MemoryStream()))
        {
          w.Serialize(Prices.Values.ToArray());
          ShopPrices = w.ToArray();
        }

        using (var w = new BinaryWriter(new MemoryStream()))
        {
          w.Serialize(Effects.Values.ToArray());
          ShopEffects = w.ToArray();
        }

        using (var w = new BinaryWriter(new MemoryStream()))
        {
          w.Serialize(Items.Values.ToArray());
          ShopItems = w.ToArray();
        }

        Version = DbUtil.Find<ShopVersionDto>(db).First().Version;
      }
    }

    public void Clear()
    {
      _items.Clear();
      _effects.Clear();
      _prices.Clear();
      ShopPrices = Array.Empty<byte>();
      ShopEffects = Array.Empty<byte>();
      ShopItems = Array.Empty<byte>();
      Version = "";
    }

    public ShopItem GetItem(ItemNumber itemNumber)
    {
      var shopItem = _items.FirstOrDefault(x => x.Key.Id == itemNumber.Id).Value;
      return shopItem;
    }

    public ShopItemInfo GetItemInfo(ItemNumber itemNumber, ItemPriceType priceType)
    {
      var item = GetItem(itemNumber);
      return item?.GetItemInfo(priceType);
    }

    public ShopItemInfo GetFirstItemInfo(ItemNumber itemNumber)
    {
      var firstPrice = GetFirstPrice(itemNumber);
      var item = GetItem(itemNumber);
      return item?.GetItemInfo(firstPrice);
    }

    public ShopItemInfo GetItemInfo(PlayerItem item)
    {
      return GetItemInfo(item.ItemNumber, item.PriceType);
    }

    public ShopPrice GetPrice(ItemNumber itemNumber, ItemPriceType priceType, ItemPeriodType periodType,
        ushort period)
    {
      var itemInfo = GetItemInfo(itemNumber, priceType);
      return itemInfo?.PriceGroup.GetPrice(periodType, period);
    }

    public ItemPriceType GetFirstPrice(ItemNumber itemNumber)
    {
      var item = GetItem(itemNumber);
      var iteminfo = item.ItemInfos.FirstOrDefault(x => x.PriceGroup.PriceType != ItemPriceType.None);

      if (iteminfo != null) return iteminfo.PriceGroup.PriceType;

      return ItemPriceType.None;
    }

    public ShopPrice GetPrice(PlayerItem item)
    {
      return GetPrice(item.ItemNumber, item.PriceType, item.PeriodType, item.Period);
    }
  }
}