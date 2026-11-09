namespace Ome.Engine.Frontend.Shell;

public enum MenuItemKind { Command, Popup, Separator }

/// <summary>One item of a drawn menu: a command, a popup with its own items, or a separator. The shell builds the bar's
/// items from its menu tree (<see cref="ShellMenu"/>), and the frontend builds the window menu's. The state fields
/// (<see cref="Enabled"/>, <see cref="Checked"/>, <see cref="Text"/>) change as a refresh runs.</summary>
public sealed class MenuItem
{
    public MenuItemKind Kind { get; }
    /// <summary>The command id (native's WM_COMMAND wParam); 0 for popups and separators.</summary>
    public int Id { get; }
    /// <summary>MF_HELP: a bar item drawn right-justified, with every item after it.</summary>
    public bool RightJustified { get; }
    public List<MenuItem> Children { get; }

    /// <summary>The full text: "&amp;" marks the mnemonic, and a tab starts the accelerator text.</summary>
    public string Text { get; set; }
    public bool Enabled { get; set; }
    public bool Checked { get; set; }
    /// <summary>MFS_DEFAULT: Windows draws the item bold (the window menu's Close).</summary>
    public bool Default { get; init; }
    /// <summary>The HBMMENU_POPUP_* bitmap Windows draws in the item's check-mark column (the window menu's Restore,
    /// Minimize, Maximize and Close), or 0 for none.</summary>
    public int Bitmap { get; init; }

    public MenuItem(MenuItemKind kind, int id, string text, bool grayed = false, bool @checked = false,
                    bool rightJustified = false, List<MenuItem>? children = null)
    {
        Kind = kind;
        Id = id;
        Text = text;
        Enabled = !grayed;
        Checked = @checked;
        RightJustified = rightJustified;
        Children = children ?? [];
    }

    /// <summary>The label part of <see cref="Text"/>, before any tab.</summary>
    public string Label => Text.Split('\t')[0];

    /// <summary>The accelerator part of <see cref="Text"/>, after the first tab ("" without one).</summary>
    public string Accelerator => Text.IndexOf('\t') is var tab and >= 0 ? Text[(tab + 1)..] : "";

    /// <summary>The mnemonic character of <see cref="Label"/>, or '\0'.</summary>
    public char Mnemonic => MnemonicOf(Label) ?? '\0';

    /// <summary>A label's mnemonic: the character after its single "&amp;", upper-cased, or null ("&amp;&amp;" is a
    /// literal ampersand).</summary>
    public static char? MnemonicOf(string label)
    {
        for (int i = 0; i < label.Length - 1; i++)
        {
            if (label[i] != '&') continue;
            if (label[i + 1] == '&') { i++; continue; }
            return char.ToUpperInvariant(label[i + 1]);
        }
        return null;
    }

    /// <summary>The first item with command id <paramref name="id"/> in this item's subtree, depth first.</summary>
    public MenuItem? Find(int id) => Find(Children, id);

    public static MenuItem? Find(IEnumerable<MenuItem> items, int id)
    {
        foreach (MenuItem item in items)
        {
            if (item.Kind == MenuItemKind.Command && item.Id == id) return item;
            if (item.Kind == MenuItemKind.Popup && item.Find(id) is { } found) return found;
        }
        return null;
    }
}
