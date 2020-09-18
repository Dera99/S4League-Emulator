namespace NeoNetsphere
{
  using System.Linq;
  using Network;
  using Network.Message.GameRule;

  internal class VoteKickManager
  {
    private Player Sender { get; set; }

    private Player Target { get; set; }

    private uint PlayerVoted { get; set; }

    private uint YesPlayer { get; set; }

    private VoteKickReason Reason { get; set; }

    public KickState State { get; internal set; }

    private Room Room { get; set; }

    public VoteKickManager(Room room)
    {
      State = KickState.CanStart;
      Room = room;
    }

    public void Start(Player sender, Player target, VoteKickReason reason)
    {
      if (State == KickState.CanStart)
      {
        Sender = sender;
        Target = target;
        Reason = reason;

        PlayerVoted++;
        YesPlayer++;

        Room.Broadcast(new GameKickOutStateAckMessage
        {
          DialogStyle = VoteKickDialogStyle.KickDialogWithSeconds,
          PlayerVoted = PlayerVoted,
          YesCount = YesPlayer,
          Reason = Reason,
          Sender = Sender.Account.Id,
          Target = Target.Account.Id
        });

        State = KickState.Execution;
      }
    }

    public void Update()
    {
      var target = Room.Players.FirstOrDefault(x => x.Value == Target).Value;
      if (target == null)
      {
        Room.Broadcast(new GameKickOutStateAckMessage
        {
          DialogStyle = VoteKickDialogStyle.KickDialogCancelled,
          PlayerVoted = PlayerVoted,
          YesCount = YesPlayer,
          Reason = Reason,
          Sender = Sender.Account.Id,
          Target = Target.Account.Id
        });

        Clear();
      }
    }

    public void UpdateResult(bool isYes)
    {
      PlayerVoted += 1;
      YesPlayer += isYes ? (uint)1 : 0;

      Room.Broadcast(new GameKickOutStateAckMessage
      {
        DialogStyle = VoteKickDialogStyle.KickDialogWithoutSeconds,
        PlayerVoted = PlayerVoted,
        YesCount = YesPlayer,
        Reason = Reason,
        Sender = Sender.Account.Id,
        Target = Target.Account.Id
      });
    }

    public void Evaluate()
    {
      State = KickState.End;

      var majority = (Room.Players.Count / 2) + 1;
      if (YesPlayer >= majority)
      {
        Room.Broadcast(new GameKickOutStateAckMessage
        {
          DialogStyle = VoteKickDialogStyle.KickDialogPlayerKicked,
          PlayerVoted = PlayerVoted,
          YesCount = YesPlayer,
          Reason = Reason,
          Sender = Sender.Account.Id,
          Target = Target.Account.Id
        });

        Room.Leave(Target, RoomLeaveReason.VoteKick);
      }
      else
      {
        Room.Broadcast(new GameKickOutStateAckMessage
        {
          DialogStyle = VoteKickDialogStyle.KickDialogNotKicked,
          PlayerVoted = PlayerVoted,
          YesCount = YesPlayer,
          Reason = Reason,
          Sender = Sender.Account.Id,
          Target = Target.Account.Id
        });
      }

      Clear();
    }

    public void Clear()
    {
      Sender = null;
      Target = null;
      Reason = VoteKickReason.Etc;

      PlayerVoted = 0;
      YesPlayer = 0;
      State = KickState.CanStart;
    }

    internal enum KickState
    {
      CanStart,
      Execution,
      End
    }
  }
}
