using NeoNetsphere;
using NeoNetsphere.Network.Message.Game;
using System;
using System.Collections.Generic;

internal class BoostManager
{
     // NOT FINISHED YET !!!
    // Not Working
    // NotUsed


    private Player _player;

    private PlayerItem _boost;

    internal BoostManager(Player plr)
    {
        _player = plr;
    }

    public void Equip(PlayerItem item, int slot)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }
        if (!CanEquip(item, slot))
        {
            throw new CharacterException($"Cannot equip item {item.ItemNumber} on slot {slot}");
        }
        _boost = item;
        _player.Session.SendAsync(new ItemUseItemAckMessage
        {
            CharacterSlot = 0,
            ItemId = item.Id,
            Action = UseItemAction.Equip,
            EquipSlot = 0
        });
    }

    public void UnEquip(int slot)
    {
        if (_player.Room != null && _player.RoomInfo.State != PlayerState.Lobby)
        {
            throw new CharacterException("Can't change items while playing");
        }
        if (_boost == null)
        {
            throw new CharacterException("No boost equiped");
        }
        _player.Session.SendAsync(new ItemUseItemAckMessage
        {
            CharacterSlot = 0,
            ItemId = (_boost?.Id ?? 0),
            Action = UseItemAction.UnEquip,
            EquipSlot = (byte)slot
        });
        _boost = null;
    }

    public int GetBoostType()
    {
        if (_boost == null)
        {
            return 0;
        }
        int result = 0;
        switch ((uint)_boost.ItemNumber)
        {
            case 5000003:
                result = 4;
                break;
            case 5000001:
            case 5000002:
                result = 2;
                break;
        }
        return result;
    }

    public float GetExpRate()
    {
        if (_boost == null)
        {
            return 0f;
        }
        if (_boost.ItemNumber == 5000003)
        {
            return 0.3f;
        }
        return 0f;
    }

    public float GetPenRate()
    {
        if (_boost == null)
        {
            return 0f;
        }
        if (_boost.ItemNumber == 5000001u)
        {
            return 0.5f;
        }
        if (_boost.ItemNumber == 5000002u)
        {
            return 1f;
        }
        return 0f;
    }

    public PlayerItem GetItem(int slot)
    {
        if (slot != 0)
        {
            throw new CharacterException("Item already equipped on slot " + slot);
        }
        return _boost;
    }

    public IReadOnlyList<PlayerItem> GetItems()
    {
        return new List<PlayerItem>
        {
            _boost
        };
    }

    public bool CanEquip(PlayerItem item, int slot)
    {
        if (item == null)
        {
            return false;
        }
        if (item.ItemNumber.Category != ItemCategory.Boost)
        {
            return false;
        }
        if (slot != 0)
        {
            return false;
        }
        if (_player.Room != null && _player.RoomInfo.State != PlayerState.Lobby)
        {
            return false;
        }
        return true;
    }
}
