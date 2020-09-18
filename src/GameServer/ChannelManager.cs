// ReSharper disable once Checknamespace 

using System.Diagnostics;
using System.Threading.Tasks;
using NeoNetsphere.Network;

namespace NeoNetsphere
{
  using System;
  using System.Collections;
  using System.Collections.Concurrent;
  using System.Collections.Generic;
  using System.Drawing;
  using System.Linq;
  using NeoNetsphere;
  using NeoNetsphere.Database.Game;

  internal class ChannelManager : IReadOnlyCollection<Channel>
  {
    private readonly ConcurrentDictionary<uint, Channel> _channels = new ConcurrentDictionary<uint, Channel>();

    public ChannelManager(IEnumerable<ChannelDto> channelInfos)
    {
      _channels.TryAdd(0,
          new Channel()
          {
            Name = "basechannel",
            PlayerLimit = Config.Instance.PlayerLimit,
            MaxLevel = 255,
            MinLevel = 0
          });

      foreach (var info in channelInfos.Where(x => x.Id > 0))
      {
        var channel = new Channel
        {
          Id = info.Id,
          Name = info.Name,
          Description = info.Description,
          PlayerLimit = info.PlayerLimit,
          MinLevel = info.MinLevel,
          MaxLevel = info.MaxLevel,
          Color = Color.FromArgb((int)info.Color),
        };
        channel.Color = Color.FromArgb(channel.Color.R, channel.Color.G, channel.Color.B);

        channel.PlayerJoined += (s, e) => OnPlayerJoined(e);
        channel.PlayerLeft += (s, e) => OnPlayerLeft(e);
        _channels.TryAdd((uint)info.Id, channel);
      }
    }

    public Channel this[uint id] => GetChannel(id);

    public Channel GetChannel(uint id)
    {
      _channels.TryGetValue(id, out var channel);
      return channel;
    }

    public void Update(AccurateDelta delta)
    {
      Parallel.ForEach(_channels.Values, (channel) => channel?.Update(delta));
    }

    #region Events

    public event EventHandler<ChannelPlayerJoinedEventArgs> PlayerJoined;
    public event EventHandler<ChannelPlayerLeftEventArgs> PlayerLeft;

    protected virtual void OnPlayerJoined(ChannelPlayerJoinedEventArgs e)
    {
      PlayerJoined?.Invoke(this, e);
    }

    protected virtual void OnPlayerLeft(ChannelPlayerLeftEventArgs e)
    {
      PlayerLeft?.Invoke(this, e);
    }

    #endregion

    #region IReadOnlyCollection

    public int Count => _channels.Count;

    public IEnumerator<Channel> GetEnumerator()
    {
      return _channels.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
      return GetEnumerator();
    }

    #endregion
  }
}