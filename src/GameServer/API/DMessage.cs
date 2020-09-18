using System;
using System.Collections.Generic;
using System.Text;

namespace NeoNetsphere.API
{
  public class DMessage : ByteArray
  {
    public byte[] Buffer => GetBuffer();
    public int Length => Buffer.Length;

    public DMessage()
    {
    }

    public DMessage(ByteArray packet)
      : base(packet)
    {
    }

    public DMessage(byte[] data, int length)
      : base(data, length)
    {
    }


    internal void Write(MessageType obj)
    {
      Write((byte)obj);
    }

    internal bool Read(ref MessageType obj)
    {
      byte a = 0;
      if (!Read(ref a))
        return false;
      obj = (MessageType)a;
      return true;
    }

    internal void Write(DMessage obj)
    {
      Write(obj.Buffer);
    }

    internal void Write(string obj)
    {
      Write((byte)1);
      WriteScalar(obj.Length);
      Write(Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(Encoding.UTF8.GetBytes(obj))));
    }

    internal bool Read(ref string obj)
    {
      long length = 0;
      byte type = 0;
      if (Read(ref type)
          && ReadScalar(ref length))
      {
        var binarytext = new byte[length];

        if (Read(ref binarytext, (int)length))
        {
          switch (type)
          {
            case 1:
              obj = Encoding.ASCII.GetString(binarytext);
              return true;
            case 2:
              obj = Encoding.Unicode.GetString(binarytext);
              return true;
            default:
              return false;
          }
        }
      }
      return false;
    }

    internal enum MessageType : byte
    {
      Ignore,
      Rmi,
      Encrypted,
      Notify
    }
  }
}
