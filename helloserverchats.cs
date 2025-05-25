using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("helloserverchats", "Автор", "1.0.0")]  // Имя плагина тоже можно изменить
    [Description("Команда /hello для приветствия игроков")]
    public class helloserverchats : CovalencePlugin  // <-- Класс теперь совпадает с именем файла
    {
        [Command("hello")]
        private void HelloCommandHandler(IPlayer player, string command, string[] args)
        {
            player.Reply("Привет, ты попал на сервер FaS");
        }
    }
}