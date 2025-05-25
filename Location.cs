using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Location", "YourName", "1.0.0")]
    [Description("Показывает текущие координаты игрока")]
    class Location : RustPlugin
    {
        [ChatCommand("location")]
        void LocationCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            
            Vector3 pos = player.transform.position;
            string message = $"Ваши координаты: Z={pos.z:F2}, Y={pos.y:F2}, X={pos.x:F2}";
            
            SendReply(player, message);
            
            // Альтернативный вариант вывода в чат
            // player.ChatMessage(message);
        }
    }
}