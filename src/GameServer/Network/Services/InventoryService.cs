using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BlubLib.DotNetty.Handlers.MessageHandling;
using ExpressMapper.Extensions;
using NeoNetsphere.Network.Data.Game;
using NeoNetsphere.Network.Message.Game;
using NeoNetsphere.Resource;
using ProudNetSrc.Handlers;
using Serilog;
using Serilog.Core;
using NeoNetsphere.Database.Game;
using Dapper.FastCrud;

namespace NeoNetsphere.Network.Services
{
    internal class InventoryService : ProudMessageHandler
    {
        // ReSharper disable once InconsistentNaming
        private static readonly ILogger Logger =
            Log.ForContext(Constants.SourceContextPropertyName, nameof(InventoryService));

        [MessageHandler(typeof(ItemUseItemReqMessage))]
        public void UseItemHandler(GameSession session, ItemUseItemReqMessage message)
        {
            // This is a weird thing since newer seasons
            // The client sends a request with itemid 0 on login
            // and requires an answer to it for equipment to work properly
            if (message.Action == UseItemAction.UnEquip && message.ItemId == 0)
            {
                session.SendAsync(new ItemUseItemAckMessage(message.Action, message.CharacterSlot, message.EquipSlot, message.ItemId));
                return;
            }

            var plr = session.Player;
            var @char = plr.CharacterManager[message.CharacterSlot];
            var item = plr.Inventory[message.ItemId];

            if (@char == null || item == null || plr.Room != null && plr.RoomInfo.State != PlayerState.Lobby)
            {
                session.SendAsync(new ItemUseItemAckMessage(UseItemAction.NoAction, message.CharacterSlot,   message.EquipSlot, message.ItemId));
                return;
            }

            try
            {
                switch (message.Action)
                {
                    case UseItemAction.Equip:
                        @char.Equip(item, message.EquipSlot);
                        break;

                    case UseItemAction.UnEquip:
                        @char.UnEquip(item.ItemNumber.Category, message.EquipSlot);
                        break;
                }
            }
            catch (CharacterException ex)
            {
                Logger.ForAccount(session)
                    .Error(ex.Message, "Unable to use item");
                session.SendAsync(new ItemUseItemAckMessage(UseItemAction.NoAction, message.CharacterSlot,
                    message.EquipSlot,
                    message.ItemId));
                session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
            }
        }

        [MessageHandler(typeof(ItemRepairItemReqMessage))]
        public void RepairItemHandler(GameSession session, ItemRepairItemReqMessage message)
        {
            var shop = GameServer.Instance.ResourceCache.GetShop();

            foreach (var id in message.Items)
            {
                var item = session.Player.Inventory[id];
                if (item == null)
                {
                    Logger.ForAccount(session)
                        .Error("Item {id} not found", id);
                    session.SendAsync(new ItemRepairItemAckMessage { Result = ItemRepairResult.Error0 });
                    return;
                }

                if (item.Durability == -1)
                {
                    Logger.ForAccount(session)
                        .Error("Item {item} can not be repaired",
                            new { item.ItemNumber, item.PriceType, item.PeriodType, item.Period });
                    session.SendAsync(new ItemRepairItemAckMessage { Result = ItemRepairResult.Error1 });
                    return;
                }

                var cost = item.CalculateRepair();
                if (session.Player.PEN < cost)
                {
                    session.SendAsync(new ItemRepairItemAckMessage { Result = ItemRepairResult.NotEnoughMoney });
                    return;
                }

                var price = shop.GetPrice(item);
                if (price == null)
                {
                    Logger.ForAccount(session)
                        .Error("No shop entry found for {item}",
                            new { item.ItemNumber, item.PriceType, item.PeriodType, item.Period });
                    session.SendAsync(new ItemRepairItemAckMessage { Result = ItemRepairResult.Error4 });
                    return;
                }

                if (item.Durability >= price.Durability)
                {
                    session.SendAsync(new ItemRepairItemAckMessage
                    {
                        Result = ItemRepairResult.OK,
                        ItemId = item.Id
                    });
                    continue;
                }

                item.Durability = price.Durability;
                session.Player.PEN -= cost;

                session.SendAsync(new ItemRepairItemAckMessage { Result = ItemRepairResult.OK, ItemId = item.Id });
                session.SendAsync(
                    new MoneyRefreshCashInfoAckMessage { PEN = session.Player.PEN, AP = session.Player.AP });
            }
        }

