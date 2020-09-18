using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlubLib.DotNetty.Handlers.MessageHandling;
using Dapper.FastCrud;
using NeoNetsphere.Database.Auth;
using NeoNetsphere.Database.Game;
using NeoNetsphere.Network.Data.Game;
using NeoNetsphere.Network.Message.Game;
using ProudNetSrc.Handlers;
using Serilog;
using Serilog.Core;

namespace NeoNetsphere.Network.Services
{
  internal class CharService : ProudMessageHandler
  {
    // ReSharper disable once InconsistentNaming
    private static readonly ILogger Logger =
        Log.ForContext(Constants.SourceContextPropertyName, nameof(CharService));

    [MessageHandler(typeof(CharacterFirstCreateReqMessage))]
    public async Task CharacterFirstCreateHandler(GameSession session, CharacterFirstCreateReqMessage message)
    {
      var plr = session.Player;
      var cmng = plr.CharacterManager;
      if (cmng.Any() && plr.Account.Nickname != string.Empty)
        return;

      try
      {
        cmng.Remove(0);
        cmng.Remove(1);
        cmng.Remove(2);
      }
      catch
      {
      }

      if (string.IsNullOrWhiteSpace(plr.Account.Nickname))
      {
        if (!await AuthService.IsNickAvailableAsync(message.Nickname))
        {
          await session.SendAsync(new ServerResultAckMessage(ServerResult.NicknameUnavailable));
          return;
        }

        using (var db = AuthDatabase.Open())
        {
          var result = (await DbUtil.FindAsync<AccountDto>(db, smtp => smtp
              .Where($"{nameof(AccountDto.Id):C} = @Id")
              .WithParameters(new { session.Player.Account.Id })
          )).FirstOrDefault();

          if (result == null)
          {
            await session.SendAsync(new ServerResultAckMessage(ServerResult.CreateCharacterFailed));
            return;
          }

          result.Nickname = message.Nickname;
          await DbUtil.UpdateAsync(db, result);
          plr.Account.Nickname = message.Nickname;
        }
      }

      var items = new List<RequitalGiveItemResultDto>();
      try
      {
        cmng.CreateFirst(0, (CharacterGender)message.Gender);
        cmng.Select(0, true);
        try
        {
          var max = 6;
          if (message.FirstItems.Length < max)
            max = message.FirstItems.Length;

          foreach (var itemNumber in message.FirstItems.Take(max))
          {
            if (itemNumber != 0)
            {
              items.Add(new RequitalGiveItemResultDto(itemNumber, 0));
              var pi = plr.Inventory.CreateSilent(itemNumber, 0, 0, 1);
              cmng.CurrentCharacter.Costumes.Equip(pi, (CostumeSlot)pi.ItemNumber.SubCategory, true);
            }
          }
        }
        catch (ArgumentException e)
        {
          Logger.Debug(e, "FirstChar items couldnt be created");
        }
      }
      catch (CharacterException ex)
      {
        Logger.ForAccount(session)
            .Error(ex.Message);
        await session.SendAsync(new ServerResultAckMessage(ServerResult.CreateCharacterFailed));
        return;
      }

      await session.SendAsync(new RequitalGiveItemResultAckMessage(items.ToArray()));

      IEnumerable<StartItemDto> startItems;
      using (var db = GameDatabase.Open())
      {
        startItems = await DbUtil.FindAsync<StartItemDto>(db, statement => statement
            .Where(
                $"{nameof(StartItemDto.RequiredSecurityLevel):C} <= @{nameof(plr.Account.SecurityLevel)}")
            .WithParameters(new { plr.Account.SecurityLevel }));
      }

      foreach (var startItem in startItems)
      {
        var shop = GameServer.Instance.ResourceCache.GetShop();
        var item = shop.Items.Values.First(group => group.GetItemInfo(startItem.ShopItemInfoId) != null);
        var itemInfo = item.GetItemInfo(startItem.ShopItemInfoId);
        var effect = itemInfo.EffectGroup.GetEffect(startItem.ShopEffectId);

        var price = itemInfo.PriceGroup.GetPrice(startItem.ShopPriceId);
        if (price == null)
        {
          Logger.Warning("Cant find ShopPrice for Start item {startItemId} - Forgot to reload the cache?",
              startItem.Id);
          continue;
        }

        var color = startItem.Color;
        if (color > item.ColorGroup)
        {
          Logger.Warning("Start item {startItemId} has an invalid color {color}", startItem.Id, color);
          color = 0;
        }

        var count = startItem.Count;
        if (count > 0 && item.ItemNumber.Category <= ItemCategory.Skill)
        {
          Logger.Warning("Start item {startItemId} cant have stacks(quantity={count})", startItem.Id, count);
          count = 0;
        }

        if (count < 0) count = 0;
        var reteff = new List<EffectNumber> { effect.Effect };
        plr.Inventory.CreateSilent(itemInfo, price, color, reteff.ToArray(), (uint)count);
      }

      plr.NeedsToSave = true;
      plr.Save();
      await AuthService.LoginAsync(session);
    }

    [MessageHandler(typeof(CharacterCreateReqMessage))]
    public void CreateCharacterHandler(GameSession session, CharacterCreateReqMessage message)
    {
      Logger.ForAccount(session)
          .Information("Creating character: {slot}", message.Slot);

      try
      {
        session.Player?.CharacterManager?.Create(message.Slot, message.Style.Gender);
      }
      catch (CharacterException ex)
      {
        Logger.ForAccount(session)
            .Error(ex.Message);
        session.SendAsync(new ServerResultAckMessage(ServerResult.CreateCharacterFailed));
      }
    }

    [MessageHandler(typeof(CharacterSelectReqMessage))]
    public void SelectCharacterHandler(GameSession session, CharacterSelectReqMessage message)
    {
      var plr = session.Player;

      if (plr == null)
        return;

      // Prevents player from changing characters while playing
      if (plr.Room?.Id > 0 &&
          plr.Room?.GameState != GameState.Waiting &&
          plr.Room?.SubGameState != GameTimeState.HalfTime &&
          plr.RoomInfo.State != PlayerState.Lobby)
      {
        session.SendAsync(new ServerResultAckMessage(ServerResult.SelectCharacterFailed));
        return;
      }

      Logger.ForAccount(session)
          .Information("Selecting character {slot}", message.Slot);

      try
      {
        plr.CharacterManager?.Select(message.Slot);
      }
      catch (CharacterException ex)
      {
        Logger.ForAccount(session)
            .Error(ex.Message);
        session.SendAsync(new ServerResultAckMessage(ServerResult.SelectCharacterFailed));
      }
    }

    [MessageHandler(typeof(CharacterDeleteReqMessage))]
    public void DeleteCharacterHandler(GameSession session, CharacterDeleteReqMessage message)
    {
      Logger.ForAccount(session)
          .Information("Removing character {slot}", message.Slot);

      var plr = session.Player;

      if (plr == null)
        return;

      // Prevents player from deleting characters while playing
      if (plr.Room?.Id > 0 &&
          plr.Room?.GameState != GameState.Waiting &&
          plr.Room?.SubGameState != GameTimeState.HalfTime &&
          plr.RoomInfo.State != PlayerState.Lobby)
      {
        session.SendAsync(new ServerResultAckMessage(ServerResult.DeleteCharacterFailed));
        return;
      }

      try
      {
        session.Player?.CharacterManager?.Remove(message.Slot);
      }
      catch (Exception ex)
      {
        Logger.ForAccount(session).Error(ex.Message);
        session.SendAsync(new ServerResultAckMessage(ServerResult.DeleteCharacterFailed));
      }
    }
  }
}