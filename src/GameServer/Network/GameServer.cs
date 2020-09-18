using System.Threading.Tasks;
using NeoNetsphere.Database.Auth;

namespace NeoNetsphere.Network
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using AuthServer.ServiceModel;
    using BlubLib.DotNetty.Handlers.MessageHandling;
    using BlubLib.IO;
    using BlubLib.Threading;
    using Dapper.FastCrud;
    using ExpressMapper;
    using ExpressMapper.Extensions;
    using Commands;
    using Database.Game;
    using Data.Chat;
    using Data.Club;
    using Data.Game;
    using Data.GameRule;
    using Message.Club;
    using Message.Game;
    using Message.GameRule;
    using Message.Relay;
    using Services;
    using Resource;
    using ProudNetSrc;
    using ProudNetSrc.Serialization;
    using Serilog;
    using Constants = Serilog.Core.Constants;
    using ErrorEventArgs = ProudNetSrc.ErrorEventArgs;

    public struct AccurateDelta
    {
        private Stopwatch _sw;
        private TimeSpan _lastTime;

        public AccurateDelta(TimeSpan initTime)
        {
            _sw = Stopwatch.StartNew();
            _lastTime = initTime;
        }

        public void Reset(TimeSpan delta)
        {
            _lastTime = delta;
            _sw = Stopwatch.StartNew();
        }

        public void Stop()
        {
            _sw.Stop();
        }

        // ReSharper disable once InconsistentNaming
        public TimeSpan delta => _lastTime.Add(TimeSpan.FromMilliseconds(_sw.ElapsedMilliseconds));
    }

    internal class GameServer : ProudServer
    {
        // ReSharper disable once InconsistentNaming
        private static readonly ILogger
            Logger = Log.ForContext(Constants.SourceContextPropertyName, nameof(GameServer));

        private readonly ServerlistMgr _serverlistManager;

        private readonly ILoop _worker;
        private readonly ILoop _worker2;
        private readonly AccurateDelta _workerDelta;
        private readonly AccurateDelta _worker2Delta;

        private TimeSpan _saveTimer;

        public readonly ConcurrentQueue<double> AverageWorkerDelta = new ConcurrentQueue<double>();
        public readonly ConcurrentDictionary<double, byte> HighWorkerDelta = new ConcurrentDictionary<double, byte>();

        private GameServer(Configuration config)
            : base(config)
        {
            RegisterMappings();
            CommandManager = new CommandManager(this);
            CommandManager.Add(new ServerCommand())
                .Add(new ReloadCommand())
                .Add(new GameCommands())
                .Add(new BanCommands())
                .Add(new UnbanCommands())
                .Add(new UserkickCommand())
                .Add(new KickCommand())
                .Add(new AllkickCommand())
                .Add(new RoomkickCommand())
                .Add(new AdminCommands())
                .Add(new NoticeCommand())
                .Add(new WholeNoticeCommand())
                .Add(new ClanCommands())
                .Add(new InventoryCommands())
                .Add(new SearchCommand())
                .Add(new CommandWrapper())
                .Add(new HelpCommand());

            PlayerManager = new PlayerManager();
            ResourceCache = new ResourceCache();
            ChannelManager = new ChannelManager(ResourceCache.GetChannels());
            ClubManager = new ClubManager(ResourceCache.GetClubs());

            _worker = new ThreadLoop(TimeSpan.FromMilliseconds(100), Worker);
            _worker2 = new ThreadLoop(TimeSpan.FromSeconds(1), Worker2);
            _workerDelta = new AccurateDelta(TimeSpan.Zero);
            _worker2Delta = new AccurateDelta(TimeSpan.Zero);

            _serverlistManager = new ServerlistMgr();
            AverageWorkerDelta.Enqueue(0);
            HighWorkerDelta.TryAdd(0, 0);
        }

        public static GameServer Instance { get; private set; }

        public CommandManager CommandManager { get; }
        public PlayerManager PlayerManager { get; }
        public ChannelManager ChannelManager { get; }
        public ClubManager ClubManager { get; set; }
        public ResourceCache ResourceCache { get; }

        public static void Initialize(Configuration config)
        {
            if (Instance != null)
                throw new InvalidOperationException("Server is already initialized");

#if LATESTS4
            config.Version = new Guid("{14229beb-3338-7114-ab92-9b4af78c688f}");
#else
      config.Version = new Guid("{beb92241-8333-4117-ab92-9b4af78c688f}");
#endif

#if OLDUI
            config.Version = new Guid("{beb92241-8333-4117-ab92-9b4af78c688f}");
#endif

            config.MessageFactories = new MessageFactory[]
            {
                new RelayMessageFactory(), new GameMessageFactory(), new GameRuleMessageFactory(),
                new ClubMessageFactory()
            };
            config.SessionFactory = new GameSessionFactory();

            // ReSharper disable InconsistentNaming
            bool MustBeLoggedIn(GameSession session)
            {
                return session.IsLoggedIn();
            }

            bool MustNotBeLoggedIn(GameSession session)
            {
                return !session.IsLoggedIn();
            }

            bool MustBeInChannel(GameSession session)
            {
                return session.Player.Channel != null;
            }

            bool MustBeInRoom(GameSession session)
            {
                return session.Player.Room != null;
            }

            bool MustNotBeInRoom(GameSession session)
            {
                return session.Player.Room == null;
            }

            bool MustBeRoomHost(GameSession session)
            {
                return session.Player.Room.Host == session.Player;
            }

            bool MustBeRoomMaster(GameSession session)
            {
                return session.Player.Room.Master == session.Player;
            }

            // ReSharper restore InconsistentNaming

            config.MessageHandlers = new IMessageHandler[]
            {
                new MessageHandler<GameSession>()
                    .AddHandler(new AuthService())
                    .AddHandler(new CharService())
                    .AddHandler(new GeneralService())
                    .AddHandler(new AdminService())
                    .AddHandler(new ChannelService())
                    .AddHandler(new ShopService())
                    .AddHandler(new InventoryService())
                    .AddHandler(new RoomService())
                    .AddHandler(new ClubService())
                    .AddHandler(new UnusedService())
                    .RegisterRule<LoginRequestReqMessage>(MustNotBeLoggedIn)
                    .RegisterRule<CharacterCreateReqMessage>(MustBeLoggedIn)
                    .RegisterRule<CharacterSelectReqMessage>(MustBeLoggedIn)
                    .RegisterRule<CharacterDeleteReqMessage>(MustBeLoggedIn)
                    .RegisterRule<AdminShowWindowReqMessage>(MustBeLoggedIn)
                    .RegisterRule<AdminActionReqMessage>(MustBeLoggedIn)
                    .RegisterRule<ChannelInfoReqMessage>(MustBeLoggedIn)
                    .RegisterRule<ChannelEnterReqMessage>(MustBeLoggedIn)
                    .RegisterRule<ChannelLeaveReqMessage>(MustBeLoggedIn, MustBeInChannel)
                    .RegisterRule<ItemBuyItemReqMessage>(MustBeLoggedIn)
                    .RegisterRule<RandomShopRollingStartReqMessage>(MustBeLoggedIn)
                    .RegisterRule<ItemUseItemReqMessage>(MustBeLoggedIn)
                    .RegisterRule<ItemRepairItemReqMessage>(MustBeLoggedIn)
                    .RegisterRule<ItemRefundItemReqMessage>(MustBeLoggedIn)
                    .RegisterRule<ItemDiscardItemReqMessage>(MustBeLoggedIn)
                    .RegisterRule<RoomQuickJoinReqMessage>(MustBeLoggedIn, MustBeInChannel, MustNotBeInRoom)
                    .RegisterRule<RoomEnterPlayerReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<RoomMakeReqMessage>(MustBeLoggedIn, MustBeInChannel, MustNotBeInRoom)
                    .RegisterRule<RoomMakeReq2Message>(MustBeLoggedIn, MustBeInChannel, MustNotBeInRoom)
                    .RegisterRule<RoomEnterReqMessage>(MustBeLoggedIn, MustBeInChannel, MustNotBeInRoom)
                    .RegisterRule<RoomLeaveReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<RoomTeamChangeReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<RoomPlayModeChangeReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<ScoreKillReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<ScoreKillAssistReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<ScoreOffenseReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<ScoreOffenseAssistReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<ScoreDefenseReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<ScoreDefenseAssistReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<ScoreTeamKillReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<ScoreHealAssistReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<ScoreSuicideReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<ScoreReboundReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom,
                        session => session.Player.RoomInfo.State != PlayerState.Lobby &&
                                   session.Player.RoomInfo.State != PlayerState.Spectating)
                    .RegisterRule<ScoreGoalReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom,
                        session => session.Player.RoomInfo.State != PlayerState.Lobby &&
                                   session.Player.RoomInfo.State != PlayerState.Spectating)
                    .RegisterRule<RoomBeginRoundReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom,
                        MustBeRoomMaster)
                    .RegisterRule<RoomReadyRoundReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom,
                        session => session.Player.RoomInfo.State == PlayerState.Lobby)
                    .RegisterRule<RoomBeginRoundReq2Message>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom,
                        MustBeRoomMaster)
                    .RegisterRule<GameLoadingSuccessReqMessage>(MustBeLoggedIn, MustBeInChannel)
                    .RegisterRule<RoomReadyRoundReq2Message>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom,
                        session => session.Player.RoomInfo.State == PlayerState.Lobby)
                    .RegisterRule<GameEventMessageReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<RoomItemChangeReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<GameAvatarChangeReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
                    .RegisterRule<RoomChangeRuleNotifyReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom,
                        MustBeRoomMaster,
                        session =>
                            session.Player.Room.GameRuleManager.GameRule.StateMachine.IsInState(GameRuleState.Waiting))
                    .RegisterRule<RoomChangeRuleNotifyReq2Message>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom,
                        MustBeRoomMaster,
                        session =>
                            session.Player.Room.GameRuleManager.GameRule.StateMachine.IsInState(GameRuleState.Waiting))
                    .RegisterRule<ClubAddressReqMessage>(MustBeLoggedIn, MustBeInChannel)
                    .RegisterRule<RoomLeaveReguestReqMessage>(MustBeLoggedIn, MustBeInChannel, MustBeInRoom)
            };

