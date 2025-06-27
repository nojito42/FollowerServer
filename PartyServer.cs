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
            _plugin.LogError("Server Already Running, or you are not the leader and server host.");
            return;
        }

        if (!int.TryParse(_plugin.Settings.Server.Port, out int port))
        {
            _plugin.LogError("Le port spécifié est invalide. Initialisation du serveur annulée.");
            return;
        }

        _plugin.LogError($"Serveur initialisé sur {ServerIP}:{port}.");

        if (_server == null)
            _server = new TcpListener(IPAddress.Parse(ServerIP), port);

        Thread serverThread = new(() =>
        {
            try
            {
                _server.Start();
                _plugin.LogError($"Serveur démarré sur {ServerIP}:{port}. En attente de connexions...");
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
                        _plugin.LogError($"[Monitor] Clients actifs : {ConnectedClients.Count}");
                    }
                }, null, 0, 2000);

                while (_isRunning)
                {
                    TcpClient client = _server.AcceptTcpClient();
                    _plugin.LogError("Nouveau client connecté.");

                    Thread clientThread = new(() => HandleClient(client))
                    {
                        IsBackground = true
                    };
                    clientThread.Start();
                }
            }
            catch (Exception ex)
            {
                _plugin.LogError($"Erreur : {ex.Message}");
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
            _plugin.LogError("Serveur arrêté.");
        }
        catch (Exception ex)
        {
            _plugin.LogError($"Erreur lors de l'arrêt du serveur : {ex.Message}");
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
            _plugin.LogError($"Erreur lors de l'envoi du message au client : {ex.Message}");
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
                        _plugin.LogError($"Client connecté : {clientName}. Total clients : {ConnectedClients.Count}");
                    }
                }
                else
                {
                    _plugin.LogError("Premier message invalide ou non reconnu, fermeture de la connexion.");
                    client.Close();
                    return;
                }
            }

            // Boucle de réception normale
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                _plugin.LogError($"[{clientName}] Message reçu : {msg}");

                // Ajoute ici le traitement du message si besoin
            }
        }
        catch (Exception ex)
        {
            _plugin.LogError($"Erreur côté client {clientName ?? "inconnu"} : {ex.Message}");
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
                        _plugin.LogError($"Client {clientName} déconnecté. Clients restants : {ConnectedClients.Count}");
                    }
                }
            }

            client.Close();
        }
    }
}
