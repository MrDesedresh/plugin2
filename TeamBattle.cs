using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using System.Collections.Generic;
using System;
using UnityEngine;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
    [Info("TeamBattle", "YourName", "1.2.3")]
    [Description("Система комнат для командных сражений с таймером и снаряжением")]
    public class TeamBattle : RustPlugin
    {
        #region Конфигурация и данные
        
        private const int MAX_ROOMS = 4;
        private const int MAX_TEAM_PLAYERS = 5;
        private const int DEFAULT_COUNTDOWN = 60;
        private const int FAST_COUNTDOWN = 10;

        private Dictionary<int, Dictionary<string, List<BasePlayer>>> Rooms = new Dictionary<int, Dictionary<string, List<BasePlayer>>>();
        private Dictionary<int, Timer> RoomTimers = new Dictionary<int, Timer>();
        private Dictionary<int, int> RoomCountdowns = new Dictionary<int, int>();
        private Dictionary<int, bool> RoomLocked = new Dictionary<int, bool>();
        private Dictionary<int, Vector3> ArenaSpawns = new Dictionary<int, Vector3>();

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
                ArenaSpawns[i] = new Vector3(314 * i, 10, -106);
            }
        }

        #endregion

        #region Команды и UI

        [ChatCommand("play")]
        void PlayCommand(BasePlayer player, string command, string[] args)
        {
            ShowRoomSelection(player);
        }

        void ShowRoomSelection(BasePlayer player)
        {
            var elements = new CuiElementContainer();
            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.9" },
                RectTransform = { AnchorMin = "0.3 0.2", AnchorMax = "0.7 0.8" },
                CursorEnabled = true
            }, "Overlay", "RoomSelectPanel");

            elements.Add(new CuiLabel
            {
                Text = { Text = "Выберите комнату", FontSize = 18, Align = TextAnchor.MiddleCenter },
                RectTransform = { AnchorMin = "0.1 0.85", AnchorMax = "0.9 0.95" }
            }, panel, "Title");

            for (int i = 1; i <= MAX_ROOMS; i++)
            {
                int roomId = i;
                int playersCount = Rooms[roomId]["Team1"].Count + Rooms[roomId]["Team2"].Count;
                string status = RoomLocked[roomId] ? "[ЗАКРЫТО]" : $"[{playersCount}/{MAX_TEAM_PLAYERS*2}]";

                elements.Add(new CuiButton
                {
                    Button = { 
                        Command = $"teambattle.selectroom {roomId}", 
                        Color = RoomLocked[roomId] ? "0.3 0.3 0.3 0.5" : "0.3 0.3 0.3 1",
                        Close = panel
                    },
                    RectTransform = { AnchorMin = $"0.1 {0.75 - (i * 0.15)}", AnchorMax = $"0.9 {0.85 - (i * 0.15)}" },
                    Text = { Text = $"Комната {roomId} {status}", FontSize = 14, Align = TextAnchor.MiddleCenter }
                }, panel, $"RoomBtn_{i}");
            }

            CuiHelper.AddUi(player, elements);
        }

        [ConsoleCommand("teambattle.selectroom")]
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

            CuiHelper.DestroyUi(player, "RoomSelectPanel");
            ShowTeamSelection(player, roomId);
        }

        void ShowTeamSelection(BasePlayer player, int roomId)
        {
            var elements = new CuiElementContainer();
            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.9" },
                RectTransform = { AnchorMin = "0.3 0.2", AnchorMax = "0.7 0.8" },
                CursorEnabled = true
            }, "Overlay", "TeamSelectPanel");

            elements.Add(new CuiLabel
            {
                Text = { Text = $"Комната {roomId} - Старт через: {RoomCountdowns[roomId]} сек", FontSize = 18, Align = TextAnchor.MiddleCenter },
                RectTransform = { AnchorMin = "0.1 0.85", AnchorMax = "0.9 0.95" }
            }, panel, "Title");

            CreateTeamButton(elements, panel, roomId, "Team1", "0.2 0.4 0.8 1", "0.1 0.6", "0.9 0.75");
            CreateTeamButton(elements, panel, roomId, "Team2", "0.8 0.2 0.2 1", "0.1 0.35", "0.9 0.5");

            elements.Add(new CuiButton
            {
                Button = { Command = "teambattle.leaveroom", Color = "0.8 0.2 0.2 1", Close = panel },
                RectTransform = { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.25" },
                Text = { Text = "Покинуть комнату", FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel, "LeaveBtn");

            CuiHelper.AddUi(player, elements);
        }

        void CreateTeamButton(CuiElementContainer elements, string panel, int roomId, string team, string color, string min, string max)
        {
            bool isFull = Rooms[roomId][team].Count >= MAX_TEAM_PLAYERS;
            
            elements.Add(new CuiButton
            {
                Button = { 
                    Command = isFull ? "" : $"teambattle.selectteam {roomId} {team}", 
                    Color = isFull ? "0.3 0.3 0.3 0.5" : color,
                    Close = panel
                },
                RectTransform = { AnchorMin = min, AnchorMax = max },
                Text = { Text = $"{team} ({Rooms[roomId][team].Count}/{MAX_TEAM_PLAYERS})", FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel, $"{team}Btn");
        }

        #endregion

        #region Логика игры

        [ConsoleCommand("teambattle.selectteam")]
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
            player.ChatMessage($"Вы в команде {team} (Комната {roomId})");
            
            UpdateRoomUI(roomId);
            ManageRoomTimer(roomId);
        }

        [ConsoleCommand("teambattle.leaveroom")]
        void LeaveRoomCMD(ConsoleSystem.Arg arg)
        {
            var player = arg.Player() as BasePlayer;
            if (player == null) return;

            int roomId = FindPlayerRoom(player);
            if (roomId == -1) return;

            if (RoomLocked[roomId])
            {
                player.ChatMessage("Нельзя выйти во время игры!");
                return;
            }

            RemovePlayerFromAllRooms(player);
            CuiHelper.DestroyUi(player, "TeamSelectPanel");
            player.ChatMessage("Вы покинули комнату");
            ManageRoomTimer(roomId);
        }

        void RemovePlayerFromAllRooms(BasePlayer player)
        {
            foreach (var room in Rooms)
            {
                foreach (var team in room.Value)
                {
                    team.Value.Remove(player);
                }
            }
        }

        int FindPlayerRoom(BasePlayer player)
        {
            foreach (var room in Rooms)
            {
                foreach (var team in room.Value)
                {
                    if (team.Value.Contains(player))
                        return room.Key;
                }
            }
            return -1;
        }

        void UpdateRoomUI(int roomId)
        {
            foreach (var team in Rooms[roomId])
            {
                foreach (var player in team.Value)
                {
                    CuiHelper.DestroyUi(player, "TeamSelectPanel");
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
            if (RoomTimers.TryGetValue(roomId, out Timer timer))
            {
                timer.Destroy();
                RoomTimers.Remove(roomId);
            }
            RoomCountdowns[roomId] = DEFAULT_COUNTDOWN;
        }

        void StartGame(int roomId)
        {
            RoomLocked[roomId] = true;
            Puts($"Игра началась в комнате {roomId}");

            TeleportTeams(roomId);
            GiveStarterKits(roomId);

            foreach (var team in Rooms[roomId])
            {
                foreach (var player in team.Value)
                {
                    player.ChatMessage($"<color=green>Игра началась! Вы в {team.Key}</color>");
                    CuiHelper.DestroyUi(player, "TeamSelectPanel");
                }
            }

            timer.Once(300f, () => ResetRoom(roomId));
        }

        void TeleportTeams(int roomId)
        {
            float offset = 5f;
            Vector3 basePos = ArenaSpawns[roomId];

            for (int i = 0; i < Rooms[roomId]["Team1"].Count; i++)
            {
                Rooms[roomId]["Team1"][i].Teleport(basePos + new Vector3(i * offset, 0, 0));
            }

            for (int i = 0; i < Rooms[roomId]["Team2"].Count; i++)
            {
                Rooms[roomId]["Team2"][i].Teleport(basePos + new Vector3(-i * offset, 0, 0));
            }
        }

        void GiveStarterKits(int roomId)
        {
            foreach (var team in Rooms[roomId])
            {
                foreach (var player in team.Value)
                {
                    player.inventory.Strip();
                    GiveWeapon(player, "rifle.ak");
                    GiveItem(player, "ammo.rifle", 100);
                    GiveItem(player, "medkit", 3);
                    GiveItem(player, "bandage", 5);
                }
            }
        }

        void GiveWeapon(BasePlayer player, string itemName)
        {
            ItemManager.CreateByName(itemName, 1)?.MoveToContainer(player.inventory.containerBelt);
        }

        void GiveItem(BasePlayer player, string itemName, int amount)
        {
            ItemManager.CreateByName(itemName, amount)?.MoveToContainer(player.inventory.containerMain);
        }

        void ResetRoom(int roomId)
        {
            Rooms[roomId]["Team1"].Clear();
            Rooms[roomId]["Team2"].Clear();
            RoomLocked[roomId] = false;
            RoomCountdowns[roomId] = DEFAULT_COUNTDOWN;
            Puts($"Комната {roomId} сброшена");
        }

        #endregion
    }
}