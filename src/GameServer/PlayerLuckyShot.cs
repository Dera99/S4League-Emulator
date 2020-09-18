namespace NeoNetsphere
{
  using System;
  using System.Collections.Generic;
  using System.Text;

  using NeoNetsphere.Network.Message.GameRule;

  internal class PlayerLuckyShot
  {
    private static readonly int _percentage = 50;
    private static readonly int _luckyGain = 30;

    private Random _luckyShotRandom = new Random();

    public Player Player { get; internal set; }


    public int BonusExp { get; internal set; }

    public int BonusPen { get; internal set; }

    public PlayerLuckyShot(Player plr)
    {
      this.Player = plr;

      this.BonusExp = 0;
      this.BonusPen = 0;
    }

    public void TryShot(LuckyShotType shotType)
    {
      if (this._luckyShotRandom.Next(100) > _percentage)
      {
        this.Player.SendAsync(new GameLuckyShotAckMessage() { LuckyShotType = shotType, Value = _luckyGain, Unk3 = 0 });
        if (shotType == LuckyShotType.EXP) this.BonusExp += _luckyGain;
        else if (shotType == LuckyShotType.PEN) this.BonusPen += _luckyGain;
      }
    }

    public void Clear()
    {
      this.BonusExp = 0;
      this.BonusPen = 0;
    }
  }
}
