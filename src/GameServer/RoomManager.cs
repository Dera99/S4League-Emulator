using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BlubLib.Collections.Concurrent;
using BlubLib.Threading.Tasks;
using NeoNetsphere;
using NeoNetsphere.Network;
using NeoNetsphere.Network.Message.Game;
using NeoNetsphere.Game;
using ProudNetSrc;
using Serilog;
using Serilog.Core;

// ReSharper disable once Checknamespace 
namespace NeoNetsphere
{
  internal class RoomManager : IReadOnlyCollection<Room>
  {
    public static readonly ILogger Logger = Log.ForContext(Constants.SourceContextPropertyName, "RoomManager");
    public readonly ConcurrentDictionary<uint, Room> _rooms = new ConcurrentDictionary<uint, Room>();

    public RoomManager(Channel channel)
    {
      Channel = channel;
      GameRuleFactory = new GameRuleFactory();
    }

    public Channel Channel { get; }

    public GameRuleFactory GameRuleFactory { get; }

    public void Update(AccurateDelta delta)
    {
      Parallel.ForEach(_rooms.Values, (room) =>
      {
        try
        {
          if (room == null)
            return;

          if (room.Id == 0 || !room.Players.Any())
          {
            Remove(room);
            return;
          }

          room?.Update(delta);
        }
        catch (Exception ex)
        {
          Logger.Error(ex.ToString());
        }
      });
    }

    public Room Get(uint id)
    {
      _rooms.TryGetValue(id, out var room);
      return room;
    }

    public Room Create_2(RoomCreationOptions options, P2PGroup p2pGroup)
    {
      try
      {
        uint id = 1;
        while (true)
        {
          if (!_rooms.ContainsKey(id))
            break;
          id++;
        }

        var room = new Room(this, id, options, p2pGroup, options.Creator);

        var roomDto = room.GetRoomInfo();
        roomDto.Password =
            !string.IsNullOrWhiteSpace(room.Options.Password) ||
            !string.IsNullOrEmpty(room.Options.Password)
                ? "nice try :)"
                : "";
        Channel.Broadcast(new RoomDeployAck2Message(roomDto));

        return room;
      }
      catch (Exception ex)
      {
        throw new Exception(ex.ToString());
      }
    }

    public Room Create(RoomCreationOptions options, P2PGroup p2pGroup)
    {
      uint id = 1;
      while (true)
      {
        if (!_rooms.ContainsKey(id))
          break;
        id++;
      }

      var room = new Room(this, id, options, p2pGroup, options.Creator);
      var roomDto = room.GetRoomInfo();
      roomDto.Password =
          !string.IsNullOrWhiteSpace(room.Options.Password) ||
          !string.IsNullOrEmpty(room.Options.Password)
              ? "nice try :)"
              : "";
      Channel.Broadcast(new RoomDeployAckMessage(roomDto));

      return room;
    }

    public void Remove(Room room)
    {
      if (room == null || room.Disposed || !_rooms.ContainsKey(room.Id))
        return;

      if (room.Players.Any())
        return;

      _rooms.Remove(room.Id);
      Channel.Broadcast(new RoomDisposeAckMessage(room.Id));
      room.Dispose();
    }

    #region Events

    #endregion

    #region IReadOnlyCollection

    public int Count => _rooms.Count;

    public Room this[uint id] => Get(id);

    public IEnumerator<Room> GetEnumerator()
    {
      return _rooms.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
      return GetEnumerator();
    }

    #endregion
  }
}