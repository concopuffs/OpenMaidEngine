using System.Buffers.Binary;
using System.Text;
using Age.Engine.Frontend.NativeResources;

namespace Age.Engine.Frontend.NativeMenu;

public enum NativeMenuItemKind { Command, Popup, Separator }

/// <summary>One item of a Win32 menu: a command, a popup with its own items, or a separator. The state fields
/// (<see cref="Enabled"/>, <see cref="Checked"/>, <see cref="Text"/>) change as AGERC refreshes the menu.</summary>
public sealed class NativeMenuItem
{
    public NativeMenuItemKind Kind { get; }
    /// <summary>The command id (WM_COMMAND wParam); 0 for popups and separators.</summary>
    public int Id { get; }
    /// <summary>MF_HELP: a bar item drawn right-justified, with every item after it.</summary>
    public bool RightJustified { get; }
    public List<NativeMenuItem> Children { get; }

    /// <summary>The full text: "&amp;" marks the mnemonic, and a tab starts the accelerator text.</summary>
    public string Text { get; set; }
    public bool Enabled { get; set; }
    public bool Checked { get; set; }

    public NativeMenuItem(NativeMenuItemKind kind, int id, string text, bool grayed = false, bool @checked = false,
                          bool rightJustified = false, List<NativeMenuItem>? children = null)
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

    /// <summary>The mnemonic character (the one after a single "&amp;"), upper-cased, or '\0'.</summary>
    public char Mnemonic
    {
        get
        {
            string label = Label;
            for (int i = 0; i < label.Length - 1; i++)
            {
                if (label[i] != '&') continue;
                if (label[i + 1] == '&') { i++; continue; }
                return char.ToUpperInvariant(label[i + 1]);
            }
            return '\0';
        }
    }

    /// <summary>The first item with command id <paramref name="id"/> in this item's subtree, depth first.</summary>
    public NativeMenuItem? Find(int id) => Find(Children, id);

    public static NativeMenuItem? Find(IEnumerable<NativeMenuItem> items, int id)
    {
        foreach (NativeMenuItem item in items)
        {
            if (item.Kind == NativeMenuItemKind.Command && item.Id == id) return item;
            if (item.Kind == NativeMenuItemKind.Popup && item.Find(id) is { } found) return found;
        }
        return null;
    }
}

/// <summary>Reads a menu resource (RT_MENU) from a 32- or 64-bit PE image without loading it, as
/// <c>LoadMenu</c> would build it. Only the classic MENU template is read; MENUEX templates return null.</summary>
public static class NativeMenuResource
{
    private const ushort MfGrayed = 0x1, MfChecked = 0x8, MfPopup = 0x10, MfEnd = 0x80, MfHelp = 0x4000;

    /// <summary>The bar items of menu <paramref name="menuId"/> in <paramref name="pe"/>, or null when the image has
    /// no such menu or is not a PE image.</summary>
    public static List<NativeMenuItem>? Load(ReadOnlySpan<byte> pe, int menuId)
    {
        try
        {
            return PeResource.TryFind(pe, PeResource.RtMenu, menuId, out int offset, out int size)
                ? ParseTemplate(pe.Slice(offset, size))
                : null;
        }
        catch (Exception error) when (error is ArgumentOutOfRangeException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Parse a classic menu template (MENUITEMTEMPLATEHEADER and its items). Null for a MENUEX template.</summary>
    public static List<NativeMenuItem>? ParseTemplate(ReadOnlySpan<byte> template)
    {
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(template);
        ushort headerSize = BinaryPrimitives.ReadUInt16LittleEndian(template[2..]);
        if (version != 0) return null;
        int offset = 4 + headerSize;
        return ParseItems(template, ref offset);
    }

    private static List<NativeMenuItem> ParseItems(ReadOnlySpan<byte> template, ref int offset)
    {
        var items = new List<NativeMenuItem>();
        while (true)
        {
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(template[offset..]);
            offset += 2;
            bool popup = (flags & MfPopup) != 0;
            int id = 0;
            if (!popup)
            {
                id = BinaryPrimitives.ReadUInt16LittleEndian(template[offset..]);
                offset += 2;
            }
            string text = ReadString(template, ref offset);
            bool grayed = (flags & MfGrayed) != 0, @checked = (flags & MfChecked) != 0;
            bool rightJustified = (flags & MfHelp) != 0;
            if (popup)
                items.Add(new NativeMenuItem(NativeMenuItemKind.Popup, 0, text, grayed, @checked, rightJustified,
                                             ParseItems(template, ref offset)));
            else if (id == 0 && text.Length == 0)
                items.Add(new NativeMenuItem(NativeMenuItemKind.Separator, 0, "", grayed: true));
            else
                items.Add(new NativeMenuItem(NativeMenuItemKind.Command, id, text, grayed, @checked, rightJustified));
            if ((flags & MfEnd) != 0) return items;
        }
    }

    private static string ReadString(ReadOnlySpan<byte> template, ref int offset)
    {
        var text = new StringBuilder();
        while (true)
        {
            char c = (char)BinaryPrimitives.ReadUInt16LittleEndian(template[offset..]);
            offset += 2;
            if (c == '\0') return text.ToString();
            text.Append(c);
        }
    }
}
