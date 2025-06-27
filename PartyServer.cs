using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FollowerServer;

public class PartyServer
{
    private TcpListener _server;
    private bool _isRunning;
    private readonly MainPlugin _plugin;
    public bool IsRunning => _isRunning;

    public readonly string ServerIP;

    public string username = "user";

    public bool IsLeaderAndServerHost => _plugin.Settings.Server.ToggleLeaderServer.Value;

    public List<TcpClient> ConnectedClients { get; set; } = []; // Liste des clients connectés

    public PartyServer(MainPlugin plugin)
    {
        _plugin = plugin;
        ServerIP = GetLocalIPAddress();

       
    }

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

        if (_server == null) _server = new TcpListener(System.Net.IPAddress.Parse(ServerIP), port);

        Thread serverThread = new(() =>
        {
            try
            {
                _server.Start();
                _plugin.LogError($"Serveur démarré sur {ServerIP}:{port}. En attente de connexions...");
                _isRunning = true;
                //remove inactive clients if they are not connected
                ConnectedClients.RemoveAll(c => !c.Connected);
                var _timer = new Timer(_ =>
                {
                    lock (ConnectedClients)
                    {
                        _plugin.LogError($"[Monitor] Clients actifs : {ConnectedClients.Count}");
                    }
                }, null, 0, 5000);

                while (_isRunning)
                {
                    TcpClient client = _server.AcceptTcpClient();
                    _plugin.LogError("Nouveau client connecté.");

                    lock (ConnectedClients)
                    {
                        ConnectedClients.Add(client);
                    }

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
            {
                return ip.ToString();
            }
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

        foreach (var client in ConnectedClients)
        {
            if (client.Connected)
            {
                SendMessageToClient(client, message);
            }
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
        try
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead;

            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                _plugin.LogError($"Message reçu d’un client : {msg}");

                // Tu peux ici désérialiser et traiter le message
            }
        }
        catch (Exception ex)
        {
            _plugin.LogError($"Client déconnecté ou erreur : {ex.Message}");
        }
        finally
        {
            // Retirer ce client de la liste des clients connectés
            lock (ConnectedClients)
            {
                ConnectedClients.Remove(client);
            }

            client.Close();
        }
    }

}