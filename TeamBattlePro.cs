using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using System;
using UnityEngine;
using Oxide.Game.Rust.Cui;
using Network;
using Facepunch;

namespace Oxide.Plugins
{
    [Info("TeamBattlePro", "YourName", "1.4.2")]
    [Description("Продвинутая система командных сражений")]
    public class TeamBattlePro : RustPlugin
        #region Конфигурация
        
        private const int MAX_ROOMS = 4;
        private const int MAX_TEAM_PLAYERS = 5;
        private const int DEFAULT_COUNTDOWN = 60;
        private const int FAST_COUNTDOWN = 10;
        private const float RESPAWN_DELAY = 2f;

        private const string BG_COLOR = "0.12 0.12 0.15 0.98";
        private const string TITLE_COLOR = "0.9 0.9 0.9 1";
        private const string BUTTON_BLUE = "0.2 0.3 0.8 1";
        private const string BUTTON_RED = "0.8 0.2 0.2 1";
        private const string BUTTON_GREEN = "0.2 0.8 0.2 1";
        private const string BUTTON_DISABLED = "0.3 0.3 0.3 0.5";

        private readonly Dictionary<int, Tuple<Vector3, Vector3>> RoomSpawns = new Dictionary<int, Tuple<Vector3, Vector3>>
        {
            [1] = Tuple.Create(new Vector3(1005, 17, -310), new Vector3(1033, 17, -266)),
            [2] = Tuple.Create(new Vector3(500, 10, 200), new Vector3(600, 10, 200)),
            [3] = Tuple.Create(new Vector3(-150, 10, 400), new Vector3(-250, 10, 400)),
            [4] = Tuple.Create(new Vector3(-300, 10, -200), new Vector3(-400, 10, -200))
        };

        #endregion

        #region Данные

        private Dictionary<int, Dictionary<string, List<BasePlayer>>> Rooms = new Dictionary<int, Dictionary<string, List<BasePlayer>>>();
        private Dictionary<int, Timer> RoomTimers = new Dictionary<int, Timer>();
        private Dictionary<int, int> RoomCountdowns = new Dictionary<int, int>();
        private Dictionary<int, bool> RoomLocked = new Dictionary<int, bool>();
        private Dictionary<BasePlayer, Tuple<int, string>> PlayerData = new Dictionary<BasePlayer, Tuple<int, string>>();

        #endregion

        #region Инициализация

        void Init()
        {
            for (int i = 1; i <= MAX_ROOMS; i++)
            {
                Rooms[i] = new Dictionary<string, List<BasePlayer>>
                {
                    ["Team1"] = new List<BasePlayer>(),
                    ["Team2"] = new List<BasePlayer>()
                };
                RoomCountdowns[i] = DEFAULT_COUNTDOWN;
                RoomLocked[i] = false;
            }
        }

        void Unload()
        {
            foreach (var timer in RoomTimers.Values) timer?.Destroy();
            RoomTimers.Clear();
            PlayerData.Clear();
            Rooms.Clear();
            RoomCountdowns.Clear();
            RoomLocked.Clear();
        }

        #endregion

        #region Команды

        [ChatCommand("play")]
        void PlayCommand(BasePlayer player, string command, string[] args)
        {
            DestroyUI(player);
            ShowRoomSelection(player);
        }

        [ConsoleCommand("teambattlepro.selectroom")]
        void SelectRoomCMD(ConsoleSystem.Arg arg)
        {
            var player = arg.Player() as BasePlayer;
            if (player == null) return;

            if (!int.TryParse(arg.Args?[0], out int roomId) || roomId < 1 || roomId > MAX_ROOMS)
            {
                player.ChatMessage("Некорректный номер комнаты!");
                return;
            }

            if (RoomLocked[roomId])
            {
                player.ChatMessage("Комната уже в игре!");
                return;
            }

            DestroyUI(player);
            ShowTeamSelection(player, roomId);
        }

