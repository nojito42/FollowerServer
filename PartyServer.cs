﻿using System;
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
    private FollowerPlugin _plugin;
    public bool IsRunning => _isRunning;

    private readonly int _port = 5051;
    public readonly string ServerIP;

    public string username = "user";

    public bool IsLeaderAndServerHost => _plugin.Settings.ServerSubMenu.ToggleLeaderServer.Value;

    private List<TcpClient> _clients = new List<TcpClient>();  // Liste des clients connectés

    public PartyServer(FollowerPlugin plugin)
    {
        _plugin = plugin;
        ServerIP = GetLocalIPAddress();
        _plugin.LogError($"Serveur initialisé sur {ServerIP}:{_port}.");
    }

    public void Start()
    {
        if (_isRunning || !IsLeaderAndServerHost)
        {
            _plugin.LogError("Server Already Running, or you are not the leader and server host.");
            return;
        }

        if (_server == null) _server = new TcpListener(System.Net.IPAddress.Parse(ServerIP), _port);

        Thread serverThread = new Thread(() =>
        {
            try
            {
                _server.Start();
                _plugin.LogError($"Serveur démarré sur {ServerIP}:{_port}. En attente de connexions...");
                _isRunning = true;

                while (_isRunning)
                {
                    TcpClient client = _server.AcceptTcpClient();
                    _plugin.LogError("Nouveau client connecté.");
                    if(_clients != null && !_clients.Any(c => c.Client.RemoteEndPoint == client.Client.RemoteEndPoint))
                    {
                        _clients.Add(client);  // Ajouter le client à la liste
                    }
                }
            }
            catch (Exception ex)
            {
                _plugin.LogError($"Erreur : {ex.Message}");
                _isRunning = false;
            }
        });

        serverThread.IsBackground = true;
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

    private string GetLocalIPAddress()
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

        foreach (var client in _clients)
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
}