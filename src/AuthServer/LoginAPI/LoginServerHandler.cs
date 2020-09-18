using System.Net;
using ProudNetSrc;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BlubLib.Threading.Tasks;
using Dapper.FastCrud;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using NeoNetsphere.Database.Auth;
using Serilog;
using Serilog.Core;
using System.Text;
namespace NeoNetsphere.LoginAPI
{
  public class LoginServerHandler : ChannelHandlerAdapter
  {

    private const short Magic = 2002;
    private static readonly ILogger Logger = Log.ForContext(Constants.SourceContextPropertyName, "LoginServer");
    private static readonly object _loginSync = new object();


    public override void ChannelActive(IChannelHandlerContext context)
    {
      base.ChannelActive(context);
      var firstMessage = new CCMessage();
      firstMessage.Write(CCMessage.MessageType.Notify);
      firstMessage.Write("Pharaohs");
      SendA(context, firstMessage);
    }

    public override void ChannelRead(IChannelHandlerContext context, object messageData)
    {
      var buffer = messageData as IByteBuffer;
      var data = new byte[0];
      if (buffer != null) data = buffer.GetIoBuffer().ToArray();

      var msg = new CCMessage(data, data.Length);
      short magic = 0;
      var message = new ByteArray();

      if (msg.Read(ref magic)
          && magic == Magic
          && msg.Read(ref message))
      {
        var receivedMessage = new CCMessage(message);
        CCMessage.MessageType coreId = 0;
        if (!receivedMessage.Read(ref coreId)) return;

        switch (coreId)
        {
          case CCMessage.MessageType.Rmi:
            short rmiId = 0;
            if (receivedMessage.Read(ref rmiId))
            {
              switch (rmiId)
              {
                case 15:

                  var username = string.Empty;
                  var password = string.Empty;
                  var hwid = string.Empty;
                  var secretkey = string.Empty;

                if (receivedMessage.Read(ref secretkey)
                     && receivedMessage.Read(ref hwid)
                      && receivedMessage.Read(ref username)
                      && receivedMessage.Read(ref password))
                  {
                     LoginAsync(context, username, password, hwid , secretkey);
                  }
                  else
                  {
                    var error = new CCMessage();
                    error.Write(false);
                    error.Write("Login error");
                    RmiSend(context, 16, error);
                  }

                  break;

                case 17:
                  context.CloseAsync();
                  break;
                default:
                  Logger.Error("Received unknown rmiId{rmi} from {endpoint}", rmiId,
                      context.Channel.RemoteAddress.ToString());
                  break;
              }
            }
            break;
          case CCMessage.MessageType.Notify:
            context.CloseAsync();
            break;
          default:
            Logger.Error("Received unknown coreID{coreid} from {endpoint}", coreId,
                context.Channel.RemoteAddress.ToString());
            break;
        }
      }
      else
      {
        Logger.Error("Received invalid packetstruct from {endpoint}", context.Channel.RemoteAddress.ToString());
        context.CloseAsync();
      }
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
      context.CloseAsync();
    }