        [MessageHandler(typeof(ItemRefundItemReqMessage))]
        public void RefundItemHandler(GameSession session, ItemRefundItemReqMessage message)
        {
            var shop = GameServer.Instance.ResourceCache.GetShop();

            var item = session.Player.Inventory[message.ItemId];
            if (item == null)
            {
                Logger.ForAccount(session)
                    .Error("Item {itemId} not found", message.ItemId);
                session.SendAsync(new ItemRefundItemAckMessage { Result = ItemRefundResult.Failed });
                return;
            }

            var price = shop.GetPrice(item);
            if (price == null)
            {
                Logger.ForAccount(session)
                    .Error("No shop entry found for {item}",
                        new { item.ItemNumber, item.PriceType, item.PeriodType, item.Period });
                session.SendAsync(new ItemRefundItemAckMessage { Result = ItemRefundResult.Failed });
                return;
            }

            if (!price.CanRefund)
            {
                Logger.ForAccount(session)
                    .Error("Cannot refund {item}", new { item.ItemNumber, item.PriceType, item.PeriodType, item.Period });
                session.SendAsync(new ItemRefundItemAckMessage { Result = ItemRefundResult.Failed });
                return;
            }

            session.Player.PEN += item.CalculateRefund(price);
            session.Player.Inventory.Remove(item);

            session.SendAsync(new ItemRefundItemAckMessage { Result = ItemRefundResult.OK, ItemId = item.Id });
            session.SendAsync(new MoneyRefreshCashInfoAckMessage { PEN = session.Player.PEN, AP = session.Player.AP });
        }

        [MessageHandler(typeof(ItemDiscardItemReqMessage))]
        public void DiscardItemHandler(GameSession session, ItemDiscardItemReqMessage message)
        {
            var shop = GameServer.Instance.ResourceCache.GetShop();

            var item = session.Player.Inventory[message.ItemId];
            if (item == null)
            {
                Logger.ForAccount(session)
                    .Error("Item {itemId} not found", message.ItemId);
                session.SendAsync(new ItemDiscardItemAckMessage { Result = 2 });
                return;
            }

            var shopItem = shop.GetItem(item.ItemNumber);
            if (shopItem == null)
            {
                Logger.ForAccount(session)
                    .Error("No shop entry found for item {item}",
                        new { item.ItemNumber, item.PriceType, item.PeriodType, item.Period });
                session.SendAsync(new ItemDiscardItemAckMessage { Result = 2 });
                return;
            }

            if (!shopItem.IsDestroyable)
            {
                Logger.ForAccount(session)
                    .Error("Cannot discard {item}",
                        new { item.ItemNumber, item.PriceType, item.PeriodType, item.Period });
                session.SendAsync(new ItemDiscardItemAckMessage { Result = 2 });
                return;
            }

            session.Player.Inventory.Remove(item);
            session.SendAsync(new ItemDiscardItemAckMessage { Result = 0, ItemId = item.Id });
        }