        [ConsoleCommand("teambattlepro.selectteam")]
        void SelectTeamCMD(ConsoleSystem.Arg arg)
        {
            var player = arg.Player() as BasePlayer;
            if (player == null) return;

            if (!int.TryParse(arg.Args?[0], out int roomId) || roomId < 1 || roomId > MAX_ROOMS)
            {
                player.ChatMessage("Ошибка выбора комнаты!");
                return;
            }

            if (RoomLocked[roomId])
            {
                player.ChatMessage("Игра уже началась!");
                return;
            }

            string team = arg.Args?[1];
            if (string.IsNullOrEmpty(team) || !Rooms[roomId].ContainsKey(team))
            {
                player.ChatMessage("Ошибка выбора команды!");
                return;
            }

            if (Rooms[roomId][team].Count >= MAX_TEAM_PLAYERS)
            {
                player.ChatMessage("Команда заполнена!");
                return;
            }

            RemovePlayerFromAllRooms(player);
            Rooms[roomId][team].Add(player);
            PlayerData[player] = Tuple.Create(roomId, team);
            player.ChatMessage($"<color=#00FF00>Вы в команде {team} (Комната {roomId})</color>");
            
            UpdateRoomUI(roomId);
            ManageRoomTimer(roomId);
        }

        [ConsoleCommand("teambattlepro.leaveroom")]
        void LeaveRoomCMD(ConsoleSystem.Arg arg)
        {
            var player = arg.Player() as BasePlayer;
            if (player == null) return;

            if (!PlayerData.TryGetValue(player, out var data))
            {
                player.ChatMessage("Вы не в комнате!");
                return;
            }

            if (RoomLocked[data.Item1])
            {
                player.ChatMessage("Нельзя выйти во время игры!");
                return;
            }

            RemovePlayerFromAllRooms(player);
            DestroyUI(player);
            player.ChatMessage("<color=#FFA500>Вы покинули комнату</color>");
            ManageRoomTimer(data.Item1);
        }

        [ConsoleCommand("teambattlepro.closeui")]
        void CloseUICMD(ConsoleSystem.Arg arg)
        {
            var player = arg.Player() as BasePlayer;
            if (player != null) DestroyUI(player);
        }

        #endregion

        #region UI

