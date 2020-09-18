using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExpressMapper.Extensions;
using NeoNetsphere.Database.Game;
using NeoNetsphere.Network;
using NeoNetsphere.Network.Data.Game;
using NeoNetsphere.Network.Message.Game;
using NeoNetsphere.Resource;
using NeoNetsphere.Shop;

namespace NeoNetsphere
{
    internal class PlayerItem
    {
        internal bool IsInvalid = false;

        private uint _count;
        private int _durability = 2400;
        private uint _enchantMP;
        private byte _enchantLvl;

        internal PlayerItem(Inventory inventory, PlayerItemDto dto)
        {
            var shop = GameServer.Instance.ResourceCache.GetShop();
            ExistsInDatabase = true;
            Inventory = inventory;
            Id = (ulong)dto.Id;

            var itemInfo = shop.Items.Values.FirstOrDefault(group => group.GetItemInfo(dto.ShopItemInfoId) != null);
            if (itemInfo == null)
            {
                IsInvalid = true;
                Inventory.RemoveInvalid(this);
                return;
            }

            ItemNumber = itemInfo.ItemNumber;

            var priceGroup = shop.Prices.Values.FirstOrDefault(group => group.GetPrice(dto.ShopPriceId) != null);
            if (priceGroup == null)
            {
                IsInvalid = true;
                Inventory.RemoveInvalid(this);
                return;
            }

            var price = priceGroup.GetPrice(dto.ShopPriceId);

            PriceType = priceGroup.PriceType;
            PeriodType = price.PeriodType;
            Period = price.Period;
            DaysLeft = (ushort)dto.DaysLeft;
            Color = dto.Color;

            var raweffects = new List<EffectNumber>();
            var effectsText = dto.Effects.Split(",").ToList();
            effectsText.ForEach(eff => { raweffects.Add(new EffectNumber(uint.Parse(eff))); });
            Effects = raweffects.ToArray();
            if (Effects.Length == 0)
                Effects = new EffectNumber[] { 0 };
            _durability = dto.Durability;
            _count = (uint)dto.Count;
            if (_count == 0)
                _count = 1;
            _enchantMP = dto.EnchantMP;
            _enchantLvl = dto.EnchantLvl;
            PurchaseDate = DateTimeOffset.FromUnixTimeSeconds(dto.PurchaseDate);
        }

        internal PlayerItem(Inventory inventory, ShopItemInfo itemInfo, ShopPrice price, byte color,
            EffectNumber[] effects,
            DateTimeOffset purchaseDate, uint count)
        {
            Inventory = inventory;
            Id = ItemIdGenerator.GetNextId();
            ItemNumber = itemInfo.ShopItem.ItemNumber;
            PriceType = itemInfo.PriceGroup.PriceType;
            PeriodType = price.PeriodType;
            Period = price.Period;
            DaysLeft = price.Period;
            Color = color;
            Effects = effects;
            PurchaseDate = purchaseDate;
            _durability = price.Durability;
            _count = count;
        }

        internal bool ExistsInDatabase { get; set; }
        internal bool NeedsToSave { get; set; }

        public Inventory Inventory { get; }

        public ulong Id { get; }
        public ItemNumber ItemNumber { get; }
        public ItemPriceType PriceType { get; }
        public ItemPeriodType PeriodType { get; }
        public ushort Period { get; set; }
        public ushort DaysLeft { get; set; }
        public byte Color { get; }
        public EffectNumber[] Effects { get; set; }
        public DateTimeOffset PurchaseDate { get; }
        public int DurabilityLoss { get; set; }

        public uint EnchantMP
        {
            get => _enchantMP;
            set
            {
                if (_enchantMP == value)
                    return;
                _enchantMP = value;
                NeedsToSave = true;
            }
        }

        public byte EnchantLvl
        {
            get => _enchantLvl;
            set
            {
                if (_enchantLvl == value)
                    return;
                _enchantLvl = value;
                NeedsToSave = true;
            }
        }

        public int Durability
        {
            get => _durability;
            set
            {
                if (_durability == value)
                    return;
                _durability = value;
                NeedsToSave = true;
            }
        }

        public uint Count
        {
            get => _count;
            set
            {
                if (_count == value)
                    return;
                _count = value;
                NeedsToSave = true;
            }
        }

        public DateTimeOffset CalculateExpireTime() => PeriodType == ItemPeriodType.Days ? PurchaseDate.AddDays(DaysLeft) : DateTimeOffset.MinValue;
        public long ExpireDate => PeriodType == ItemPeriodType.Days ? Expire() : -1;

        private long Expire()
        {
            switch (PeriodType)
            {
                case ItemPeriodType.None:
                    return uint.MaxValue;

                case ItemPeriodType.Days:
                    {
                        var left = PurchaseDate.AddDays(DaysLeft) - DateTime.Now;
                        if (left.Seconds > 0)
                            return (long)left.TotalSeconds;
                    }
                    return 0;
            }
            return 0;
        }

        public EffectNumber[] GetItemEffects()
        {
            if (Effects.Length == 0)
                return null;

            var effects = GameServer.Instance.ResourceCache.GetEffects();
            
            var ret_effects = new List<EffectNumber>();
            foreach (var eff in Effects)
                ret_effects.Add(effects.GetValueOrDefault(eff).Id);

            return ret_effects.ToArray();
        }

        public ShopItem GetShopItem()
        {
            var shop = GameServer.Instance.ResourceCache.GetShop();
            return shop.GetItem(ItemNumber);
        }

        public ShopItemInfo GetShopItemInfo()
        {
            var shop = GameServer.Instance.ResourceCache.GetShop();
            return shop.GetItemInfo(ItemNumber, PriceType);
        }

        public ShopPrice GetShopPrice()
        {
            return GetShopItemInfo().PriceGroup.GetPrice(PeriodType, Period);
        }

        public ItemDurabilityInfoDto LoseDurability(int loss)
        {
            if (loss < 0)
                throw new ArgumentOutOfRangeException(nameof(loss));

            if (Inventory.Player.Room == null)
                throw new InvalidOperationException("Player is not inside a room");

            if (Durability == -1)
            {
                DurabilityLoss = 0;
            }
            else
            {
                Durability -= loss;
                DurabilityLoss = loss;
                if (Durability < 0)
                    Durability = 0;
            }

            return this.Map<PlayerItem, ItemDurabilityInfoDto>();
        }

        public uint CalculateRefund(ShopPrice price)
        {
            if (Count == 0)
                Count = 1;
            var shopprice = price.Price * Count;

            if (PriceType == ItemPriceType.Premium || PriceType == ItemPriceType.AP)
                return (uint)shopprice;
            return (uint)(shopprice * 0.25);
        }

        public uint CalculateRepair()
        {
            if (PeriodType != ItemPeriodType.None)
                return 0; // Todo

            var price = GetShopPrice();
            var life = 1.0 - (Durability / price.Durability);
            var repair = price.Price * 0.2 * life;
            return (uint)repair;
        }


    }
}