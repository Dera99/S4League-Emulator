using System;
using System.IO;
using System.Linq;
using NeoNetsphere;

// ReSharper disable once Checknamespace 
namespace NeoNetsphere.Game
{
  internal abstract class PlayerRecord
  {
    protected PlayerRecord(Player player)
    {
      Player = player;
      Player.RoomInfo.Stats = this;
    }

    public Player Player { get; }
    public abstract uint TotalScore { get; }
    public uint Kills { get; set; }
    public uint KillAssists { get; set; }
    public uint Suicides { get; set; }
    public uint Deaths { get; set; }

    public virtual uint GetPenGain(out uint bonusPen)
    {
      bonusPen = 0;
      var exp = GetExpGain(out var bonus);
      if (bonus > exp) return (uint)(exp + bonus / 2);
      return (uint)(exp - bonus / 2);
    }

    public virtual int GetExpGain(out int bonusExp)
    {
      var place = 1;
      ExperienceRates expRates = null;
      var game = Config.Instance.Game;
      bonusExp = 0;

      switch (Player.Room.GameRuleManager.GameRule.GameRule)
      {
        case GameRule.Arcade:
          break;
        case GameRule.Arena:
          break;
        case GameRule.BattleRoyal:
          expRates = game.BRExpRates;
          break;
        case GameRule.Captain:
          expRates = game.CaptainExpRates;
          break;
        case GameRule.Challenge:
          break;
        case GameRule.Chaser:
          expRates = game.ChaserExpRates;
          break;
        case GameRule.CombatTrainingDM:
          break;
        case GameRule.CombatTrainingTD:
          break;
        case GameRule.Deathmatch:
          expRates = game.DeathmatchExpRates;
          break;
        case GameRule.Horde:
          break;
        case GameRule.PassTouchdown:
          expRates = game.TouchdownExpRates;
          break;
        case GameRule.Practice:
          break;
        case GameRule.SemiTouchdown:
          break;
        case GameRule.Siege:
          break;
        case GameRule.SnowballFight:
          expRates = game.DeathmatchExpRates;
          break;
        case GameRule.Survival:
          break;
        case GameRule.Touchdown:
          expRates = game.TouchdownExpRates;
          break;
        case GameRule.Tutorial:
          break;
        case GameRule.Warfare:
          break;
      }

      if (expRates == null)
        return 0;

      var plrs = Player.Room.TeamManager.Players
          .Where(plr => plr.RoomInfo.State == PlayerState.Waiting &&
                        plr.RoomInfo.Mode == PlayerGameMode.Normal)
          .ToArray();

      foreach (var plr in plrs.OrderByDescending(plr => plr.RoomInfo.Stats.TotalScore))
      {
        if (plr == Player)
          break;

        place++;
        if (place > 3)
          break;
      }

      var rankingBonus = 1.0f;
      switch (place)
      {
        case 1:
          rankingBonus += expRates.FirstPlaceBonus / 100.0f;
          break;

        case 2:
          rankingBonus += expRates.SecondPlaceBonus / 100.0f;
          break;

        case 3:
          rankingBonus += expRates.ThirdPlaceBonus / 100.0f;
          break;
      }

      var timeExp = expRates.ExpPerMin * Player.RoomInfo.PlayTime.Minutes;
      var playersExp = plrs.Length * expRates.PlayerCountFactor;
      var scoreExp = expRates.ExpPerMin * TotalScore;

      var expGained = (timeExp + playersExp + scoreExp) * rankingBonus;

      bonusExp = (int)expGained /* * Player.GetExpRate()*/;

      return (int)Math.Round(bonusExp * 0.01);
    }

    public virtual void Reset()
    {
      Kills = 0;
      KillAssists = 0;
      Suicides = 0;
      Deaths = 0;
      Player.LuckyShot.Clear();
    }

    public virtual void Serialize(BinaryWriter w, bool isResult)
    {
      if (Player?.Account == null)
        return;

      if (Player?.Account?.Id == 0)
        return;

      if (Player.RoomInfo.Team == null)
        return;

      w.Write(Player.Account.Id); // Int64
      w.Write((byte)Player.RoomInfo.Team.Team); // Int8
      w.Write((byte)Player.RoomInfo.State); // Int8
      w.Write(Convert.ToByte(Player.RoomInfo.IsReady)); // Int8
      w.Write((uint)Player.RoomInfo.Mode); // Int32
      w.Write(TotalScore); // Int32
      w.Write(0); // Int32

      uint bonusPen = 0;
      var bonusExp = 0;
      var rankUp = false;
      if (isResult && Player.RoomInfo.State != PlayerState.Lobby)
      {
        var penGain = GetPenGain(out bonusPen);
        var expGain = GetExpGain(out bonusExp);
        if (Player.Room.Options.IsFriendly)
        {
          expGain = 0;
          penGain /= 80;

          bonusExp = 0;
          bonusPen /= 80;
        }

        penGain += (uint)Player.LuckyShot.BonusPen;
        expGain += Player.LuckyShot.BonusExp;

        bonusPen += (uint)Player.LuckyShot.BonusPen;
        bonusExp += Player.LuckyShot.BonusExp;
        w.Write(penGain); // Int32
        w.Write(expGain); // Int32
        Player.PEN += (penGain + bonusPen);
        rankUp = Player.GainExp(expGain + bonusExp);
      }
      else
      {
        w.Write(0);
        w.Write(0);
      }

      w.Write(Player.TotalExperience); // Int32
      w.Write(rankUp); // Int8
      w.Write(bonusExp); // Int32
      w.Write(bonusPen); // Int32
      w.Write(0); // Int32

      /*
          1 PC Room(korean internet cafe event)
          2 PEN+
          4 EXP+
          8 20%
          16 25%
          32 30%
      */

      w.Write(0); // Int32
      w.Write((byte)0); // Int8
      w.Write((byte)0); // Int8
      w.Write((byte)0); // Int8
      w.Write(0); // Int32
      w.Write(0); // Int32
      w.Write(0); // Int32
      w.Write(0); // Int32

      // NEW - UNKNOWN
      w.Write(0); // Int32
      w.Write((byte)0); // Int8 -- player room index?? team?
      w.Write(0); // Int32
      w.Write(0); // Int32
    }
  }
}