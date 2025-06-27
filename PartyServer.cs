using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;

namespace FollowerServer;

public class PartyServer(MainPlugin plugin)
{
    private TcpListener _server;
    private bool _isRunning;
    private readonly MainPlugin _plugin = plugin;
    public bool IsRunning => _isRunning;

    public static string ServerIP => GetLocalIPAddress();
    public string username = "user";

    public bool IsLeaderAndServerHost => _plugin.Settings.Server.ToggleLeaderServer.Value;

    public Dictionary<string, TcpClient> ConnectedClients { get; set; } = [];

    public void Start()
    {
        if (_isRunning || !IsLeaderAndServerHost)
        {
            _plugin.Log("Server Already Running, or you are not the leader and server host.", LogLevel.Error);
            return;
        }

        if (!int.TryParse(_plugin.Settings.Server.Port, out int port))
        {
            _plugin.Log("Le port spécifié est invalide. Initialisation du serveur annulée.", LogLevel.Error);
            return;
        }

        _plugin.Log($"Serveur initialisé sur {ServerIP}:{port}.", LogLevel.Error);

        if (_server == null)
            _server = new TcpListener(IPAddress.Parse(ServerIP), port);

        Thread serverThread = new(() =>
        {
            try
            {
                _server.Start();
                _plugin.Log($"Serveur démarré sur {ServerIP}:{port}. En attente de connexions...", LogLevel.Error);
                _isRunning = true;
                var toRemove = ConnectedClients
                    .Where(kvp => !kvp.Value.Connected)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in toRemove)
                {
                    ConnectedClients.Remove(key);
                }

                var _timer = new Timer(_ =>
                {
                    lock (ConnectedClients)
                    {
                        _plugin.Log($"[Monitor] Clients actifs : {ConnectedClients.Count}", LogLevel.Info, 2000);
                    }
                }, null, 0, 2000);

                while (_isRunning)
                {
                    TcpClient client = _server.AcceptTcpClient();
                    _plugin.Log("Nouveau client connecté.", LogLevel.Error);

                    Thread clientThread = new(() => HandleClient(client))
                    {
                        IsBackground = true
                    };
                    clientThread.Start();
                }
            }
            catch (Exception ex)
            {
                _plugin.Log($"Erreur : {ex.Message}", LogLevel.Error);
                _isRunning = false;
            }
        })
        {
            IsBackground = true
        };

        serverThread.Start();
    }
    public void Stop()
    {
        if (!_isRunning) return;

        try
        {
            _isRunning = false;
            _server.Stop();
            _plugin.Log("Serveur arrêté.", LogLevel.Error);
        }
        catch (Exception ex)
        {
            _plugin.Log($"Erreur lors de l'arrêt du serveur : {ex.Message}", LogLevel.Error);
        }
    }
    private static string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return ip.ToString();
        }
        throw new Exception("Local IP Address Not Found!");
    }
    public void BroadcastLeaderInput(LeaderInput leaderInput)
    {
        var message = new Message
        {
            MessageType = MessageType.Input,
            Content = $"Sending {leaderInput}",
            Input = leaderInput
        };

        foreach (var kvp in ConnectedClients)
        {
            var client = kvp.Value;
            if (client.Connected)
                SendMessageToClient(client, message);
        }
    }
    private void SendMessageToClient(TcpClient client, Message message)
    {
        try
        {
            NetworkStream stream = client.GetStream();
            byte[] messageBytes = Encoding.UTF8.GetBytes(message.Serialize());
            stream.Write(messageBytes, 0, messageBytes.Length);
        }
        catch (Exception ex)
        {
            _plugin.Log($"Erreur lors de l'envoi du message au client : {ex.Message}", LogLevel.Error);
        }
    }
    private void HandleClient(TcpClient client)
    {
        string clientName = null;

        try
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead;

            // Lire le premier message (doit être un "Connect" avec nom du client)
            if ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var messageObj = Message.DeserializeMessage(msg);

                if (messageObj != null && messageObj.MessageType == MessageType.Connect)
                {
                    clientName = messageObj.Content;

                    lock (ConnectedClients)
                    {
                        ConnectedClients.TryAdd(clientName, client);
                        _plugin.Log($"Client connecté : {clientName}. Total clients : {ConnectedClients.Count}", LogLevel.Error);
                    }
                }
                else
                {
                    _plugin.Log("Premier message invalide ou non reconnu, fermeture de la connexion.", LogLevel.Error);
                    client.Close();
                    return;
                }
            }

            // Boucle de réception normale
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                _plugin.Log($"[{clientName}] Message reçu : {msg}", LogLevel.Error);

                // Ajoute ici le traitement du message si besoin
            }
        }
        catch (Exception ex)
        {
            _plugin.Log($"Erreur côté client {clientName ?? "inconnu"} : {ex.Message}", LogLevel.Error);
        }
        finally
        {
            if (clientName != null)
            {
                lock (ConnectedClients)
                {
                    if (ConnectedClients.ContainsKey(clientName))
                    {
                        ConnectedClients.Remove(clientName);
                        _plugin.Log($"Client {clientName} déconnecté. Clients restants : {ConnectedClients.Count}", LogLevel.Error);
                    }
                }
            }

            client.Close();
        }
    }
}
