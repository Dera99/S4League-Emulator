namespace NeoNetsphere.Network
{
  public enum AuthLoginResult : byte
  {
    OK = 0,
    WrongIdorPw = 1,
    Banned = 2,
    Failed = 3, // Used for permanent ban

    Failed2 = 7
  }

  public enum GameLoginResult : uint
  {
    OK = 0,
    ServerFull = 1,
    TerminateOtherConnection = 2,
    ExistingExit = 3,
    ServerFull2 = 4,
    WrongVersion = 5,
    ChooseNickname = 6,

    FailedAndRestart = 7,
    SessionTimeout = 8,
    AuthenticationFailed = 9
  }

  public enum ServerResult : uint
  {
    ServerError = 0,
    CannotFindRoom = 1,
    AlreadyPlaying = 2,
    NonExistingChannel = 3,
    ChannelLimitReached = 4,
    ChannelEnter = 5,
    ServerLimitReached = 6,
    PlayerLimitReached = 7,
    RoomChangingRules = 8,
    ChannelLeave = 9,
    PlayerNotFound = 10,
    CreateCharacterFailed = 11,
    DeleteCharacterFailed = 12,
    SelectCharacterFailed = 13,
    CreateNicknameSuccess = 14,
    NicknameUnavailable = 15,
    NicknameAvailable = 16,
    PasswordError = 17,
    WelcomeToS4World = 18,
    IPLocked = 19,
    ForbiddenToConnectFor5Min = 20,
    UserAlreadyExist = 21,
    DBError = 22,
    CreateCharacterFailed2 = 23,
    JoinChannelFailed = 24,
    RequiredChannelLicense = 25,
    WearingUnusableItem = 26,
    CannotSellWearingItem = 27,
    CantEnterRoom = 29,
    ImpossibleToEnterRoom = 30,
    CantReadClanInfo = 31,
    TaskCompensationError = 32,
    FailedToRequestTask = 33,
    ItemExchangeFailed = 34,
    ItemExchangeFailed2 = 35,
    SelectGameMode = 36,
    EnteringFailed = 38, // You should clear the low level first
    HackingTrialDetected = 43,
    CantEnterBecauseKicked = 44,
    CantEnterBecauseVoteKick = 45,
    InternetSlow = 47,
    NetworkCheck = 48,
    CantKickThisPlayer = 49,
    WeaponNotAllowed = 50,
    FailedToCreateRoom = 56
  }

  public enum ChannelInfoRequest : byte
  {
    RoomList = 3,
    RoomList2 = 4,
    ChannelList = 5
  }

  public enum ChangeTeamResult : byte
  {
    Full = 0,
    AlreadyReady = 1
  }

  public enum ClubState
  {
    NotJoined,
    AwaitingAccept,
    Joined
  }

  public enum ClubRank
  {
    Master = 1,
    TempMaster,
    Staff,
    Regular,
    Normal,
    BadManner,
    Aclass,
    Bclass,
    Cclass
  }

  public enum VoteKickMessage
  {
    Ok = 1,
    InsufficientMoney = 2,
    CurrentlyRunning = 3,
    NotEnoughtPlayerToVote = 4,
    PlayerNotInRoom = 5,
    CantKickGM = 6
  }

  public enum VoteKickDialogStyle
  {
    KickDialogWithSeconds = 1,
    KickDialogWithoutSeconds = 2,
    KickDialogCancelled = 3,
    KickDialogPlayerKicked = 4,
    KickDialogNotKicked = 5
  }

  public enum VoteKickResult
  {
    Ok,
    DontHaveARight,
    DontMeetRequirements
  }

  public enum ClanMasterChangeMessage
  {
    NotInClan = 1,
    NoMatchFound = 2,
    CannotFindMember = 3,
    MemberNotHaveAuthority = 4,
    EntrustMasterAlreadyExist = 5,
    Ok = 6
  }

  public enum ClubMessage
  {
    Ok,
    NotInClan,
    PlayerCannotBeFound,
    YouCannotRegisterMoreThanAClan,
    AlreadyInClan,
    CannotInviteToClan
  }

  public enum ClubJoinMessage
  {
    RegistrationDone,
    SuccessToEnter,
    NotInAnyClan,
    NoMatchFound,
    YouCannotRegister,
    WithdrawMessage,
    MaxPlayerLimit,
    RegistrationNotAvailable,
    WaitingClanApprovation
  }

  public enum FriendState
  {
    NotInList,
    Requesting,
    InList,
    RequestDialog,
    Unk,
    RegisteredInMyList
  }

  public enum FriendAction : uint
  {
    Add,
    Remove,
    Update,
    Decline
  }

  public enum FriendResult
  {
    Ok,
    UserNotExist,
  }
    public enum EnchantResult
    {
        ErrorEnchant,
        NotEnoughMoney,
        ErrorItemEnchant,
        None,
        NotEnoughEffect,
        Reset,
        Success,
        SuccessJackpot,
        SuccessChipOrAnother
    }
}
