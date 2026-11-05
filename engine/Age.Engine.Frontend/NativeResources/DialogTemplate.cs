using System.Buffers.Binary;
using System.Text;

namespace Age.Engine.Frontend.NativeResources;

/// <summary>One control of a dialog template, in dialog units relative to the dialog's client area.</summary>
/// <param name="Id">The control id; -1 for the 0xffff / 0xffffffff "no id" of static labels.</param>
/// <param name="ClassName">"Button", "Edit", "Static", "ListBox", "ScrollBar" or "ComboBox" for the predefined
/// atoms, else the window class name (e.g. "SysTabControl32", "msctls_trackbar32").</param>
/// <param name="Text">The control's text; for an SS_ICON static, the icon's resource name.</param>
/// <param name="TextId">A numeric text (0xffff, id): for an SS_ICON static, the icon's resource id.</param>
public sealed record DialogItem(int Id, string ClassName, string Text, int? TextId, uint Style, uint ExStyle, int X,
                                int Y, int Width, int Height)
{
    public bool Visible => (Style & DialogStyles.WsVisible) != 0;
    public bool Disabled => (Style & DialogStyles.WsDisabled) != 0;
    public bool TabStop => (Style & DialogStyles.WsTabStop) != 0;
    public bool Group => (Style & DialogStyles.WsGroup) != 0;
    public bool Border => (Style & DialogStyles.WsBorder) != 0;

    /// <summary>The low style bits that pick a predefined control's kind (BS_*, SS_* type).</summary>
    public uint TypeBits => ClassName switch
    {
        "Button" => Style & 0xf,
        "Static" => Style & 0x1f,
        _ => Style & 0xffff,
    };
}

/// <summary>A DIALOG or DIALOGEX resource: the frame in dialog units, its caption and font, and its controls in
/// creation (tab and z) order.</summary>
public sealed record DialogTemplate(uint Style, uint ExStyle, int X, int Y, int Width, int Height, string Title,
                                    int FontPointSize, int FontWeight, string FontFace,
                                    IReadOnlyList<DialogItem> Items)
{
    /// <summary>The first control with <paramref name="id"/>, or null.</summary>
    public DialogItem? Item(int id) => Items.FirstOrDefault(i => i.Id == id);
}

/// <summary>Window and control style bits that dialog templates use.</summary>
public static class DialogStyles
{
    public const uint WsVisible = 0x10000000, WsDisabled = 0x08000000, WsBorder = 0x00800000;
    public const uint WsSysMenu = 0x00080000, WsGroup = 0x00020000, WsTabStop = 0x00010000;
    public const uint DsSetFont = 0x40;

    public const uint BsPushButton = 0x0, BsDefPushButton = 0x1, BsCheckBox = 0x2, BsAutoCheckBox = 0x3;
    public const uint BsRadioButton = 0x4, Bs3State = 0x5, BsAuto3State = 0x6, BsGroupBox = 0x7;
    public const uint BsAutoRadioButton = 0x9;

    public const uint SsLeft = 0x0, SsCenter = 0x1, SsRight = 0x2, SsIcon = 0x3, SsBlackRect = 0x4;
    public const uint SsBlackFrame = 0x7, SsEtchedHorz = 0x10, SsEtchedVert = 0x11, SsEtchedFrame = 0x12;
    public const uint SsNoPrefix = 0x80, SsSunken = 0x1000;

    public const uint EsCenter = 0x1, EsRight = 0x2, EsMultiline = 0x4, EsAutoHScroll = 0x80, EsReadOnly = 0x800;
}

