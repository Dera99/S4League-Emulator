using System.Linq;

namespace NeoNetsphere
{
  public static class Namecheck
  {
    public static bool IsNameValid(string name, bool allowSpace = false)
    {
      if (!allowSpace)
      {
        return !name.StartsWith("[") && !name.Contains("GM") && !name.Contains("GS") &&
               !name.ToLower().Contains("admin") && name.All(c => char.IsLetterOrDigit(c) || c == '_');
      }
      else
      {
        return !name.StartsWith("[") && !name.Contains("GM") && !name.Contains("GS") &&
               !name.ToLower().Contains("admin") &&
               name.All(c => char.IsLetterOrDigit(c) || c == ' ' || c == '_');
      }
    }
  }
}