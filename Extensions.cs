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
    //public static bool IsLeaderOnSameMap(this Leader l) => l.Element?.ChildCount == 3;
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
    public static DateTime lastActionTime = DateTime.MinValue;
    public static int ActionCooldownMS = 50;
    public static bool TryDoAction(this MainPlugin p, Action act)
    {
        if ((DateTime.Now - lastActionTime).TotalMilliseconds < ActionCooldownMS)
            return false;

        lastActionTime = DateTime.Now;
        act();
        return true;
    }

    public static List<Buff> GetBuffs(this MainPlugin p)
    {
        return p.GameController.Player.Buffs;
    }
}