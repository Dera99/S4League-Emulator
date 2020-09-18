using System.Linq;
using System.Threading;
using Dapper.FastCrud;
using NeoNetsphere.Database.Game;

namespace NeoNetsphere
{
  internal static class ItemIdGenerator
  {
    private static long s_counter;

    public static void Initialize()
    {
      using (var db = GameDatabase.Open())
      {
        var result = DbUtil.Find<PlayerItemDto>(db);
        if (result.Any())
          s_counter = result.Max(item => item.Id);
      }
    }

    public static ulong GetNextId()
    {
      return (ulong)Interlocked.Add(ref s_counter, 1);
    }
  }

  internal static class DenyIdGenerator
  {
    private static int s_counter;

    public static void Initialize()
    {
      using (var db = GameDatabase.Open())
      {
        var result = DbUtil.Find<PlayerDenyDto>(db);
        if (result.Any())
          s_counter = result.Max(item => item.Id);
      }
    }

    public static int GetNextId()
    {
      return Interlocked.Add(ref s_counter, 1);
    }
  }
}
