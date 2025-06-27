using ExileCore.PoEMemory.Components;
using ExileCore;
using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Numerics;
using Newtonsoft.Json;
using ExileCore.Shared.Helpers;

namespace FollowerServer;
public class PartyClient(MainPlugin plugin)
{
    public string ClientName => _plugin.GameController.Player?.GetComponent<Player>()?.PlayerName;
    private TcpClient _client;
    private NetworkStream _stream;
    private readonly MainPlugin _plugin = plugin;
    private string ServerIp => _plugin.Settings.Party.ServerIP; // Adresse IP du serveur (leader)
    public bool IsConnected => _client != null && _client.Connected;
    public void Connect()
    {
        if (!int.TryParse(_plugin.Settings.Server.Port, out int port))
        {
            _plugin.Log("Le port spécifié est invalide. Connexion annulée.", LogLevel.Error);
            TerminateConnection();
            return;
        }

        if (!IsServerAvailable(ServerIp, port, 500))
        {
            _plugin.Log("Serveur injoignable, connexion annulée.", LogLevel.Error);
            TerminateConnection();

            return;
        }
        try
        {
            _client = new TcpClient(ServerIp, port);
            _stream = _client.GetStream();

            Thread receiveThread = new(ReceiveMessages)
            {
                IsBackground = true
            };
            receiveThread.Start();

            SendMessage(MessageType.Connect, ClientName);
            MainPlugin.Status = eStatus.Running;
        }
        catch (Exception ex)
        {
            MainPlugin.Status = eStatus.Stopped;
            _plugin.Log($"Erreur lors de la connexion au serveur : {ex.Message}", LogLevel.Error);
        }
    }

    private void TerminateConnection()
    {
        _client = null;
        _stream = null;
        MainPlugin.Status = eStatus.Stopped;
        _plugin.IsTaskRunning = false;
    }

    private static bool IsServerAvailable(string ip, int port, int timeoutMs = 1000)
    {
        try
        {
            using (var client = new TcpClient())
            {
                var result = client.BeginConnect(ip, port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(timeoutMs);
                return success && client.Connected;
            }
        }
        catch
        {
            return false;
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
                _plugin.Log($"Message envoyé : {message.Serialize()}", LogLevel.Error);
            }
            catch (Exception ex)
            {
                _plugin.Log($"Erreur lors de l'envoi du message : {ex.Message}", LogLevel.Error);
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
                                if (!string.IsNullOrEmpty(messageObj.Content))
                                {
                                    _plugin.Log($"Message reçu : {messageObj.Content}", LogLevel.Error);
                                   
                                    if(messageObj.Content == "||")
                                    {
                                        _plugin.Log("Pause Follow");
                                        MainPlugin.Status = eStatus.Paused;
                                       
                                    }
                                    else if (messageObj.Content == "|>")
                                    {
                                        _plugin.Log("Resume Follow");
                                        MainPlugin.Status = eStatus.Running;
                                    }
                                    
                                }
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
                    _plugin.Log($"Erreur de JSON : {jsonEx.Message}. Message reçu : {message}", LogLevel.Error);
                }
            }
        }
        catch (Exception ex)
        {
            _plugin.Log($"Erreur lors de la réception des messages : {ex.Message}", LogLevel.Error);
        }
        finally
        {
            if (_client != null && _client.Connected)
            {
                _stream?.Close();
                _client.Close();
                _client = null;
            }

            _plugin.Log("Déconnecté du serveur.", LogLevel.Error);
            _stream = null;
            _client = null;
            _plugin.IsTaskRunning = false;
        }
    }
    private void ProcessLeaderInput(LeaderInput input)
    {
        try
        {
            if (_plugin.GameController.Area.CurrentArea.IsHideout ||
                (_plugin.Settings.Party.FollowInTown == false && _plugin.GameController.Area.CurrentArea.IsTown) ||
                MenuWindow.IsOpened || MainPlugin.Status != eStatus.Running)
            {
                return;
            }

            if (!int.TryParse(input.RawInput, out var inputIndex) || inputIndex <= 0)
            {
                _plugin.Log($"Input non reconnu ou invalide : {input.RawInput}", LogLevel.Error);
                return;
            }

            var leaderEntity = _plugin.GameController.Entities?
                .FirstOrDefault(x => x.Type == ExileCore.Shared.Enums.EntityType.Player &&
                                     x.GetComponent<Player>()?.PlayerName == input.LeaderName);
            if (leaderEntity == null) return;
            var actorskill = _plugin.GameController.Player.GetComponent<Actor>().ActorSkills.FirstOrDefault(x => x.SkillSlotIndex == inputIndex && x.Id != 10505);

            if (actorskill != null && _plugin.Settings.Party.UseSmartTPSkill && actorskill.GetStat(ExileCore.Shared.Enums.GameStat.SkillIsTravelSkill) > 0)
            {
                return;
            }

            var clientWindow = _plugin.GameController.Window.GetWindowRectangleTimeCache;
            var mouse = input.MouseCoords;

            float clickX = (mouse.X * clientWindow.Width) - _plugin.Settings.Party.screenOffsetAdjustementX;
            float clickY = (mouse.Y * clientWindow.Height) - _plugin.Settings.Party.screenOffsetAdjustementY;
            var clickPos = new Vector2(clickX, clickY);

            var leaderPos = leaderEntity.GridPosNum;
            var leaderScreenPos = _plugin.GameController.IngameState.Data.GetGridScreenPosition(leaderPos);
            var distance = leaderScreenPos.Distance(clickPos);

            if (distance > 100)
            {
                var actor = leaderEntity.GetComponent<Actor>();
                var pathfinding = leaderEntity.GetComponent<Pathfinding>();

                Vector2? worldTarget = actor?.CurrentAction?.Destination.ToVector2Num() ??
                                       pathfinding?.WantMoveToPosition.ToVector2Num();

                if (worldTarget != null && worldTarget.HasValue && worldTarget != Vector2.Zero)
                {
                    clickPos = _plugin.GameController.IngameState.Data.GetGridScreenPosition(worldTarget.Value);
                    _plugin.Log($"Position ajustée : {clickPos} depuis destination.");
                }
                else
                {
                    clickPos = leaderScreenPos;
                    _plugin.Log($"Ajustement par défaut à la position du leader : {clickPos}.", LogLevel.Error);
                }
            }

            if (clientWindow.Contains(clickPos.ToSharpDx()))
            {
                Input.SetCursorPos(clickPos);
                Thread.Sleep(10);
            }

            var scs = _plugin.GetSkillBarShortCuts();
            if (inputIndex < scs.Count)
            {
                scs[inputIndex].PressShortCut(10);
            }
        }
        catch (Exception ex)
        {
            _plugin.Log($"Erreur lors du traitement de l'input : {ex}", LogLevel.Error);
        }
    }
    public void Disconnect()
    {
        if (_client != null)
        {
            _stream?.Close();
            _client.Close();
            TerminateConnection();
        }
    }
}