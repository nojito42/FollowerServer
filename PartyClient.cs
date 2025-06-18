using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;
using Newtonsoft.Json;
using FollowerPlugin;
using System.Windows.Forms;
using ExileCore.Shared.Helpers;
using GameOffsets.Native;

namespace FollowerServer;


public class PartyClient(FollowerPlugin plugin)
{
    private TcpClient _client;
    private NetworkStream _stream;
    private readonly FollowerPlugin _plugin = plugin;
    private string ServerIp => _plugin.Settings.ServerSubMenu.ServerIP; // Adresse IP du serveur (leader)
    private readonly int _serverPort = 5051; // Port du serveur
    public bool IsConnected => _client != null && _client.Connected;

    public void Connect()
    {
        try
        {
            _client = new TcpClient(ServerIp, _serverPort);
            _stream = _client.GetStream();

            Thread receiveThread = new Thread(ReceiveMessages);
            receiveThread.IsBackground = true;
            receiveThread.Start();
            SendMessage(MessageType.Order, "Hello from client!");
        }
        catch (Exception ex)
        {
            _plugin.LogError($"Erreur lors de la connexion au serveur : {ex.Message}");
        }
    }
    public void SendMessage(MessageType messageType, string content)
    {
        if (_stream != null)
        {
            try
            {
                var message = new Message
                {
                    MessageType = messageType,
                    Content = content
                };

                byte[] messageBytes = Encoding.UTF8.GetBytes(message.Serialize());
                _stream.Write(messageBytes, 0, messageBytes.Length);
                _plugin.LogError($"Message envoyé : {message.Serialize()}");
            }
            catch (Exception ex)
            {
                _plugin.LogError($"Erreur lors de l'envoi du message : {ex.Message}");
            }
        }
    }
    private void ReceiveMessages()
    {
        try
        {
            byte[] buffer = new byte[1024];
            int bytesRead;

            while ((bytesRead = _stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                try
                {
                    var messageObj = Message.DeserializeMessage(message);
                    if (messageObj != null)
                    {
                        switch (messageObj.MessageType)
                        {
                            case MessageType.Order:
                                break;

                            case MessageType.Input:
                                if (messageObj.Input != null)
                                    ProcessLeaderInput(messageObj.Input);
                                break;

                            default:
                                break;
                        }
                    }

                }
                catch (JsonException jsonEx)
                {
                    _plugin.LogError($"Erreur de JSON : {jsonEx.Message}. Message reçu : {message}");
                }
            }
        }
        catch (Exception ex)
        {
            _plugin.LogError($"Erreur lors de la réception des messages : {ex.Message}");
        }
        finally
        {
            _client.Close();
            _plugin.LogError("Déconnecté du serveur.");
        }
    }
    private List<PlayerSkill> SetLeaderSkillAndShortCuts()
    {

        var shortcuts = _plugin.shortcuts;
        var sc = shortcuts.Skip(7).Take(13).ToList();
        var foeskills = new List<PlayerSkill>();

        for (int i = 0; i < sc.Count; i++)
        {
            var skillBarSkill = _plugin.GameController.IngameState.IngameUi.SkillBar.Skills[i];
            foeskills.Add(new PlayerSkill
            {
                Shortcut = sc[i],
                Skill = skillBarSkill
            });
        }
        return foeskills;
    }

    private void ProcessLeaderInput(LeaderInput input)
    {
        try
        {
            if (_plugin.GameController.Area.CurrentArea.IsHideout)
            {
                return;
            }
            Entity leaderEntity = _plugin.GameController.Entities?
                .FirstOrDefault(x => x.Type == ExileCore.Shared.Enums.EntityType.Player &&
                                     x.GetComponent<Player>()?.PlayerName == input.LeaderName);
            var followerSkills = SetLeaderSkillAndShortCuts();
            var scsAll = _plugin.shortcuts;
            var scs = scsAll.Skip(7).Take(13).ToList();
            var i = -1;

            if (int.TryParse(input.RawInput, out var i1))
            {
                if (i1 <= 0)
                {
                    _plugin.LogError($"Input non reconnu : {input.RawInput}");
                    return;
                }
            }

            if (leaderEntity == null)
            {
                return;
            }
            if (_plugin.Settings.PartySubMenu.FollowInTown == false && _plugin.GameController.Area.CurrentArea.IsTown || MenuWindow.IsOpened)
            {
                return;
            }
            var clientWindow = _plugin.GameController.Window.GetWindowRectangleTimeCache;
            //Vector2 leaderScreenPos = _plugin.GameController.IngameState.Data.GetWorldScreenPosition(leaderEntity.PosNum);
            //Vector2 followerScreenPos = _plugin.GameController.IngameState.Data.GetWorldScreenPosition(_plugin.GameController.Player.PosNum);

            //float offsetX = followerScreenPos.X - leaderScreenPos.X;
            //float offsetY = followerScreenPos.Y - leaderScreenPos.Y;

            float clickX = (input.MouseCoords.X * clientWindow.Width) - (/*offsetX + */_plugin.Settings.PartySubMenu.screenOffsetAdjustementX);
            float clickY = (input.MouseCoords.Y * clientWindow.Height) - (/*offsetY +*/ _plugin.Settings.PartySubMenu.screenOffsetAdjustementY);

            var clickPos = new Vector2(clickX, clickY);
            if (_plugin.GameController.Window.GetWindowRectangleTimeCache.Contains(clickPos.ToSharpDx()))
            {

                Input.SetCursorPos(clickPos);
                Thread.Sleep(10);
            }
            //if (input.RawInput.Contains("Roll") && _plugin.Settings.PartySubMenu.DodgeRollWithLeader)
            //{
            //    Thread.Sleep(5);
            //    _plugin.DodgeRoll.PressShortCut(10);
            //    return;
            //}
            //else if (input.RawInput.Contains("AttackInPlace"))
            //{
            //    var sc = _plugin.GetShortcutByNameContains("(25/");

            //    if (input.Pressed)
            //    {
            //        Input.KeyDown((Keys)sc.MainKey);
            //        _plugin.LogError($"KeyDown : {sc.MainKey}");
            //    }
            //    else
            //    {
            //        Input.KeyUp((Keys)sc.MainKey);
            //        _plugin.LogError($"KeyUp : {sc.MainKey}");
            //    }
            //}

            if (int.TryParse(input.RawInput, out var i2))
            {
                i = i2;
                var sc = scs[i];
                if (i > 0)
                {
                    sc.PressShortCut(10);
                }
            }

        }
        catch (Exception ex)
        {
            _plugin.LogError($"Erreur lors du traitement de l'input : {ex}");
        }
    }

    //private void ProcessLeaderInput(LeaderInput input)
    //{
    //    try
    //    {
    //        if (_plugin.GameController.Area.CurrentArea.IsHideout)
    //            return;

    //        Entity leaderEntity = _plugin.GameController.Entities?
    //            .FirstOrDefault(x => x.Type == ExileCore.Shared.Enums.EntityType.Player &&
    //                                 x.GetComponent<Player>()?.PlayerName == input.LeaderName);

    //        var followerSkills = SetLeaderSkillAndShortCuts();
    //        var scsAll = _plugin.shortcuts;
    //        var scs = scsAll.Skip(7).Take(13).ToList();
    //        var i = -1;

    //        if (int.TryParse(input.RawInput, out var i1))
    //        {
    //            if (i1 <= 0)
    //            {
    //                _plugin.LogError($"Input non reconnu : {input.RawInput}");
    //                return;
    //            }
    //        }

    //        if (leaderEntity == null)
    //            return;

    //        if (_plugin.Settings.PartySubMenu.FollowInTown == false && _plugin.GameController.Area.CurrentArea.IsTown || MenuWindow.IsOpened)
    //            return;

    //        var actionDestination = leaderEntity.GetComponent<Actor>()?.CurrentAction?.Destination;


    //        Vector2 leaderScreenPos = _plugin.GameController.IngameState.Data.GetWorldScreenPosition(leaderEntity.PosNum);
    //        Vector2 followerScreenPos = _plugin.GameController.IngameState.Data.GetWorldScreenPosition(_plugin.GameController.Player.PosNum);

    //        if (actionDestination != null && actionDestination.HasValue)
    //        {
    //            var worldTarget = actionDestination.Value.ToVector2Num();
    //            var screenWorldTarget = _plugin.GameController.IngameState.Data.GetGridScreenPosition(worldTarget);
    //            //var screenDistance = followerScreenPos.Distance(leaderScreenPos);

    //            //if (screenDistance < 1000) // éviter les positions trop éloignées ou invalides
    //            //{
    //            if (_plugin.GameController.Window.GetWindowRectangle().Contains(screenWorldTarget))
    //                input.MouseCoords = screenWorldTarget;
    //            else
    //                _plugin.LogError($"Action destination hors de l'écran : {screenWorldTarget} pour {leaderEntity.GetComponent<Player>()?.PlayerName} à {leaderEntity.GridPosNum}.");
    //            //}
    //        }

    //        //if (input.MouseCoords == Vector2.Zero)
    //        //{
    //        //    _plugin.LogError("MouseCoords vide, annulation du clic");
    //        //    return;
    //        //}

    //        Vector2 clickPos;
    //        var clientWindow = _plugin.GameController.Window.GetWindowRectangleTimeCache;

    //        if (actionDestination == null || actionDestination == Vector2i.Zero)
    //        {
    //            float offsetX = followerScreenPos.X - leaderScreenPos.X;
    //            float offsetY = followerScreenPos.Y - leaderScreenPos.Y;

    //            float clickX = (input.MouseCoords.X * clientWindow.Width) - (offsetX + _plugin.Settings.PartySubMenu.screenOffsetAdjustementX);
    //            float clickY = (input.MouseCoords.Y * clientWindow.Height) - (offsetY + _plugin.Settings.PartySubMenu.screenOffsetAdjustementY);

    //            clickPos = new Vector2(clickX, clickY);
    //        }
    //        else
    //        {
    //            clickPos = input.MouseCoords;
    //        }
    //        _plugin.LogMessage($"Clic position: {clickPos}, Leader Position: {leaderEntity.GridPosNum}, Distance: {clickPos.Distance(actionDestination.Value)}");
    //        if (_plugin.GameController.Window.GetWindowRectangleTimeCache.Contains(clickPos) || clickPos.Distance(leaderEntity.GridPosNum) <=10)
    //        {
    //            Input.SetCursorPos(clickPos);
    //            Thread.Sleep(10);
    //        }
    //        else
    //        {
    //           clickPos =_plugin.GameController.IngameState.Data.GetGridScreenPosition(leaderEntity.GridPosNum);
    //            Input.SetCursorPos(clickPos);
    //            Thread.Sleep(10);

    //        }

    //        if (int.TryParse(input.RawInput, out var i2))
    //        {
    //            i = i2;
    //            if (i >= 0 && i < scs.Count)
    //            {
    //                var sc = scs[i];
    //                sc.PressShortCut(10);
    //            }
    //            else
    //            {
    //                _plugin.LogError($"Index de raccourci invalide : {i}");
    //            }
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        _plugin.LogError($"Erreur lors du traitement de l'input : {ex}");
    //    }
    //}
    public void Disconnect()
    {
        if (_client != null)
        {
            _client.Close();
            _client = null;
        }
    }
}