﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using ProtoBuf;

namespace ClientCommands
{
    [ProtoContract]
    [NetworkPacketId(ClientIdentifiers.AddPlayer)]
    public struct AddPlayer : IClientCommand
    {
        [ProtoMember(1)]
        public int ID;
        [ProtoMember(2)]
        public string Name;
        [ProtoMember(3)]
        public int Color;
        [ProtoMember(4)]
        public long Money;
        [ProtoMember(5)]
        public Dictionary<int, DiplomacyFlags> Diplomacy;
        [ProtoMember(6)]
        public bool Silent; // whether to display the "player has connected" or not.
        [ProtoMember(7)]
        public bool ConsolePlayer; // this is true when the server says us that we have control over this one.

        public bool Process()
        {
            if (!MapLogic.Instance.IsLoaded)
                return false;
            if (MapLogic.Instance.GetPlayerByID(ID) != null)
                return false; // player already exists: this should NOT happen

            Player player = new Player((ServerClient)null);
            player.ID = ID;
            player.Name = Name;
            player.Color = Color;
            player.Money = Money;
            foreach (var pair in Diplomacy)
                player.Diplomacy[pair.Key] = pair.Value;
            if (ConsolePlayer)
            {
                GameConsole.Instance.WriteLine("We are controlling player {0}.", player.Name);
                MapLogic.Instance.ConsolePlayer = player;
            }
            MapLogic.Instance.AddNetPlayer(player, Silent);
            return true;
        }
    }

    [ProtoContract]
    [NetworkPacketId(ClientIdentifiers.DelPlayer)]
    public struct DelPlayer : IClientCommand
    {
        [ProtoMember(1)]
        public int ID;
        [ProtoMember(2)]
        public bool Kick; // whether the "player was kicked" message will be displayed (if Silent is false)
        [ProtoMember(3)]
        public bool Silent;

        public bool Process()
        {
            if (!MapLogic.Instance.IsLoaded)
                return false;
            Player player = MapLogic.Instance.GetPlayerByID(ID);
            if (player == MapLogic.Instance.ConsolePlayer)
                MapLogic.Instance.ConsolePlayer = null;
            if (player != null)
                MapLogic.Instance.DelNetPlayer(player, Silent, Kick);
            return true;
        }
    }

    [ProtoContract]
    [NetworkPacketId(ClientIdentifiers.ChatMessage)]
    public struct ChatMessage : IClientCommand
    {
        [ProtoMember(1)]
        public int PlayerID;
        [ProtoMember(2)]
        public string Text;

        public bool Process()
        {
            if (!MapLogic.Instance.IsLoaded)
                return false;
            Player player = (PlayerID > 0) ? MapLogic.Instance.GetPlayerByID(PlayerID) : null;
            int color = (player != null) ? player.Color : Player.AllColorsSystem;
            string text = (player != null) ? player.Name + ": " + Text : "<server>: " + Text;
            MapViewChat.Instance.AddChatMessage(color, text);
            return true;
        }
    }

    [ProtoContract]
    [NetworkPacketId(ClientIdentifiers.SpeedChanged)]
    public struct SpeedChanged : IClientCommand
    {
        [ProtoMember(1)]
        public int NewSpeed;

        public bool Process()
        {
            if (!MapLogic.Instance.IsLoaded)
                return false;
            if (NewSpeed != MapLogic.Instance.Speed)
            {
                MapLogic.Instance.Speed = NewSpeed;
                MapViewChat.Instance.AddChatMessage(Player.AllColorsSystem, Locale.Main[108 + NewSpeed]);
            }
            return true;
        }
    }

    [ProtoContract]
    [NetworkPacketId(ClientIdentifiers.AddUnit)]
    public struct AddUnit : IClientCommand
    {
        [ProtoMember(1)]
        public int Tag;
        [ProtoMember(2)]
        public int X;
        [ProtoMember(3)]
        public int Y;
        [ProtoMember(4)]
        public int Angle;
        [ProtoMember(5)]
        public int Player;
        [ProtoMember(6)]
        public int ServerID; // this also contains templates
        [ProtoMember(7)]
        public UnitStats CurrentStats;
        [ProtoMember(8)]
        public bool IsAvatar;

        public bool Process()
        {
            Debug.LogFormat("added unit {0}", Tag);
            if (!MapLogic.Instance.IsLoaded)
                return false;
            MapUnit unit = MapLogic.Instance.GetUnitByTag(Tag);
            bool newUnit = false;
            if (unit == null)
            {
                unit = new MapUnit(ServerID);
                if (unit.Class == null)
                    return false; // invalid unit created
                unit.Tag = Tag;
                newUnit = true;
            }
            Player player = MapLogic.Instance.GetPlayerByID(Player);
            if (player == null)
            {
                Debug.LogFormat("Unable to resolve player {0} for unit {1}", Player, Tag);
                return false;
            }
            unit.Player = player;
            if (IsAvatar)
                unit.Player.Avatar = unit;
            unit.States.RemoveRange(1, unit.States.Count - 1); // clear states.
            unit.UnlinkFromWorld();
            unit.X = X;
            unit.Y = Y;
            unit.LinkToWorld();
            unit.Angle = Angle;
            unit.Stats = CurrentStats;
            if (newUnit)
                MapLogic.Instance.Objects.Add(unit);
            return true;
        }
    }

    [ProtoContract]
    [NetworkPacketId(ClientIdentifiers.DelUnit)]
    public struct DelUnit : IClientCommand
    {
        [ProtoMember(1)]
        public int Tag;

        public bool Process()
        {
            if (!MapLogic.Instance.IsLoaded)
                return false;

            MapUnit unit = MapLogic.Instance.GetUnitByTag(Tag);
            if (unit == null)
            {
                Debug.LogFormat("Attempted to delete nonexistent unit {0}", Tag);
            }
            else
            {
                unit.Dispose();
                MapLogic.Instance.Objects.Remove(unit);
            }

            return true;
        }
    }

    [ProtoContract]
    [NetworkPacketId(ClientIdentifiers.WalkUnit)]
    public struct WalkUnit : IClientCommand
    {
        [ProtoMember(1)]
        public int Tag;
        [ProtoMember(2)]
        public int StartAngle;
        [ProtoMember(3)]
        public int EndAngle;
        [ProtoMember(4)]
        public int X1;
        [ProtoMember(5)]
        public int Y1;
        [ProtoMember(6)]
        public int X2;
        [ProtoMember(7)]
        public int Y2;

        public bool Process()
        {
            if (!MapLogic.Instance.IsLoaded)
                return false;
            MapUnit unit = MapLogic.Instance.GetUnitByTag(Tag);
            if (unit == null)
            {
                Debug.LogFormat("Attempted to delete nonexistent unit {0}", Tag);
            }
            else
            {
                unit.Angle = StartAngle;
                unit.States.Insert(1, new RotateState(unit, EndAngle));
                unit.States.Insert(1, new MoveState(unit, X2, Y2)); // rotate still first.
            }

            return true;
        }
    }
}
