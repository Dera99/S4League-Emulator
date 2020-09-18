namespace NeoNetsphere
{
  public struct CharacterStyle
  {
    public uint Value => this;

    public CharacterGender Gender { get; set; }
    public byte Slot { get; set; }

    public CharacterStyle(uint value)
    {
      Gender = (CharacterGender)(value & 1);
      Slot = (byte)(value >> 28);
    }

    public CharacterStyle(CharacterGender gender, byte slot)
    {
      Gender = gender;
      Slot = slot;
    }

    public override string ToString()
    {
      return Value.ToString();
    }

    public static implicit operator uint(CharacterStyle style)
    {
      var value = (byte)style.Gender | (0 << 1) | (0 << 7) | (0 << 13) |
                  (0 << 18) | (style.Slot << 28);
      return (uint)value;
    }

    public static implicit operator CharacterStyle(uint value)
    {
      return new CharacterStyle(value);
    }
  }
}