#if DEBUG
      config.Logger = Logger;
#endif
            Instance = new GameServer(config);
        }

        public void BroadcastNotice(string message)
        {
            Broadcast(new NoticeAdminMessageAckMessage(message));
        }

        private void Worker2(TimeSpan deltax)
        {
            var delta = _worker2Delta;
            delta.Reset(deltax);

            Parallel.ForEach(PlayerManager, (plr) =>
            {
                try
                {
                    if (plr.Session == null && !plr.NeedsToSave)
                        plr.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex.ToString());
                }
            });

            Parallel.ForEach(Sessions.Values, (session) =>
            {
                try
                {
                    var gameSession = (GameSession)session;
                    var plr = gameSession.Player;
                    if (plr == null)
                        return;

                    if (Config.Instance.ACMode == 2 && plr.BE.HB_Enabled)
                    {
                        plr.BE.HB_Last += delta.delta;

                        if (plr.BE.HB_Last.TotalSeconds < 30)
                            return;

                        if (plr.BE.HB_Last_Issued && plr.BE.HB_Info_Received == 2)
                        {
                            plr.BE.HB_Last_Issued = false;
                            plr.BE.HB_Last = TimeSpan.Zero;

                            using (var writer = new BinaryWriter(new MemoryStream()))
                            {
                                writer.Write((byte)9);
                                writer.Write((ushort)plr.BE.HB_Count++);
                                gameSession.SendAsync(new BattleyeS2CDataMessage(writer.ToArray()));
                            }
                        }
                        else if (!plr.BE.HB_Last_Issued || plr.BE.HB_Info_Received == 1)
                        {
                            Logger.ForAccount(plr)
                                .Warning("[BE] Player {0}: Timeout", plr.Account.Nickname);
                            plr.SendAsync(new ServerResultAckMessage(ServerResult.HackingTrialDetected));
                            plr.Disconnect();
                            return;
                        }
                    }

                    if (session.ConnectDate.Add(TimeSpan.FromMinutes(5)) <
                              DateTimeOffset.Now)
                    {
                        if (plr.IsLoggedIn())
                            return;

                        Logger.ForAccount(gameSession)
                            .Information("Inactivity/GSTimeout notice");

                        gameSession.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex.ToString());
                }
            });

            _saveTimer = _saveTimer.Add(delta.delta);
            if (_saveTimer > Config.Instance.SaveInterval)
            {
                _saveTimer = TimeSpan.Zero;

                var players = PlayerManager.Where(plr => plr.IsLoggedIn());
                var enumerable = players as Player[] ?? players.ToArray();
                if (!enumerable.Any())
                    return;

                foreach (var plr in enumerable)
                {
                    try
                    {
                        plr.Save();
                    }
                    catch (Exception ex)
                    {
                        Logger.ForAccount(plr)
                            .Error(ex, "Failed to save playerdata");
                    }
                }

                Logger.Information("Playerdata save completed.");
            }
        }

        private void Worker(TimeSpan deltax)
        {
            var delta = _workerDelta;
            delta.Reset(deltax);

            ChannelManager.Update(delta);

            if (AverageWorkerDelta.Count == 30)
                AverageWorkerDelta.TryDequeue(out _);
            AverageWorkerDelta.Enqueue(delta.delta.TotalMilliseconds);

            if (HighWorkerDelta.Count == 10)
            {
                var lowest = HighWorkerDelta.Keys.Min();

                if (lowest < HighWorkerDelta.Keys.Average())
                    HighWorkerDelta.TryRemove(lowest, out _);
            }
            else if (delta.delta.TotalMilliseconds > HighWorkerDelta.Keys.Min())
                HighWorkerDelta.TryAdd(delta.delta.TotalMilliseconds, 0);

            if (delta.delta.TotalMilliseconds > HighWorkerDelta.Keys.Max())
                HighWorkerDelta.TryAdd(delta.delta.TotalMilliseconds, 0);
        }

        private static void RegisterMappings()
        {
            Mapper.Register<GameServer, ServerInfoDto>()
                .Member(dest => dest.ApiKey, src => Config.Instance.AuthAPI.ApiKey)
                .Member(dest => dest.Id, src => Config.Instance.Id)
                .Member(dest => dest.Name,
                    src =>
                        $"{Config.Instance.Name}")
                .Member(dest => dest.PlayerLimit, src => Config.Instance.PlayerLimit)
                .Member(dest => dest.PlayerOnline, src => src.Sessions.Count)
                .Member(dest => dest.EndPoint,
                    src => new IPEndPoint(IPAddress.Parse(Config.Instance.IP), Config.Instance.Listener.Port))
                .Member(dest => dest.ChatEndPoint,
                    src => new IPEndPoint(IPAddress.Parse(Config.Instance.IP), Config.Instance.ChatListener.Port));

            Mapper.Register<Player, PlayerAccountInfoDto>()
                .Function(dest => dest.IsGM, src => src.Account.SecurityLevel > SecurityLevel.Tester)
                .Member(dest => dest.GameTime, src => TimeSpan.Parse(src.PlayTime))
                .Member(dest => dest.TotalExp, src => src.TotalExperience)
                .Function(dest => dest.TutorialState,
                    src => (uint)(Config.Instance.Game.EnableTutorial ? src.TutorialState : 1))
                .Member(dest => dest.Nickname, src => src.Account.Nickname)
                .Member(dest => dest.TotalMatches, src => src.TotalLosses + src.TotalWins)
                .Member(dest => dest.MatchesWon, src => src.TotalWins)
                .Member(dest => dest.MatchesLost, src => src.TotalLosses)
                .Member(dest => dest.BRStats, src => src.stats.BattleRoyal.GetStatsDto())
                .Member(dest => dest.ChaserStats, src => src.stats.Chaser.GetStatsDto())
                .Member(dest => dest.CPTStats, src => src.stats.Captain.GetStatsDto())
                .Member(dest => dest.DMStats, src => src.stats.DeathMatch.GetStatsDto())
                .Member(dest => dest.TDStats, src => src.stats.TouchDown.GetStatsDto())
                .Member(dest => dest.SiegeStats, src => src.stats.Siege.GetStatsDto());

            Mapper.Register<Channel, ChannelInfoDto>()
                .Member(dest => dest.PlayersOnline, src => src.Players.Count);

            Mapper.Register<PlayerItem, ItemDto>()
                .Member(dest => dest.Id, src => src.Id)
                .Function(dest => dest.ExpireTime, src => src.CalculateExpireTime())
                .Function(dest => dest.EnchantMP, src => src.EnchantMP)
                .Function(dest => dest.EnchantLevel, src => src.EnchantLvl)
                .Function(dest => dest.Durability, src =>
                {
                    if (src.PeriodType == ItemPeriodType.Units) return (int)src.Count;
                    return src.Durability;

                })
                .Function(dest => dest.Effects, src =>
                {
                    var desteffects = new List<ItemEffectDto>();
                    src.Effects.ToList().ForEach(eff => { desteffects.Add(new ItemEffectDto { Effect = eff }); });
                    return desteffects.ToArray();
                });

            Mapper.Register<Deny, DenyDto>()
                .Member(dest => dest.AccountId, src => src.DenyId)
                .Member(dest => dest.Nickname, src => src.Nickname);

            Mapper.Register<PlayerItem, Data.P2P.ItemDto>()
                .Function(dest => dest.ItemNumber, src => src?.ItemNumber ?? 0);

            Mapper.Register<RoomCreationOptions, ChangeRuleDto>()
                .Function(dest => dest.GameRule, src => src.GameRule)
                .Member(dest => dest.MapId, src => (byte)src.MapId)
                .Member(dest => dest.PlayerLimit, src => src.PlayerLimit)
                .Member(dest => dest.Points, src => src.ScoreLimit)
                .Member(dest => dest.Time, src => (byte)src.TimeLimit.TotalMinutes)
                .Member(dest => dest.ItemLimit, src => src.ItemLimit)
                .Member(dest => dest.Password, src => src.Password)
                .Member(dest => dest.Name, src => src.Name)
                .Member(dest => dest.HasSpectator, src => src.HasSpectator)
                .Member(dest => dest.SpectatorLimit, src => src.SpectatorLimit);

            Mapper.Register<RoomCreationOptions, ChangeRuleDto2>()
                .Function(dest => dest.GameRule, src => src.GameRule)
                .Member(dest => dest.MapId, src => (byte)src.MapId)
                .Member(dest => dest.PlayerLimit, src => src.PlayerLimit)
                .Member(dest => dest.Points, src => src.ScoreLimit)
                .Value(dest => dest.Unk1, 0)
                .Member(dest => dest.Time, src => (byte)src.TimeLimit.TotalMinutes)
                .Member(dest => dest.ItemLimit, src => src.ItemLimit)
                .Member(dest => dest.Password, src => src.Password)
                .Member(dest => dest.Name, src => src.Name)
                .Member(dest => dest.HasSpectator, src => src.HasSpectator)
                .Member(dest => dest.SpectatorLimit, src => src.SpectatorLimit)
                .Member(dest => dest.ChangeRuleId, src => src.ChangeRuleId)
                .Member(dest => dest.FMBurnMode, src => src.GetFMBurnModeInfo());

            Mapper.Register<Mail, NoteDto>()
                .Function(dest => dest.ReadCount, src => src.IsNew ? 0 : 1)
                .Function(dest => dest.DaysLeft,
                    src => DateTimeOffset.Now < src.Expires ? (src.Expires - DateTimeOffset.Now).TotalDays : 0)
                .Function(dest => dest.IsGift, src => src.IsClan)
                .Function(dest => dest.Sender, src => src.IsClan ? string.Empty : src.Sender);

            Mapper.Register<Mail, NoteContentDto>()
                .Member(dest => dest.Id, src => src.Id)
                .Member(dest => dest.Message, src => src.Message)
                .Function(dest => dest.Unk1, src => src.IsClan ? 0x1 : 0x0)
                .Function(dest => dest.Unk2, src => src.IsClan ? 0x1 : 0x0);

            Mapper.Register<PlayerItem, ItemDurabilityInfoDto>()
                .Member(dest => dest.ItemId, src => src.Id)
                .Function(dest => dest.Durabilityloss, src =>
                {
                    var loss = src.DurabilityLoss;
                    src.DurabilityLoss = 0;
                    return loss;
                });

            Mapper.Register<Player, PlayerInfoShortDto>()
                .Function(dest => dest.AccountId, src => src?.Account?.Id ?? 0)
                .Function(dest => dest.Nickname, src => src?.Account?.Nickname ?? "n/A")
                .Function(dest => dest.IsGM, src => src?.Account?.SecurityLevel > SecurityLevel.Tester)
                .Function(dest => dest.TotalExp, src => src?.TotalExperience ?? 0);

            Mapper.Register<Player, PlayerLocationDto>()
                .Function(dest => dest.ChannelId, src => src.Channel?.Id > 0 ? (int)src?.Channel?.Id : -1)
                .Function(dest => dest.RoomId, src => src.Room?.Id > 0 ? (int)src?.Room?.Id : -1)
                .Function(dest => dest.ServerGroupId,
                    src => Instance.PlayerManager.Contains(src.Account.Id) ? Config.Instance.Id : -1)
                .Function(dest => dest.GameServerId,
                    src => Instance.PlayerManager.Contains(src.Account.Id) ? Config.Instance.Id : -1) // TODO Server ids
                .Function(dest => dest.ChatServerId,
                    src => Instance.PlayerManager.Contains(src.Account.Id) ? Config.Instance.Id : -1);

            Mapper.Register<Player, PlayerInfoDto>()
                .Function(dest => dest.Info, src => src.Map<Player, PlayerInfoShortDto>())
                .Function(dest => dest.Location, src => src.Map<Player, PlayerLocationDto>());

            Mapper.Register<Player, UserDataDto>()
                .Member(dest => dest.TotalExp, src => src.TotalExperience)
                .Function(dest => dest.AccountId, src => src.Account?.Id ?? 0)
                .Function(dest => dest.Nickname, src => src.Account?.Nickname ?? "n/A")
                .Member(dest => dest.GameTime, src => TimeSpan.Parse(src.PlayTime))
                .Member(dest => dest.TotalMatches, src => src.TotalMatches)
                .Member(dest => dest.MatchesWon, src => src.TotalWins)
                .Member(dest => dest.MatchesLost, src => src.TotalLosses)
                .Member(dest => dest.Level, src => src.Level)
                .Member(dest => dest.BattleRoyalStats, src => src.stats.BattleRoyal.GetUserDataDto())
                .Member(dest => dest.BRScore, src => 0)
                .Member(dest => dest.CaptainStats, src => src.stats.Captain.GetUserDataDto())
                .Member(dest => dest.CaptainScore, src => 0)
                .Member(dest => dest.ChaserStats, src => src.stats.Chaser.GetUserDataDto())
                .Member(dest => dest.ChaserSurvivability, src => 0)
                .Member(dest => dest.DMStats, src => src.stats.DeathMatch.GetUserDataDto())
                .Member(dest => dest.DMScore, src => 0)
                .Member(dest => dest.TDStats, src => src.stats.TouchDown.GetUserDataDto())
                .Member(dest => dest.TDScore, src => 0)
                .Member(dest => dest.SiegeStats, src => src.stats.Siege.GetUserDataDto())
                .Member(dest => dest.SiegeScore, src => 0);
         
            Mapper.Register<Player, PlayerNameTagInfoDto>()
                .Member(dest => dest.AccountId, src => src.Account.Id);

            Mapper.Register<Player, ClubMyInfoDto>()
                .Function(dest => dest.Id, src => src.Club?.Id ?? 0)
                .Function(dest => dest.Name, src => src.Club?.ClanName ?? string.Empty)
                .Function(dest => dest.Rank, src => src.Club?.GetPlayer(src.Account.Id)?.Rank ?? ClubRank.Regular)
                .Function(dest => dest.Type, src => src.Club?.ClanIcon ?? string.Empty)
                .Function(dest => dest.State, src => src.Club?[src.Account?.Id ?? 0].State ?? 0);

            Mapper.Register<Player, PlayerClubInfoDto>()
                .Function(dest => dest.Id, src => src.Club?.Id ?? 0)
                .Function(dest => dest.Name, src => src.Club?.ClanName ?? string.Empty)
                .Function(dest => dest.Type, src => src.Club?.ClanIcon ?? string.Empty);

            Mapper.Register<Player, ClubMemberDto2>()
                .Function(dest => dest.AccountId, src => src.Account?.Id ?? 0)
                .Function(dest => dest.Nickname, src => src.Account?.Nickname ?? "n/A")
                .Function(dest => dest.ServerId, src => Instance.PlayerManager.Contains(src.Account.Id) ? Config.Instance.Id : -1)
                .Function(dest => dest.ChannelId, src => src.Channel?.Id > 0 ? src.Channel.Id : -1)
                .Function(dest => dest.RoomId, src => src.Room?.Id > 0 ? (int)src.Room.Id : -1)
                .Function(dest => dest.ClanRank, src => src.Club?.GetPlayer(src.Account.Id)?.Rank ?? ClubRank.Regular)
                .Function(dest => dest.LastLogin, src => src.Account?.AccountDto?.LastLogin ?? string.Empty);

            Mapper.Register<ClubPlayerInfo, ClubMemberDto2>()
                .Function(dest => dest.AccountId, src => src.AccountId)
                .Function(dest => dest.Nickname, src => src.Account?.Nickname ?? "n/A")
                .Function(dest => dest.LastLogin, src => src.Account.LastLogin ?? string.Empty)
                .Function(dest => dest.ClanRank, src => src.Rank)
                .Value(dest => dest.ServerId, -1)
                .Value(dest => dest.ChannelId, -1)
                .Value(dest => dest.RoomId, -1);

            Mapper.Register<Player, ClubMemberDto>()
                .Function(dest => dest.AccountId, src => src.Account?.Id ?? 0)
                .Function(dest => dest.Nickname, src => src.Account?.Nickname ?? "n/A")
                .Function(dest => dest.ClanRank, src => src.Club?.GetPlayer(src.Account.Id)?.Rank ?? ClubRank.Regular)
                .Function(dest => dest.LastLogin, src => src.Account?.AccountDto?.LastLogin ?? string.Empty);

            Mapper.Register<ClubPlayerInfo, ClubMemberDto>()
                .Function(dest => dest.AccountId, src => src.AccountId)
                .Function(dest => dest.Nickname, src => src.Account?.Nickname ?? "n/A")
                .Function(dest => dest.LastLogin, src => src.Account.LastLogin ?? string.Empty)
                .Function(dest => dest.ClanRank, src => src.Rank);

            Mapper.Register<Player, ClubInfoDto>()
                .Function(dest => dest.Id, src => src.Club?.Id ?? 0)
                .Function(dest => dest.Name, src => src.Club?.ClanName ?? "n/A")
                .Function(dest => dest.MasterName,
                    src => src.Club?.Players.Values.FirstOrDefault(x => x.Rank == ClubRank.Master)?.Account?.Nickname ??
                           string.Empty)
                .Function(dest => dest.MemberCount, src => src.Club?.Count + 5 ?? 0)
                .Function(dest => dest.Type, src => src.Club?.ClanIcon ?? string.Empty);

            Mapper.Register<Player, ClubInfoDto2>()
                .Function(dest => dest.Id, src => src.Club?.Id ?? 0)
                .Function(dest => dest.Id2, src => src.Club?.Id ?? 0)
                .Function(dest => dest.Name, src => src.Club?.ClanName ?? "n/A")
                .Function(dest => dest.MasterName,
                    src => src.Club?.Players.Values.FirstOrDefault(x => x.Rank == ClubRank.Master)?.Account?.Nickname ??
                           string.Empty)
                .Function(dest => dest.MemberCount, src => src.Club?.Count + 5 ?? 0)
                .Function(dest => dest.Type, src => src.Club?.ClanIcon ?? string.Empty);

            Mapper.Register<Friend, PlayerInfoDto>()
                .Function(dest => dest.Info, src =>
                {
                    var plr = Instance.PlayerManager.Get(src.FriendId);

                    if (plr != null)
                    {
                        return plr.Map<Player, PlayerInfoShortDto>();
                    }

                    using (var authdb = AuthDatabase.Open())
                    using (var db = GameDatabase.Open())
                    {
                        var accountDto = DbUtil.Find<AccountDto>(authdb, statement => statement
                            .Where($"{nameof(AccountDto.Id):C} = @Id")
                            .WithParameters(new { Id = src.FriendId })).FirstOrDefault();

                        var playerDto = DbUtil.Find<PlayerDto>(db, statement => statement
                            .Where($"{nameof(PlayerDto.Id):C} = @Id")
                            .WithParameters(new { Id = src.FriendId })).FirstOrDefault();

                        if (playerDto != null && accountDto != null)
                        {
                            return new PlayerInfoShortDto(src.FriendId, accountDto.Nickname,
                                playerDto.TotalExperience,
                                (SecurityLevel)accountDto.SecurityLevel >= SecurityLevel.GameSage);
                        }

                        if (accountDto != null)
                        {
                            return new PlayerInfoShortDto(src.FriendId, accountDto.Username, 0,
                                ((SecurityLevel)accountDto.SecurityLevel) >= SecurityLevel.GameSage);
                        }

                        return new PlayerInfoShortDto(0, string.Empty, 0, false);
                    }
                })
                .Function(dest => dest.Location, src =>
                {
                    var plr = Instance.PlayerManager.Get(src.FriendId);
                    return plr?.Map<Player, PlayerLocationDto>() ?? new PlayerLocationDto();
                });

            Mapper.Register<ClubPlayerInfo, PlayerInfoDto>()
                .Function(dest => dest.Info, src =>
                {
                    var plr = Instance.PlayerManager.Get(src.AccountId);

                    if (plr != null)
                    {
                        return plr.Map<Player, PlayerInfoShortDto>();
                    }

                    using (var db = GameDatabase.Open())
                    {
                        var playerDto = DbUtil.Find<PlayerDto>(db, statement => statement
                            .Where($"{nameof(PlayerDto.Id):C} = @Id")
                            .WithParameters(new { Id = src.AccountId })).FirstOrDefault();

                        if (playerDto != null)
                        {
                            return new PlayerInfoShortDto(src.AccountId, src.Account?.Nickname ?? string.Empty,
                                playerDto.TotalExperience,
                                (SecurityLevel)src.Account.SecurityLevel >= SecurityLevel.GameSage);
                        }

                        return new PlayerInfoShortDto(src.AccountId, src.Account?.Nickname ?? string.Empty, 0,
                            ((SecurityLevel)(src.Account?.SecurityLevel ?? 0)) >= SecurityLevel.GameSage);
                    }
                })
                .Function(dest => dest.Location, src =>
                {
                    var plr = Instance.PlayerManager.Get(src.AccountId);
                    return plr?.Map<Player, PlayerLocationDto>() ?? new PlayerLocationDto();
                });

            Mapper.Compile(CompilationTypes.Source);
        }

        #region Events

        protected override void OnStarted()
        {
            ResourceCache.PreCache();
            _worker.Start();
            _worker2.Start();
            _workerDelta.Reset(TimeSpan.Zero);
            _worker2Delta.Reset(TimeSpan.Zero);
            _serverlistManager.Start();
        }

        protected override void OnStopping()
        {
            _workerDelta.Stop();
            _worker2Delta.Stop();
            _worker.Stop(new TimeSpan(0));
            _worker2.Stop(new TimeSpan(0));
            _serverlistManager.Dispose();
        }

        protected override void OnDisconnected(ProudSession session)
        {
            try
            {
                var gameSession = (GameSession)session;
                if (gameSession.Player != null)
                {
                    gameSession.Player.Room?.Leave(gameSession.Player);
                    gameSession.Player.Channel?.Leave(gameSession.Player);
                    gameSession.Player.Save();

                    PlayerManager.Remove(gameSession.Player);

                    Logger.ForAccount(gameSession.Player)
                        .Information($"Client {session.RemoteEndPoint} disconnected");

                    if (gameSession.Player.ChatSession != null)
                    {
                        Club.LogOff(gameSession.Player);
                        gameSession.Player.ChatSession.GameSession = null;
                        gameSession.Player.ChatSession.Dispose();
                    }

                    if (gameSession.Player.RelaySession != null)
                    {
                        gameSession.Player.RelaySession.GameSession = null;
                        gameSession.Player.RelaySession.Dispose();
                    }

                    gameSession.Player.Session = null;
                    gameSession.Player.ChatSession = null;
                    gameSession.Player.RelaySession = null;
                    gameSession.Player.Dispose();
                    gameSession.Player = null;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e.ToString());
            }

            base.OnDisconnected(session);
        }

        protected override void OnError(ErrorEventArgs e)
        {
            var gameSession = (GameSession)e.Session;
            var log = Logger;
            if (e.Session != null)
                log = log.ForAccount((GameSession)e.Session);

            if (e.Exception.ToString().ToLower().Contains("opcode") ||
                e.Exception.ToString().ToLower().Contains("rmi") ||
                e.Exception.ToString().ToLower().Contains("bad format in"))
            {
                log.Warning(e.Exception.InnerException.Message);
                if (gameSession?.Player?.Room == null || !gameSession.Player.RoomInfo.HasLoaded)
                    gameSession?.SendAsync(new ServerResultAckMessage(ServerResult.ServerError));
            }
            else
            {
                log.Error(e.Exception, "Unhandled server error");
                if (gameSession?.Player?.Room == null || !gameSession.Player.RoomInfo.HasLoaded)
                    gameSession?.SendAsync(new ServerResultAckMessage(ServerResult.ServerError));
            }

            base.OnError(e);
        }

        #endregion
    }
}
