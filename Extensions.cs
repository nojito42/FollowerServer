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
namespace FollowerPlugin;

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
            Thread.Sleep(10);
            return true;
        }
        return false;
    }
    public static Element Party(this GameController gc) => gc.IngameState.IngameUi.PartyElement.Children[0];
    public static bool IsLeaderOnSameMap(this Leader l) => l.Element?.ChildCount == 3;
    public static Vector3 ParseVector3(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Input cannot be null or empty.", nameof(input));
        }

        // Remove angle brackets and normalize the separators
        input = input.Replace("<", "").Replace(">", "").Replace(" ", "").Replace(",", ".");

        // Split the string into components
        string[] components = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (components.Length != 3)
        {
            throw new FormatException("Input string must contain exactly three components.");
        }

        // Parse each component into a float
        if (float.TryParse(components[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(components[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
            float.TryParse(components[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            return new Vector3(x, y, z);
        }
        else
        {
            throw new FormatException("One or more components could not be parsed as float.");
        }
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
    public static Shortcut GetShortcutByNameContains(this FollowerServer.FollowerPlugin p, string name)
    {
        //p.GameController.IngameState.ShortcutSettings.Shortcuts
        return p.shortcuts.FirstOrDefault(s => s.ToString().Contains(name));
    }
}