     [MessageHandler(typeof(ItemUseCapsuleReqMessage))]
          public async Task ItemUseCapsule(GameSession session, ItemUseCapsuleReqMessage message)
           {
               var ItemBags = GameServer.Instance.ResourceCache.GetItemRewards();
               var shop = GameServer.Instance.ResourceCache.GetShop();

               var plr = session.Player;
               var item = plr.Inventory[message.ItemId];

               if (!ItemBags.ContainsKey(item.ItemNumber))
               {
                   await session.SendAsync(new ServerResultAckMessage(ServerResult.DBError));
                   return;
               }

               item.Count--;

               if (item.Count <= 0)
                   plr.Inventory.Remove(item);
               else
                   await session.SendAsync(new ItemUpdateInventoryAckMessage(InventoryAction.Update, item.Map<PlayerItem, ItemDto>()));

               var ItemBag = ItemBags[item.ItemNumber];
               var Rewards = (from bag in ItemBag.Bags
                               let reward = bag.Take()
                               select new CapsuleRewardDto
                               {
                                   RewardType = reward.Type,
                                   ItemNumber = reward.Item,
                                   PriceType = reward.PriceType,
                                   PeriodType = reward.PeriodType,
                                   Period = reward.Period,
                                   Color = reward.Color,
                                   PEN = reward.PEN,
                               }).ToArray();

            Dictionary<CapsuleRewardDto, EffectNumber[]> _rewards = new Dictionary<CapsuleRewardDto, EffectNumber[]>();
               foreach (var rew in Rewards)
               {
                   var info = shop.GetItemInfo(rew.ItemNumber, rew.PriceType);
                   var effects = new List<EffectNumber>();

                if (info == null && rew.RewardType != CapsuleRewardType.PEN)
                {
                    await session.SendAsync(new ItemUseCapsuleAckMessage(7));
                    item.Count++;
                    return;
                }

                   if (rew.RewardType == CapsuleRewardType.Item)
                   {
                       foreach (var effect in info.EffectGroup.Effects)
                           effects.Add(effect.Effect);
                   }

                   var reward = new CapsuleRewardDto
                   {
                       RewardType = rew.RewardType,
                       ItemNumber = rew.ItemNumber,
                       PriceType = rew.PriceType,
                       PeriodType = rew.PeriodType,
                       Effect = rew.RewardType == CapsuleRewardType.PEN ? 0 : info.EffectGroup.MainEffect,
                       Period = rew.Period,
                       Color = (byte)rew.Color,
                       PEN = rew.PEN
                   };
                   _rewards.Add(reward, effects.ToArray());
               }

               foreach (var it in _rewards)
               {
                   if (it.Key.RewardType == CapsuleRewardType.PEN)
                   {
                       plr.PEN += it.Key.PEN;
                   }
                   else
                   {
                       if (it.Key.PeriodType == ItemPeriodType.None)
                       {
                           //plr.Inventory.Create(it.Key.ItemNumber, it.Key.PriceType, it.Key.PeriodType, (ushort)it.Key.Period, it.Key.Color, null, 1);
                           plr.Inventory.Create(it.Key.ItemNumber, 0, it.Key.Color, new EffectNumber[0], 1);
                    }
                       else
                       {
                           var prev = plr.Inventory
                               .FirstOrDefault(p => p.ItemNumber == it.Key.ItemNumber
                               && p.PeriodType == it.Key.PeriodType
                               && p.PriceType == it.Key.PriceType);
                           if (prev == null || prev.ItemNumber == 0)
                           {
                            // plr.Inventory.Create(it.Key.ItemNumber, it.Key.PriceType, it.Key.PeriodType, (ushort)it.Key.Period, it.Key.Color, null, 1);
                            plr.Inventory.Create(it.Key.ItemNumber, 0, it.Key.Color, new EffectNumber[0], 1);
                        }
                        else
                           {
                               if (it.Key.PeriodType == ItemPeriodType.Units)
                                   prev.Count += it.Key.Period;
                               else
                                   prev.Period += (ushort)it.Key.Period;
                               await session.SendAsync(new ItemUpdateInventoryAckMessage(InventoryAction.Update, prev.Map<PlayerItem, ItemDto>()));
                           }
                       }
                   }
               }
               await session.SendAsync(new ItemUseCapsuleAckMessage(_rewards.Keys.ToArray(), 3));
               await session.SendAsync(new MoneyRefreshCashInfoAckMessage(plr.PEN, plr.AP));
           }

