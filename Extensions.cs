using ExileCore;
using ExileCore.PoEMemory;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Shortcut = GameOffsets.Shortcut;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;
using FollowerPlugin;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared;

namespace FollowerServer;

public static class Extensions
{
    public static bool CloseWindowIfOpen(this GameController gc)
    {
        var m = gc.IngameState.IngameUi;
        var flaggedPanelsList = new List<Element> { m.TreePanel, m.AtlasTreePanel, m.OpenLeftPanel, m.InventoryPanel, m.SettingsPanel, m.ChatPanel.Children[0] };
        if (flaggedPanelsList.Any(fp => fp.IsVisible))
        {
            Input.KeyDown(Keys.Escape);
            Thread.Sleep(10);
            Input.KeyUp(Keys.Escape);
            Thread.Sleep(300);
            return true;
        }
        return false;
    }
    public static List<PartyElementPlayerElement> Party(this GameController gc) => gc.IngameState.IngameUi.PartyElement.PlayerElements;

    public static List<Shortcut> GetSkillBarShortCuts(this MainPlugin p)
    {
        return [.. p.GameController.IngameState.ShortcutSettings.Shortcuts.Skip(7).Take(13)];
    }
    public static bool IsShortCutPressed(this Shortcut shortcut)
    {
        return shortcut.MainKey != ConsoleKey.None &&
               (shortcut.Modifier != GameOffsets.ShortcutModifier.None ?
               Input.IsKeyDown((Keys)shortcut.MainKey) && Input.IsKeyDown((Keys)shortcut.Modifier) :
               Input.IsKeyDown((Keys)shortcut.MainKey));
    }
    public static bool PressShortCut(this Shortcut shortcut, int delayMS)
    {
        var shortcutKey = (Keys)shortcut.MainKey;
        var shortcutModifier = (Keys)shortcut.Modifier;

        if (shortcutKey != Keys.None)
        {
            if (shortcutModifier != Keys.None)
            {
                Input.KeyDown(shortcutModifier);
            }
            Input.KeyDown(shortcutKey);
            Thread.Sleep(10);

            Input.KeyUp(shortcutKey);
            if (shortcutModifier != Keys.None)
            {

                Input.KeyUp(shortcutModifier);
            }
            Thread.Sleep(delayMS);
        }
        return true;
    }
}
public static class FollowerPluginExtensions
{
    private static DateTime lastActionTime = DateTime.MinValue;
    private static int actionCooldownMS = 50;
    public static bool TryDoAction(this MainPlugin p, Action act)
    {
        if ((DateTime.Now - lastActionTime).TotalMilliseconds < actionCooldownMS)
            return false;

        lastActionTime = DateTime.Now;
        act();
        return true;
    }
    public static List<Buff> GetBuffs(this MainPlugin p)
    {
        return p.GameController.Player.Buffs;
    }
    public static void DisconnectWithMessage(this MainPlugin p, string message)
    {
        p.Log(message,LogLevel.Info, 5);
        p.PartyClient?.Disconnect();
        p.PartyClient = null;
        p.PartyServer?.Stop();
        p.PartyServer = null;
        p.IsTaskRunning = false;
    }

    public static void Log(this MainPlugin p, string message, LogLevel l = LogLevel.Info, float duration = 1)
    {
        if (p == null || p.GameController == null || p.GameController.IngameState == null)
            return;

        var colors = new SharpDX.Color[3]
        {
            new SharpDX.Color(255, 255, 255), // Info
            new SharpDX.Color(255, 165, 0), // Warning
            new SharpDX.Color(255, 0, 0) // Error
        };
      
        if (l >= (LogLevel)p.Settings.Misc.LogLevel.Values.IndexOf(p.Settings.Misc.LogLevel.Value) && p.Settings.Misc.ShowDebugInfo)
            p.LogMessage($"[{l}] {message}", duration, colors[(int)l]);
    }
}
public static class ServerClientExtensions
{
    public static Coroutine LoginCoroutine { get; private set; }
    public static void ToggleLeaderServer(this MainPlugin p)
    {
        if (p.Settings.Server.ToggleLeaderServer.Value)
        {
            p.PartyServer ??= new PartyServer(p);

            if (p.Settings.Server.ToggleLeaderServer.Value && !p.PartyServer.IsRunning)
            {
                p.Log("Starting server...");
               
                p.PartyServer.Start();
                MainPlugin.Status = eStatus.Running;
            }
            else
            {
                p.Settings.Server.ToggleLeaderServer.Value = false;
            }
        }
        else
        {
            if (p.PartyServer != null && p.PartyServer.IsRunning)
            {
                p.Log("Stopping server...");
                p.PartyServer.Stop();
            }

        }
    }
    public static void ConnectTask(this MainPlugin p)
    {
        if (p.PartyClient == null)
        {
            p.PartyClient = new PartyClient(p);
        }
        if (p.PartyClient.IsConnected)
            return;

        // Si une coroutine existe déjà et n'est pas terminée, on ne la relance pas
        if (LoginCoroutine != null && !LoginCoroutine.IsDone)
        {
            p.Log("Coroutine already running. Skipping new start.");
            return;
        }

        // Si une coroutine existe mais est finie, on la clean
        if (LoginCoroutine != null && LoginCoroutine.IsDone)
        {

            LoginCoroutine = null;
        }

        p.Log("Starting connection task to party server...");

        LoginCoroutine = new Coroutine(() =>
        {
            p.IsTaskRunning = true;

            if (p.PartyClient?.IsConnected == false)
            {
                // Si le plugin est désactivé ou si les paramètres ont changé
                if (!p.Settings.Enable || !p.Settings.Party.ConnectClient)
                {
                    p.Log("Plugin disabled or settings turned off. Stopping coroutine.");
                    p.DisconnectWithMessage("FollowerPlugin is disabled, stopping connection task.");


                    return;
                }

                if (p.GameController.Party()?.Count > 0)
                {
                    if (!p.PartyClient.IsConnected)
                    {
                        p.PartyClient.Connect();
                        p.Log("Attempting to reconnect to party server...");
                    }

                }

            }
            else
            {
                Core.ParallelRunner.FindByName("ConnectRoutine").Done(true);
                LoginCoroutine = null; // Nettoyage de la coroutine
                p.Log("Already connected to party server.");
            }

            p.Log("Connection coroutine ended.");
            p.IsTaskRunning = false;

        }, 1500, p, "ConnectRoutine", true);

        if (LoginCoroutine != null)
            Core.ParallelRunner.Run(LoginCoroutine);
    }
}
public enum LogLevel
{
    Info,
    Warning,
    Error
}