using System;
using System.IO;
using System.Runtime.CompilerServices;
using BlubLib.Serialization;
using ProudNetSrc.Serialization.Serializers;

namespace ProudNetSrc.Serialization.Messages.Core
{
  [BlubContract]
  internal class RmiMessage : ICoreMessage
  {
    public RmiMessage()
    {
    }

    public RmiMessage(byte[] data)
    {
      Data = data;
    }

    [BlubMember(0, typeof(ReadToEndSerializer))]
    public byte[] Data { get; set; }
  }

  [BlubContract]
  internal class EncryptedReliableMessage : ICoreMessage
  {
    public EncryptedReliableMessage()
    {
    }

    public EncryptedReliableMessage(byte[] data, EncryptMode encryptMode)
    {
      Data = data;
      EncryptMode = encryptMode;
    }

    [BlubMember(0)] public EncryptMode EncryptMode { get; set; }

    [BlubMember(1, typeof(ArrayWithScalarSerializer))]
    public byte[] Data { get; set; }
  }

  [BlubContract]
  internal class Encrypted_UnReliableMessage : ICoreMessage
  {
    public Encrypted_UnReliableMessage()
    {
    }

    public Encrypted_UnReliableMessage(byte[] data)
    {
      Data = data;
    }

    [BlubMember(0)] public byte Unk { get; set; }

    [BlubMember(1, typeof(ArrayWithScalarSerializer))]
    public byte[] Data { get; set; }
  }

  [BlubContract(typeof(Serializer))]
  internal class CompressedMessage : ICoreMessage
  {
    public CompressedMessage()
    {
    }

    public CompressedMessage(int decompressedLength, byte[] data)
    {
      DecompressedLength = decompressedLength;
      Data = data;
    }

    public int DecompressedLength { get; set; }
    public byte[] Data { get; set; }

    internal class Serializer : ISerializer<CompressedMessage>
    {
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public bool CanHandle(Type type)
      {
        return type == typeof(CompressedMessage);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void Serialize(BinaryWriter writer, CompressedMessage value)
      {
        writer.WriteScalar(value.Data.Length);
        writer.WriteScalar(value.DecompressedLength);
        writer.Write(value.Data);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public CompressedMessage Deserialize(BinaryReader reader)
      {
        var length = reader.ReadScalar();
        return new CompressedMessage(reader.ReadScalar(), reader.ReadBytes(length));
      }
    }
  }

  [BlubContract]
  internal class ReliableUdp_FrameMessage : ICoreMessage
  {
    [BlubMember(0)] public byte Unk { get; set; }

    [BlubMember(1, typeof(ArrayWithScalarSerializer))]
    public byte[] Data { get; set; }
  }
}