/// <summary>Reads RT_DIALOG templates (classic DLGTEMPLATE and DLGTEMPLATEEX) from a PE image, as CreateDialog
/// would read them.</summary>
public static class DialogTemplateResource
{
    /// <summary>Dialog <paramref name="name"/> in <paramref name="pe"/>, or null when there is none.</summary>
    public static DialogTemplate? Load(ReadOnlySpan<byte> pe, ResourceName name)
    {
        byte[]? bytes = PeResource.Read(pe, PeResource.RtDialog, name);
        if (bytes == null) return null;
        try
        {
            return Parse(bytes);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static DialogTemplate Parse(ReadOnlySpan<byte> t)
    {
        bool extended = BinaryPrimitives.ReadUInt16LittleEndian(t) == 1
                        && BinaryPrimitives.ReadUInt16LittleEndian(t[2..]) == 0xffff;
        int p;
        uint style, exStyle;
        int count;
        if (extended)
        {
            exStyle = BinaryPrimitives.ReadUInt32LittleEndian(t[8..]);
            style = BinaryPrimitives.ReadUInt32LittleEndian(t[12..]);
            count = BinaryPrimitives.ReadUInt16LittleEndian(t[16..]);
            p = 18;
        }
        else
        {
            style = BinaryPrimitives.ReadUInt32LittleEndian(t);
            exStyle = BinaryPrimitives.ReadUInt32LittleEndian(t[4..]);
            count = BinaryPrimitives.ReadUInt16LittleEndian(t[8..]);
            p = 10;
        }
        int x = BinaryPrimitives.ReadInt16LittleEndian(t[p..]);
        int y = BinaryPrimitives.ReadInt16LittleEndian(t[(p + 2)..]);
        int cx = BinaryPrimitives.ReadInt16LittleEndian(t[(p + 4)..]);
        int cy = BinaryPrimitives.ReadInt16LittleEndian(t[(p + 6)..]);
        p += 8;
        SzOrOrd(t, ref p, out _, out _);        // menu
        SzOrOrd(t, ref p, out _, out _);        // window class
        SzOrOrd(t, ref p, out string title, out _);
        int pointSize = 0, weight = 0;
        string face = "";
        if ((style & DialogStyles.DsSetFont) != 0)
        {
            pointSize = BinaryPrimitives.ReadUInt16LittleEndian(t[p..]);
            p += 2;
            if (extended)
            {
                weight = BinaryPrimitives.ReadUInt16LittleEndian(t[p..]);
                p += 4;                         // weight, italic, charset
            }
            face = ReadString(t, ref p);
        }

        var items = new List<DialogItem>(count);
        for (int i = 0; i < count; i++)
        {
            p = (p + 3) & ~3;
            uint itemStyle, itemExStyle;
            int id;
            if (extended)
            {
                itemExStyle = BinaryPrimitives.ReadUInt32LittleEndian(t[(p + 4)..]);
                itemStyle = BinaryPrimitives.ReadUInt32LittleEndian(t[(p + 8)..]);
                p += 12;
            }
            else
            {
                itemStyle = BinaryPrimitives.ReadUInt32LittleEndian(t[p..]);
                itemExStyle = BinaryPrimitives.ReadUInt32LittleEndian(t[(p + 4)..]);
                p += 8;
            }
            int ix = BinaryPrimitives.ReadInt16LittleEndian(t[p..]);
            int iy = BinaryPrimitives.ReadInt16LittleEndian(t[(p + 2)..]);
            int icx = BinaryPrimitives.ReadInt16LittleEndian(t[(p + 4)..]);
            int icy = BinaryPrimitives.ReadInt16LittleEndian(t[(p + 6)..]);
            p += 8;
            if (extended)
            {
                uint rawId = BinaryPrimitives.ReadUInt32LittleEndian(t[p..]);
                id = rawId is 0xffffffff or 0xffff ? -1 : unchecked((int)rawId);
                p += 4;
            }
            else
            {
                ushort rawId = BinaryPrimitives.ReadUInt16LittleEndian(t[p..]);
                id = rawId == 0xffff ? -1 : rawId;
                p += 2;
            }
            SzOrOrd(t, ref p, out string className, out int? classAtom);
            if (classAtom is int atom) className = AtomClass(atom);
            SzOrOrd(t, ref p, out string text, out int? textId);
            int extra = BinaryPrimitives.ReadUInt16LittleEndian(t[p..]);
            p += 2 + extra;
            items.Add(new DialogItem(id, className, text, textId, itemStyle, itemExStyle, ix, iy, icx, icy));
        }
        return new DialogTemplate(style, exStyle, x, y, cx, cy, title, pointSize, weight, face, items);
    }

    private static string AtomClass(int atom) => atom switch
    {
        0x80 => "Button",
        0x81 => "Edit",
        0x82 => "Static",
        0x83 => "ListBox",
        0x84 => "ScrollBar",
        0x85 => "ComboBox",
        _ => $"#{atom}",
    };

    // sz_Or_Ord: 0x0000 (none), 0xffff then a 16-bit ordinal, or a NUL-terminated UTF-16 string.
    private static void SzOrOrd(ReadOnlySpan<byte> t, ref int p, out string text, out int? ordinal)
    {
        ushort first = BinaryPrimitives.ReadUInt16LittleEndian(t[p..]);
        ordinal = null;
        text = "";
        if (first == 0xffff)
        {
            ordinal = BinaryPrimitives.ReadUInt16LittleEndian(t[(p + 2)..]);
            p += 4;
        }
        else
        {
            text = ReadString(t, ref p);
        }
    }

    private static string ReadString(ReadOnlySpan<byte> t, ref int p)
    {
        var text = new StringBuilder();
        while (true)
        {
            char c = (char)BinaryPrimitives.ReadUInt16LittleEndian(t[p..]);
            p += 2;
            if (c == '\0') return text.ToString();
            text.Append(c);
        }
    }
}

/// <summary>Dialog units to pixels for a dialog font whose average character width and height are the base
/// units, as MapDialogRect computes: x * baseX / 4 and y * baseY / 8, rounded as MulDiv rounds (half away from
/// zero).</summary>
public readonly record struct DialogBaseUnits(int X, int Y)
{
    public int ToPixelsX(int units) => MulDiv(units, X, 4);
    public int ToPixelsY(int units) => MulDiv(units, Y, 8);

    /// <summary>A control's pixel rectangle. CreateDialogIndirect converts x, y, cx and cy each on its own, so a
    /// width is not the difference of converted edges (the 終了 dialog's 134-unit prompt is 235 px, not 234).</summary>
    public (int X, int Y, int Width, int Height) ToPixels(int x, int y, int width, int height)
        => (ToPixelsX(x), ToPixelsY(y), ToPixelsX(width), ToPixelsY(height));

    /// <summary>GdiGetCharDimensions' average width: the summed advances of A–Z and a–z, halved and rounded
    /// ((sum / 26 + 1) / 2).</summary>
    public static int AverageCharWidth(Func<char, int> advance)
    {
        int sum = 0;
        for (char c = 'A'; c <= 'Z'; c++) sum += advance(c);
        for (char c = 'a'; c <= 'z'; c++) sum += advance(c);
        return (sum / 26 + 1) / 2;
    }

    public static int MulDiv(int number, int numerator, int denominator)
    {
        long product = (long)number * numerator;
        long half = denominator / 2;
        return (int)(product >= 0 ? (product + half) / denominator : (product - half) / denominator);
    }
}