        [MessageHandler(typeof(ItemMPRefillReqMessage))]
        public void MPRefill(GameSession session, ItemMPRefillReqMessage message)
        {
            // Todo Some improvements
            Player player = session.Player;
            PlayerItem playerItem = player.Inventory[message.ItemId2];
            PlayerItem playerItem2 = player.Inventory[message.ItemId];
            uint[] array = new uint[25]
            {
                168,
                168,
                336,
                336,
                504,
                504,
                672,
                672,
                840,
                840,
                1176,
                1176,
                1512,
                1680,
                2016,
                2688,
                3360,
                4032,
                4872,
                6048,
                6048,
                6048,
                6048,
                6048,
                6048
            };
            if (playerItem.EnchantMP >= array[playerItem.EnchantLvl])
            {
                session.SendAsync(new ItemMPRefillAckMessage
                {
                    Result = 1
                });
                return;
            }
            if (playerItem == null || playerItem2 == null || player == null || playerItem.PeriodType == ItemPeriodType.Units || (int)playerItem.ItemNumber.Category > 2)
            {
                session.SendAsync(new ItemMPRefillAckMessage
                {
                    Result = 1
                });
                return;
            }
            playerItem2.Count--;
            if (playerItem2.Count == 0)
            {
                player.Inventory.Remove(playerItem2.Id);
            }
            else
            {
                session.SendAsync(new ItemUpdateInventoryAckMessage(InventoryAction.Update, playerItem2.Map<PlayerItem, ItemDto>()));
            }
            playerItem.EnchantMP += 6048;
            if (playerItem.EnchantMP > array[playerItem.EnchantLvl])
            {
                playerItem.EnchantMP = array[playerItem.EnchantLvl];
            }
            session.SendAsync(new ItemMPRefillAckMessage
            {
                Result = 0
            });
            session.SendAsync(new ItemUpdateInventoryAckMessage(InventoryAction.Update, playerItem.Map<PlayerItem, ItemDto>()));
            return;
        }

