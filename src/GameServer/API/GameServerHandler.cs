using System;
using System.Collections.Generic;
using System.Linq;

using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

using NeoNetsphere;
using NeoNetsphere.Network;

using Serilog;
using Serilog.Core;

namespace NeoNetsphere.API
{
  public class GameServerHandler : ChannelHandlerAdapter
  {
    private static readonly ILogger logger = Log.ForContext(Constants.SourceContextPropertyName, nameof(GameServerHandler));
    private static readonly short Magic = 0x1111;

    public override void ChannelActive(IChannelHandlerContext context)
    {
      base.ChannelActive(context);
      var message = new DMessage();
      message.Write(DMessage.MessageType.Notify);
      message.Write("NetIT-Core");
      SendA(context, message);
    }

    public override void ChannelRead(IChannelHandlerContext context, object messageData)
    {
      var buffer = messageData as IByteBuffer;
      var data = new byte[0];
      if (buffer != null) data = buffer.GetIoBuffer().ToArray();

      var msg = new DMessage(data, data.Length);
      short magic = 0;
      var message = new ByteArray();

      if (msg.Read(ref magic)
        && magic == Magic
        && msg.Read(ref message))
      {
        var receivedMessage = new DMessage(message);
        DMessage.MessageType coreId = 0;
        if (!receivedMessage.Read(ref coreId))
          return;

        switch (coreId)
        {
          case DMessage.MessageType.Notify:
            break;

          case DMessage.MessageType.Rmi:
            short type = 0;
            receivedMessage.Read(ref type);

            switch (type)
            {
              case 10: //Online Players
                var result = GameServer.Instance.Sessions.Values.Where(c =>
                {
                  var a = (GameSession)c;
                  return a.IsLoggedIn();
                }).ToList().Count;
                var ack = new DMessage();

                if (result < 0)
                {
                  ack.Write(false);
                }
                else
                {
                  ack.Write(true);
                  ack.Write(result);
                }

                RmiSend(context, 11, ack);
                break;

              case 12: //Test Server
                long channelId = 0;
                long userId = 0;

                receivedMessage.Read(ref channelId);
                receivedMessage.Read(ref userId);

                if (channelId == 0 || userId == 0)
                  return;

                var response = new DMessage();
                response.Write(channelId);
                response.Write(userId);

                RmiSend(context, 13, response);
                break;
            }

            break;
        }
      }
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
      base.ExceptionCaught(context, exception);
      logger.Error(exception.StackTrace);
    }

    public void RmiSend(IChannelHandlerContext ctx, short rmiId, DMessage message)
    {
      var rmiframe = new DMessage();
      rmiframe.Write(DMessage.MessageType.Rmi);
      rmiframe.Write(rmiId);
      rmiframe.Write(message);
      SendA(ctx, rmiframe);
    }

    public void SendA(IChannelHandlerContext ctx, DMessage data)
    {
      var coreframe = new DMessage();
      coreframe.Write(Magic);
      coreframe.WriteScalar(data.Length);
      coreframe.Write(data);

      var buffer = Unpooled.Buffer(coreframe.Length);
      buffer.WriteBytes(coreframe.Buffer);
      ctx.WriteAndFlushAsync(buffer);
    }
  }
}
