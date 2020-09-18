using System.Threading;
using BlubLib.Threading.Tasks;

namespace NeoNetsphere.Network
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using BlubLib.Collections.Generic;
  using BlubLib.DotNetty.Handlers.MessageHandling;
  using DotNetty.Transport.Channels;
  using DotNetty.Transport.Channels.Sockets;
  using Message.Game;
  using ProudNetSrc;
  using ProudNetSrc.Handlers;
  using Serilog;
  using Serilog.Core;

  internal class MessageHandler<TSession> : ProudMessageHandler
      where TSession : ProudSession
  {
    // ReSharper disable once StaticMemberInGenericType
    public static readonly ILogger Logger = Log.ForContext(Constants.SourceContextPropertyName,
        nameof(MessageHandler<TSession>));

    private readonly IDictionary<Type, List<Predicate<TSession>>> _filter =
        new Dictionary<Type, List<Predicate<TSession>>>();

    private readonly IList<IMessageHandler> _messageHandlers = new List<IMessageHandler>();

    public override async Task<bool> OnMessageReceived(IChannelHandlerContext context, object message)
    {
      _filter.TryGetValue(message.GetType(), out var predicates);

      if (!GetParameter(context, message, out TSession session))
        throw new Exception("Unable to retrieve session");

      if (predicates != null && predicates.Any(predicate => !predicate(session)))
      {
        Logger.Debug("Dropping message {messageName} from client {remoteAddress}", message.GetType().Name,
            ((ISocketChannel)context.Channel).RemoteAddress);
        return false;
      }

      //test
      try
      {
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
          await HandleMessage(context, message).WaitAsync(cts.Token);
      }
      catch (TaskCanceledException)
      {
        await OnUnhandledMessage(context, message, true);
      }
      catch (Exception e)
      {
        context.FireExceptionCaught(e);
      }

      return true;
    }

    private async Task OnUnhandledMessage(IChannelHandlerContext context, object message, bool timeOut = false)
    {
      if (!GetParameter(context, message, out TSession session))
        throw new Exception("Unable to retrieve session");

      var messagename = message.GetType().Name;
      if (message.GetType().Name == "RecvContext")
      {
        var recvContext = (RecvContext)message;
        messagename = recvContext.Message.GetType().Name;
      }

            if (session.GetType() == typeof(GameSession))
            {
                var gameSession = (GameSession)(object)session;
                Logger.ForAccount(gameSession).Error("Unhandled message <{messageName}>, Canceled: {timeout}", messagename, timeOut);

                // test 
                await session.SendAsync(new ServerResultAckMessage(ServerResult.HackingTrialDetected));
                session.Dispose();
            }
            else if (session.GetType() == typeof(ChatSession))
            {
                var gameSession = (ChatSession)(object)session;
                Logger.ForAccount(gameSession).Error("Unhandled message <{messageName}>, Canceled: {timeout}", messagename, timeOut);
                // test 
                await session.SendAsync(new ServerResultAckMessage(ServerResult.HackingTrialDetected));
                session.Dispose();
            }
            else if (session.GetType() == typeof(RelaySession))
            {
                var gameSession = (RelaySession)(object)session;
                Logger.ForAccount(gameSession).Error("Unhandled message <{messageName}>, Canceled: {timeout}", messagename, timeOut);
                // test 
                await session.SendAsync(new ServerResultAckMessage(ServerResult.HackingTrialDetected));
                session.Dispose();
            }
            else
            {
                Logger.Error("Unhandled message {messageName}", messagename);
                // test 
                await session.SendAsync(new ServerResultAckMessage(ServerResult.HackingTrialDetected));
                session.Dispose();
            }

            if (session.GetType() == typeof(GameSession))
      {
        var gameSession = (GameSession)(object)session;
        if (gameSession.Player?.Room == null || !(gameSession.Player?.RoomInfo?.HasLoaded ?? true))
        {
          await gameSession.SendAsync(new ServerResultAckMessage(ServerResult.ServerError));
        }
      }
    }

    private async Task<bool> HandleMessage(IChannelHandlerContext context, object message)
    {
      if (!GetParameter(context, message, out TSession session))
        throw new Exception("Unable to retrieve session");

      foreach (var messageHandler in _messageHandlers)
      {
        try
        {
          if (await messageHandler.OnMessageReceived(context, message))
            return true;
        }
        catch (Exception ex)
        {
          context.FireExceptionCaught(ex);
        }
      }

      await OnUnhandledMessage(context, message);
      return false;
    }

    public MessageHandler<TSession> AddHandler(IMessageHandler handler)
    {
      _messageHandlers.Add(handler);
      return this;
    }

    public MessageHandler<TSession> RegisterRule<T>(params Predicate<TSession>[] predicates)
    {
      if (predicates == null)
        throw new ArgumentNullException(nameof(predicates));

      _filter.AddOrUpdate(typeof(T),
          new List<Predicate<TSession>>(predicates),
          (key, oldValue) =>
          {
            oldValue.AddRange(predicates);
            return oldValue;
          });
      return this;
    }

    public MessageHandler<TSession> RegisterRule<T>(Predicate<TSession> predicate)
    {
      _filter.AddOrUpdate(typeof(T),
          new List<Predicate<TSession>> { predicate },
          (key, oldValue) =>
          {
            oldValue.Add(predicate);
            return oldValue;
          });
      return this;
    }
  }
}