        [MessageHandler(typeof(ItemEnchantReqMessage))]
        public void ItemEnchantReq(GameSession session, ItemEnchantReqMessage message)
        {
            // Todo !! Enchant For Costumes - Only need to fill the xml containing effects and levels 
            Player player = session.Player;
            if (player == null)
            {
                return;
            }
            if (player.PEN < 200)
            {
                session.SendAsync(new EnchantItemAckMessage(EnchantResult.NotEnoughMoney));
                return;
            }
            PlayerItem item = player.Inventory[(ulong)message.ItemId];
            // Checks if item not equal null and enchant lvl is less than 16 and is a weapon
            if (item == null || item.ItemNumber.Category != ItemCategory.Weapon || item.EnchantLvl >= 16 )
            {
                session.SendAsync(new EnchantItemAckMessage(EnchantResult.ErrorItemEnchant));
            }
            else
            {
                try
                {
                    EnchantSys enchantSystems = GameServer.Instance.ResourceCache.GetEnchantSystem()[item.EnchantLvl];
                    uint num = (new uint[25]
                    {
                        200,
                        200,
                        300,
                        300,
                        500,
                        500,
                        600,
                        600,
                        800,
                        800,
                        1100,
                        1100,
                        1300,
                        1500,
                        1800,
                        2300,
                        2900,
                        3500,
                        4200,
                        5200,
                        5200,
                        5200,
                        5200,
                        5200,
                        5200
                    })[item.EnchantLvl];
                    EnchantGroup[] array = enchantSystems.EnchantGroup.Where(delegate (EnchantGroup i)
                    {
                        return (i.Category == item.ItemNumber.Category) & (i.SubCategory == item.ItemNumber.SubCategory);
                    }).ToArray();

                    // Checks if player's pen is less than cost
                    if (player.PEN < num)
                    {
                        session.SendAsync(new EnchantItemAckMessage(EnchantResult.NotEnoughMoney));
                        return;
                    }

                    EnchantSystem enchantSystem = array[0].Eff();
                    EnchantResult result = EnchantResult.Success;
                    uint num2 = enchantSystem.Effect;
                    List<EffectNumber> list = item.Effects.ToList();
                    if (list.Contains(num2 - 1))
                    {
                        list.Remove(num2 - 1);
                        list.Add(num2);
                    }
                    else if (list.Contains(num2 - 2))
                    {
                        list.Remove(num2 - 2);
                        list.Add(num2);
                    }
                    else if (list.Contains(num2 - 3))
                    {
                        list.Remove(num2 - 3);
                        list.Add(num2);
                    }
                    else if (list.Contains(num2 - 4))
                    {
                        list.Remove(num2 - 4);
                        list.Add(num2);
                    }
                    else if (!list.Contains(num2) && !list.Contains(num2 + 1) && !list.Contains(num2 + 2) && !list.Contains(num2 + 3) && !list.Contains(num2 + 4))
                    {
                        list.Add(num2);
                    }
                    else
                    {
                        list = new List<EffectNumber>();
                        if (item.ItemNumber.Category == ItemCategory.Weapon)
                        {

                            if (item.Effects[0].Equals(1299600007))
                            {
                                list.Add(1299600007);
                                list.Add(1299602002);
                                list.Add(1208300005);
                                list.Add(1208301005);
                            }
                            else if (item.Effects[0].Equals(1203300005))
                            {
                                list.Add(1203300005);
                                list.Add(1203301005);
                                list.Add(1299600007);
                            }
                            else if (item.Effects[0].Equals(1299600006))
                            {
                                list.Add(1299600006);
                            }
                            else
                            {
                                list.Add(1299600001);
                            }
                        }
                    }

                    item.EnchantLvl++;
                    item.Effects = list.ToArray();
                    item.EnchantMP = 0;
                    player.PEN -= num;
                    session.SendAsync(new EnchantItemAckMessage
                    {
                        Result = result,
                        ItemId = (ulong)message.ItemId,
                        Effect = num2
                    });
                    session.SendAsync(new ItemUpdateInventoryAckMessage(InventoryAction.Update, item.Map<PlayerItem, ItemDto>()));
                    session.SendAsync(new MoneyRefreshCashInfoAckMessage(player.PEN, player.AP));
                    Logger.ForAccount(session).Information(string.Format("Enchanted {0} With {1} PEN", item.ItemNumber, num));

                }
                catch (Exception ex)
                {
                    session.SendAsync(new EnchantItemAckMessage(EnchantResult.ErrorItemEnchant));
                    //Logger.Information(ex.StackTrace);
                }
            }
            return;
        }


        [MessageHandler(typeof(ItemUseRecordResetReqMessage))]
        public async void UserDeathmatchResetReq(GameSession session, ItemUseRecordResetReqMessage message)
        {
            try
            {
                Player plr = session.Player;
                PlayerItem item = plr.Inventory[message.ItemId];
                using (var db = GameDatabase.Open())
                {
                    PlayerDeathMatchDto plrDeathMatchDto = new PlayerDeathMatchDto
                    {
                        PlayerId = (int)plr.Account.Id,
                        Won = 0,
                        Loss = 0,
                        KillAssists = 0,
                        Kills = 0,
                        Deaths = 0
                    };
                    await db.UpdateAsync(plrDeathMatchDto);
                }
                plr.Inventory.Remove(item);
                await session.SendAsync(new ItemUseRecordResetAckMessage
                {
                    Result = 0,
                    Unk2 = 0
                });
                await session.SendAsync(new PlayerAccountInfoAckMessage(plr.Map<Player, PlayerAccountInfoDto>()));
            }
            catch (Exception ex)
            {
                Logger.Warning("Error During UserDeathMatchResetReq : " + ex);
            }
        }
    }
}