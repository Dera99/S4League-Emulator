namespace ProudNetSrc.Handlers
{
  using System;
  using System.Net.Sockets;
  using DotNetty.Codecs;
  using DotNetty.Transport.Channels;

  internal class ErrorHandler : ChannelHandlerAdapter
  {
    private readonly ProudServer _server;

    public ErrorHandler(ProudServer server)
    {
      _server = server;
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
      if (exception == null)
        return;

      if (exception is SocketException socketException)
      {
        if (socketException.SocketErrorCode == SocketError.ConnectionReset)
          return;
      }

      var session = context.Channel.GetAttribute(ChannelAttributes.Session).Get();

      var exc = exception.GetBaseException();
      if (exception.GetType() == typeof(SocketException) ||
          exception.GetType() == typeof(ClosedChannelException) ||
          exception.GetType() == typeof(ProudFrameException) ||
          exc.GetType() == typeof(SocketException) ||
          exc.GetType() == typeof(ClosedChannelException) ||
          exc.GetType() == typeof(ProudFrameException))
      {
        session?.CloseAsync();
        return;
      }

      _server.Configuration.Logger?.Error(exception, "Unhandled exception");
      _server.RaiseError(new ErrorEventArgs(session, exception));
    }
  }
}
