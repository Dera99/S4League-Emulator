using System.Linq;

namespace NeoNetsphere
{
  public static class Namecheck
  {
    public static bool IsNameValid(string name)
    {
      return !name.StartsWith("[") && !name.Contains("GM") && !name.Contains("GS") &&
             !name.Contains("CM") && !name.Contains("PM") && !name.Contains("SA") &&
             !name.ToLower().Contains("admin") && name.All(c => char.IsLetterOrDigit(c)
             || c == '_' || c == '.' || c == '-' || c == '*' || c == '+' || c == '~'
             || c == '-' || c == '/' || c == '#');
    }
  }
}
