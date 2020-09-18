using System;
using System.Collections.Generic;
using System.Text;

namespace NeoNetsphere.API
{
  public class ByteArray
  {
    private byte[] _buffer = new byte[0];
    private int _readoffset;
    private int _writeoffset;

    public byte[] GetBuffer()
    {
      return (byte[])_buffer.Clone();
    }

    public int ReadOffset => _readoffset;
    public int WriteOffset => _writeoffset;

    public ByteArray()
    {
    }

    public ByteArray(ByteArray data)
    {
      _buffer = data._buffer;
    }

    public ByteArray(byte[] data)
    {
      _buffer = (byte[])data.Clone();
    }

    public ByteArray(byte[] data, int length)
    {
      _buffer = new byte[length];
      Array.Copy(data, _buffer, length);
    }

    internal void Write(byte[] obj)
    {
      if (_buffer.Length <= _writeoffset + obj.Length)
        Array.Resize(ref _buffer, _writeoffset + obj.Length);
      Array.Copy(obj, 0, _buffer, _writeoffset, obj.Length);
      _writeoffset = _writeoffset + obj.Length;
    }

    internal void WriteScalar(byte obj)
    {
      WriteScalar((long)obj);
    }

    internal void WriteScalar(short obj)
    {
      WriteScalar((long)obj);
    }

    internal void WriteScalar(int obj)
    {
      WriteScalar((long)obj);
    }

    internal void WriteScalar(long obj)
    {
      if (obj <= sbyte.MaxValue)
      {
        Write((byte)1);
        Write((byte)obj);
      }
      else if (obj <= short.MaxValue)
      {
        Write((byte)2);
        Write((short)obj);
      }
      else if (obj <= int.MaxValue)
      {
        Write((byte)4);
        Write((int)obj);
      }
      else
      {
        Write((byte)8);
        Write(obj);
      }
    }

    internal void Write(bool obj)
    {
      if (obj)
        Write((byte)1);
      else
        Write((byte)0);
    }

    internal void Write(byte obj)
    {
      var data = new byte[1];
      data[0] = obj;
      Write(data);
    }

    internal void Write(short obj)
    {
      Write(BitConverter.GetBytes(obj));
    }

    internal void Write(int obj)
    {
      Write(BitConverter.GetBytes(obj));
    }

    internal void Write(long obj)
    {
      Write(BitConverter.GetBytes(obj));
    }

    internal void Write(ByteArray obj)
    {
      WriteScalar(obj._writeoffset);
      Write(obj._buffer);
    }

    internal bool Read(ref ByteArray obj)
    {
      long length = 0;
      if (ReadScalar(ref length))
      {
        var data = new byte[length];
        if (Read(ref data, data.Length))
        {
          obj = new ByteArray(data);
          return true;
        }
      }
      return false;
    }

    internal bool Read(ref byte[] obj, int length)
    {
      if (_buffer.Length >= _readoffset + length)
      {
        var data = new byte[length];
        Array.Copy(_buffer, _readoffset, data, 0, length);
        obj = data;
        _readoffset = _readoffset + length;
        return true;
      }
      return false;
    }

    internal bool Read(ref bool obj)
    {
      byte a = 0;
      var retval = Read(ref a);
      obj = a == 1;
      return retval;
    }

    internal bool Read(ref byte obj)
    {
      if (_buffer.Length >= _readoffset)
      {
        obj = _buffer[_readoffset];
        _readoffset = _readoffset + 1;
        return true;
      }
      return false;
    }

    internal bool Read(ref short obj)
    {
      var data = new byte[2];
      if (Read(ref data[0])
          && Read(ref data[1]))
      {
        obj = BitConverter.ToInt16(data, 0);
        return true;
      }
      return false;
    }

    internal bool Read(ref int obj)
    {
      var data = new byte[4];
      if (Read(ref data[0])
          && Read(ref data[1])
          && Read(ref data[2])
          && Read(ref data[3]))
      {
        obj = BitConverter.ToInt32(data, 0);
        return true;
      }
      return false;
    }

    internal bool Read(ref long obj)
    {
      var data = new byte[8];
      if (Read(ref data[0])
          && Read(ref data[1])
          && Read(ref data[2])
          && Read(ref data[3])
          && Read(ref data[4])
          && Read(ref data[5])
          && Read(ref data[6])
          && Read(ref data[7]))
      {
        obj = BitConverter.ToInt64(data, 0);
        return true;
      }
      return false;
    }

    internal bool ReadScalar(ref long obj)
    {
      byte a = 0;
      short b = 0;
      var c = 0;
      long d = 0;

      byte padding = 0;
      if (!Read(ref padding))
        return false;
      switch (padding)
      {
        case 8:
          if (!Read(ref d))
            return false;
          obj = d;
          break;
        case 4:
          if (!Read(ref c))
            return false;
          obj = c;
          break;
        case 2:
          if (!Read(ref b))
            return false;
          obj = b;
          break;
        case 1:
          if (!Read(ref a))
            return false;
          obj = a;
          break;
        default:
          return false;
      }
      return true;
    }
  }
}