        void ShowRoomSelection(BasePlayer player)
        {
            var cui = new CuiElementContainer();
            string panel = cui.Add(new CuiPanel
            {
                Image = { Color = BG_COLOR },
                RectTransform = { AnchorMin = "0.25 0.2", AnchorMax = "0.75 0.8" },
                CursorEnabled = true
            }, "Overlay", "RoomSelectPanel");

            cui.Add(new CuiLabel
            {
                Text = { Text = "⚔ ВЫБОР КОМНАТЫ", FontSize = 22, Align = TextAnchor.MiddleCenter, Color = TITLE_COLOR },
                RectTransform = { AnchorMin = "0.1 0.88", AnchorMax = "0.9 0.95" }
            }, panel, "Title");

            for (int i = 1; i <= MAX_ROOMS; i++)
            {
                int playersCount = Rooms[i]["Team1"].Count + Rooms[i]["Team2"].Count;
                bool isLocked = RoomLocked[i];
                string status = isLocked ? "🔒 ИДЕТ ИГРА" : $"👥 {playersCount}/{MAX_TEAM_PLAYERS*2}";

                cui.Add(new CuiButton
                {
                    Button = { 
                        Command = isLocked ? "" : $"teambattlepro.selectroom {i}", 
                        Color = isLocked ? BUTTON_DISABLED : BUTTON_GREEN,
                        Close = panel
                    },
                    RectTransform = { AnchorMin = $"0.1 {0.75 - (i * 0.15)}", AnchorMax = $"0.9 {0.85 - (i * 0.15)}" },
                    Text = { 
                        Text = $"🏟 КОМНАТА {i}\n{status}", 
                        FontSize = 16, 
                        Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 1"
                    }
                }, panel, $"RoomBtn_{i}");
            }

            cui.Add(new CuiButton
            {
                Button = { Command = "teambattlepro.closeui", Color = BUTTON_RED, Close = panel },
                RectTransform = { AnchorMin = "0.1 0.05", AnchorMax = "0.9 0.12" },
                Text = { Text = "✖ ЗАКРЫТЬ", FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel, "CloseBtn");

            CuiHelper.AddUi(player, cui);
        }

        void ShowTeamSelection(BasePlayer player, int roomId)
        {
            var cui = new CuiElementContainer();
            string panel = cui.Add(new CuiPanel
            {
                Image = { Color = BG_COLOR },
                RectTransform = { AnchorMin = "0.25 0.2", AnchorMax = "0.75 0.8" },
                CursorEnabled = true
            }, "Overlay", "TeamSelectPanel");

            cui.Add(new CuiLabel
            {
                Text = { 
                    Text = $"⏳ КОМНАТА {roomId} - СТАРТ ЧЕРЕЗ: {RoomCountdowns[roomId]} СЕК", 
                    FontSize = 18, 
                    Align = TextAnchor.MiddleCenter,
                    Color = TITLE_COLOR
                },
                RectTransform = { AnchorMin = "0.1 0.88", AnchorMax = "0.9 0.95" }
            }, panel, "Title");

            CreateTeamButton(cui, panel, roomId, "Team1", "🔵 КОМАНДА 1", BUTTON_BLUE, "0.1 0.6", "0.9 0.75");
            CreateTeamButton(cui, panel, roomId, "Team2", "🔴 КОМАНДА 2", BUTTON_RED, "0.1 0.35", "0.9 0.5");

            cui.Add(new CuiButton
            {
                Button = { Command = "teambattlepro.leaveroom", Color = BUTTON_RED, Close = panel },
                RectTransform = { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.2" },
                Text = { Text = "🚪 ПОКИНУТЬ КОМНАТУ", FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel, "LeaveBtn");

            CuiHelper.AddUi(player, cui);
        }

        void CreateTeamButton(CuiElementContainer cui, string panel, int roomId, string team, string text, string color, string min, string max)
        {
            bool isFull = Rooms[roomId][team].Count >= MAX_TEAM_PLAYERS;
            bool isLocked = RoomLocked[roomId];
            
            cui.Add(new CuiButton
            {
                Button = { 
                    Command = (isFull || isLocked) ? "" : $"teambattlepro.selectteam {roomId} {team}", 
                    Color = isLocked ? BUTTON_DISABLED : (isFull ? BUTTON_DISABLED : color),
                    Close = panel
                },
                RectTransform = { AnchorMin = min, AnchorMax = max },
                Text = { 
                    Text = $"{text}\n({Rooms[roomId][team].Count}/{MAX_TEAM_PLAYERS})", 
                    FontSize = 14, 
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                }
            }, panel, $"{team}Btn");
        }

        void DestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "RoomSelectPanel");
            CuiHelper.DestroyUi(player, "TeamSelectPanel");
        }

        #endregion

        #region Логика игры

        void RemovePlayerFromAllRooms(BasePlayer player)
        {
            if (PlayerData.TryGetValue(player, out var data))
            {
                Rooms[data.Item1][data.Item2].Remove(player);
                PlayerData.Remove(player);
            }
        }

        void UpdateRoomUI(int roomId)
        {
            foreach (var team in Rooms[roomId])
            {
                foreach (var player in team.Value)
                {
                    DestroyUI(player);
                    ShowTeamSelection(player, roomId);
                }
            }
        }

        void ManageRoomTimer(int roomId)
        {
            int playersCount = Rooms[roomId]["Team1"].Count + Rooms[roomId]["Team2"].Count;
            
            if (playersCount == 0)
            {
                ResetTimer(roomId);
                return;
            }

            RoomCountdowns[roomId] = playersCount >= MAX_TEAM_PLAYERS*2 ? FAST_COUNTDOWN : DEFAULT_COUNTDOWN;

            if (!RoomTimers.ContainsKey(roomId))
            {
                StartRoomCountdown(roomId);
            }
        }

        void StartRoomCountdown(int roomId)
        {
            RoomTimers[roomId] = timer.Every(1f, () => 
            {
                if (RoomLocked[roomId])
                {
                    ResetTimer(roomId);
                    return;
                }

                RoomCountdowns[roomId]--;
                UpdateRoomUI(roomId);

                if (RoomCountdowns[roomId] <= 0)
                {
                    StartGame(roomId);
                    return;
                }

                if (Rooms[roomId]["Team1"].Count + Rooms[roomId]["Team2"].Count == 0)
                {
                    ResetTimer(roomId);
                }
            });
        }

        void ResetTimer(int roomId)
        {
            if (RoomTimers.TryGetValue(roomId, out Timer t))
            {
                t.Destroy();
                RoomTimers.Remove(roomId);
            }
            RoomCountdowns[roomId] = DEFAULT_COUNTDOWN;
        }

        void StartGame(int roomId)
        {
            RoomLocked[roomId] = true;
            Puts($"Игра началась в комнате {roomId}");

            TeleportTeams(roomId);

            /* При необходимости выдачи снаряжения раскомментируйте
            foreach (var team in Rooms[roomId])
            {
                foreach (var player in team.Value)
                {
                    player.inventory.Strip();
                    //GiveItem(player, "rifle.ak", 1, true);
                    //GiveItem(player, "ammo.rifle", 150, false);
                }
            }
            */

            foreach (var team in Rooms[roomId])
            {
                foreach (var player in team.Value)
                {
                    player.ChatMessage($"<color=#00FF00>⚔ БИТВА НАЧАЛАСЬ! ВАША КОМАНДА: {team.Key}</color>");
                    DestroyUI(player);
                }
            }

            timer.Once(300f, () => ResetRoom(roomId));
        }

        void TeleportTeams(int roomId)
        {
            if (!RoomSpawns.TryGetValue(roomId, out var spawns))
            {
                Puts($"Ошибка: спавны для комнаты {roomId} не найдены!");
                return;
            }

            for (int i = 0; i < Rooms[roomId]["Team1"].Count; i++)
            {
                var player = Rooms[roomId]["Team1"][i];
                Vector3 spawnPos = spawns.Item1 + new Vector3(i * 2f, 0, 0);
                SafeTeleport(player, spawnPos);
            }

            for (int i = 0; i < Rooms[roomId]["Team2"].Count; i++)
            {
                var player = Rooms[roomId]["Team2"][i];
                Vector3 spawnPos = spawns.Item2 + new Vector3(i * 2f, 0, 0);
                SafeTeleport(player, spawnPos);
            }
        }

        void SafeTeleport(BasePlayer player, Vector3 position)
        {
            try
            {
                if (position.y < 1) position.y = TerrainMeta.HeightMap.GetHeight(position);
                player.Teleport(position);
                player.ClientRPCPlayer(null, player, "StartLoading");
                player.SendNetworkUpdateImmediate();
                // Убрали player.UpdatePlayerCollider(true) - метода нет в API
            }
            catch (Exception ex)
            {
                Puts($"Ошибка телепортации: {ex}");
            }
        }

        void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (!PlayerData.TryGetValue(player, out var data)) return;
            if (!RoomLocked[data.Item1]) return;

            timer.Once(RESPAWN_DELAY, () =>
            {
                if (player.IsDead() || !player.IsConnected) return;

                Vector3 spawnPos = data.Item2 == "Team1"
                    ? RoomSpawns[data.Item1].Item1
                    : RoomSpawns[data.Item1].Item2;

                SafeTeleport(player, spawnPos);
                player.Heal(100f);

                /* При необходимости выдачи снаряжения после смерти раскомментируйте
                player.inventory.Strip();
                GiveItem(player, "rifle.ak", 1, true);
                GiveItem(player, "ammo.rifle", 100, false);
                */
            });
        }

        void GiveItem(BasePlayer player, string shortname, int amount, bool toBelt)
        {
            var item = ItemManager.CreateByName(shortname, amount);
            if (item == null) return;

            if (toBelt)
                item.MoveToContainer(player.inventory.containerBelt);
            else
                item.MoveToContainer(player.inventory.containerMain);

            player.SendNetworkUpdateImmediate();
        }

        void ResetRoom(int roomId)
        {
            foreach (var team in Rooms[roomId])
            {
                foreach (var player in team.Value)
                {
                    PlayerData.Remove(player);
                }
                team.Value.Clear();
            }
            RoomLocked[roomId] = false;
            RoomCountdowns[roomId] = DEFAULT_COUNTDOWN;
            Puts($"Комната {roomId} сброшена");
        }

        #endregion
    }
}
