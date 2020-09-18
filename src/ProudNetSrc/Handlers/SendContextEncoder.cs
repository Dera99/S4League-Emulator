namespace ProudNetSrc.Handlers
{
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using DotNetty.Buffers;
  using DotNetty.Codecs;
  using DotNetty.Transport.Channels;
  using ProudNetSrc.Codecs;
  using ProudNetSrc.Serialization.Messages.Core;

  internal class SendContextEncoder : MessageToMessageEncoder<SendContext>
  {
    protected override void Encode(IChannelHandlerContext context, SendContext message, List<object> output)
    {
      if (!(message.Message is IByteBuffer buffer))
        throw new ProudException($"{nameof(SendContextEncoder)} can only handle {nameof(IByteBuffer)}");

      try
      {
        var data = buffer.GetIoBuffer().ToArray();
        ICoreMessage coreMessage = new RmiMessage(data);

        if (message.SendOptions.Compress)
        {
          data = CoreMessageEncoder.Encode(coreMessage);
          if (data.Length > 500)
          {
            coreMessage = new CompressedMessage(data.Length, data.CompressZLib());
          }
        }

        if (message.SendOptions.Encrypt)
        {
          data = CoreMessageEncoder.Encode(coreMessage);
          var session = context.Channel.GetAttribute(ChannelAttributes.Session).Get();
          using (var src = new MemoryStream(data))
          using (var dst = new MemoryStream())
          {
            session.Crypt.Encrypt(context.Allocator, EncryptMode.Secure, src, dst, true);
            data = dst.ToArray();
          }

          coreMessage = new EncryptedReliableMessage(data, EncryptMode.Secure);
        }

        output.Add(coreMessage);
      }
      finally
      {
        buffer.Release();
      }
    }
  }
}
