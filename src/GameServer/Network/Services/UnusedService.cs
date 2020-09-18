using System;
using System.Threading.Tasks;
using BlubLib.DotNetty.Handlers.MessageHandling;
using NeoNetsphere.Network.Message.Club;
using NeoNetsphere.Network.Message.Game;
using NeoNetsphere.Network.Message.GameRule;
using NeoNetsphere.Network.Message.Relay;
using ProudNetSrc.Handlers;
using Serilog;
using Serilog.Core;

namespace NeoNetsphere.Network.Services
{
  internal class UnusedService : ProudMessageHandler
  {
    private static readonly ILogger Logger =
        Log.ForContext(Constants.SourceContextPropertyName, nameof(GeneralService));

    [MessageHandler(typeof(CNotifyP2PLogMessage))]
    public void CNotifyP2PLog(RelaySession session, CNotifyP2PLogMessage message)
    {
    }

    [MessageHandler(typeof(GameAvatarDurabilityDecreaseReqMessage))]
    public void GameAvatarDurabilityDecreaseReq(GameSession session, GameAvatarDurabilityDecreaseReqMessage message)
    {
    }

    [MessageHandler(typeof(TaskNotifyReqMessage))]
    public void TaskNotifyReq(GameSession session, TaskNotifyReqMessage message)
    {
      session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
      // Todo
    }

    [MessageHandler(typeof(TaskReguestReqMessage))]
    public void TaskReguestReq(GameSession session, TaskReguestReqMessage message)
    {
      session.SendAsync(new ServerResultAckMessage(ServerResult.FailedToRequestTask));
      // Todo
    }

    [MessageHandler(typeof(MoneyUseCoinReqMessage))]
    public void MoneyUseCoinReq(GameSession session, MoneyUseCoinReqMessage message)
    {
      // Todo
    }
  }
}