    private static async Task LoginAsync(IChannelHandlerContext context, string username, string password,
        string hwid , string secretkey)
    {
    // This var is used for sending the unban date to the launcher
     var unbandate = DateTimeOffset.Now;
     try
      {
        var endpoint = new IPEndPoint(((IPEndPoint)context.Channel.RemoteAddress).Address.MapToIPv4(),
            ((IPEndPoint)context.Channel.RemoteAddress).Port);
                if (username.Length >= 20)
                {
                    Logger.Error("Login & Password to long for {0}", endpoint);
                    var check = new CCMessage();
                    check.Write(false);
                    check.Write("Username is more than 20 chars");
                    RmiSend(context, 16, check);
                    return;
                }

                if (hwid == string.Empty)
                {
                    Logger.Error("No HWID found on endpoint {0}", endpoint);
                    var check = new CCMessage();
                    check.Write(false);
                    check.Write("Cannot Generate HWID");
                    RmiSend(context, 16, check);
                    return;
                }
               

                if (Config.Instance.AuthAPI.BlockedHWIDS.Contains(hwid))
                {
                    Logger.Error("Hwid ban(conf): {0}, Address: {1}", hwid, endpoint);
                    goto hwidban;
                }

                if (Config.Instance.LauncherCheck)
                {
                    // Launcher Check
                    if (secretkey != Config.Instance.LauncherCheckKey)
                    {
                        Logger.Error("Wrong Launcher => User: " + username + " hwid: " + hwid + " from " + endpoint);
                        var keycheck = new CCMessage();
                        keycheck.Write(false);
                        keycheck.Write("Invalid Launcher");
                        RmiSend(context, 16, keycheck);
                        return;
                    }
                }

          using (var db = AuthDatabase.Open())
           {
          Logger.Information("AuthAPI login from {0}, HWID: {1}", endpoint, hwid);
                

          if (username.Length < 4 || password.Length < 4)
          {
            Logger.Error("Too short credentials for {username} / {endpoint}", username, endpoint);
            var lengtherr = new CCMessage();
            lengtherr.Write(false);
            lengtherr.Write("Invalid length of username/password");
            RmiSend(context, 16, lengtherr);
            return;
          }

          if (!Namecheck.IsNameValid(username))
          {
            Logger.Error("Invalid username for {username} / {endpoint}", username, endpoint);
            var nickerr = new CCMessage();
            nickerr.Write(false);
            nickerr.Write("Username contains invalid characters");
            RmiSend(context, 16, nickerr);
            return;
          }

          var result = await db.FindAsync<AccountDto>(statement => statement
              .Where($"{nameof(AccountDto.Username):C} = @{nameof(username)}")
              .Include<BanDto>(join => join.LeftOuterJoin())
              .WithParameters(new { Username = username }));
          var account = result.FirstOrDefault();

          var hwidResult = await db.FindAsync<HwidBanDto>(statement => statement
              .Where($"{nameof(HwidBanDto.Hwid):C} = @{nameof(hwid)}")
              .WithParameters(new { Hwid = hwid }));

          if (hwidResult?.Any() ?? false)
          {
            Logger.Error("Hwid ban(db): {0}, Address: {1}", hwid, endpoint);
            goto hwidban;
          }

          if (account == null &&
              (Config.Instance.NoobMode || Config.Instance.AutoRegister))
          {
            account = new AccountDto { Username = username };

            var newSalt = new byte[24];
            using (var csprng = new RNGCryptoServiceProvider())
            {
              csprng.GetBytes(newSalt);
            }

            var hash = new byte[24];
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, newSalt, 24000))
            {
              hash = pbkdf2.GetBytes(24);
            }

            account.Password = Convert.ToBase64String(hash);
            account.Salt = Convert.ToBase64String(newSalt);

            await db.InsertAsync(account);
          }

          var salt = Convert.FromBase64String(account?.Salt ?? "");

          var passwordGuess =
              new Rfc2898DeriveBytes(password, salt, 24000).GetBytes(24);
          var actualPassword =
              Convert.FromBase64String(account?.Password ?? "");

          var difference =
              (uint)passwordGuess.Length ^ (uint)actualPassword.Length;

          for (var i = 0;
              i < passwordGuess.Length && i < actualPassword.Length;
              i++)
            difference |= (uint)(passwordGuess[i] ^ actualPassword[i]);

          if ((difference != 0 ||
               string.IsNullOrWhiteSpace(account?.Password ?? "")) &&
              !Config.Instance.NoobMode)
          {
            Logger.Error("Wrong credentials for {username} / {endpoint}", username, endpoint);
            goto wrong;
          }

          if (account != null)
          {
            var now = DateTimeOffset.Now.ToUnixTimeSeconds();
            var ban = account.Bans.FirstOrDefault(b => b.Date + (b.Duration ?? 0) > now);
            if (ban != null)
            {
              var unbanDate = DateTimeOffset.FromUnixTimeSeconds(ban.Date + (ban.Duration ?? 0));
                 unbandate = DateTimeOffset.FromUnixTimeSeconds(ban.Date + (ban.Duration ?? 0));
            Logger.Error("{user} is banned until {until}", account.Username, unbanDate);
              goto ban;
            }

            account.LoginToken = AuthHash
                .GetHash256(
                    $"{context.Channel.RemoteAddress}-{account.Username}-{account.Password}-{endpoint.Address.MapToIPv4()}")
                .ToLower();
            account.LastLogin = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            account.AuthToken = string.Empty;
            account.newToken = string.Empty;
            await db.UpdateAsync(account);

            var history = new AuthHistoryDto
            {
              Account = account,
              AccountId = account.Id,
              Date = DateTimeOffset.Now.ToUnixTimeSeconds(),
              HWID = hwid
            };

            await db.InsertAsync(history);

            var ack = new CCMessage();
            ack.Write(true);
            ack.Write(account.LoginToken);
            RmiSend(context, 16, ack);
          }

          Logger.Information("AuthAPI login success for {username}", username);
          return;
        }
      }
      catch (Exception e)
      {
        Logger.Error(e.ToString());
        goto error;
      }

    wrong:
      {
        var error = new CCMessage();
        error.Write(false);
        error.Write("Invalid username or password");
        RmiSend(context, 16, error);
      }

    error:
      {
        var error = new CCMessage();
        error.Write(false);
        error.Write("Login error");
        RmiSend(context, 16, error);
      }

    ban:
      {
        var error = new CCMessage();
        error.Write(false);
        error.Write("Account is banned until "+ unbandate);
        RmiSend(context, 16, error);
      }

    hwidban:
      {
        var error = new CCMessage();
        error.Write(false);
        error.Write("You have been blocked");
        RmiSend(context, 16, error);
      }
    }

    private static void RmiSend(IChannelHandlerContext ctx, short rmiId, CCMessage message)
    {
      var rmiframe = new CCMessage();
      rmiframe.Write(CCMessage.MessageType.Rmi);
      rmiframe.Write(rmiId);
      rmiframe.Write(message);
      SendA(ctx, rmiframe);
    }

    private static Task SendA(IChannelHandlerContext ctx, CCMessage data)
    {
      var coreframe = new CCMessage();
      coreframe.Write(Magic);
      coreframe.WriteScalar(data.Length);
      coreframe.Write(data);

      var buffer = Unpooled.Buffer(coreframe.Length);
      buffer.WriteBytes(coreframe.Buffer);
      ctx.WriteAndFlushAsyncEx(buffer).WaitEx();
      return Task.CompletedTask;
    }
  }
}
