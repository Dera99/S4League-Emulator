using System.Collections.Generic;
using System.Threading.Tasks;
using NeoNetsphere.Network;

namespace NeoNetsphere.Commands
{
  internal interface ICommand
  {
    string Name { get; }
    bool AllowConsole { get; }
    SecurityLevel Permission { get; }
    IReadOnlyList<ICommand> SubCommands { get; }

    Task<bool> Execute(GameServer server, Player plr, string[] args);
    string Help();
  }
}