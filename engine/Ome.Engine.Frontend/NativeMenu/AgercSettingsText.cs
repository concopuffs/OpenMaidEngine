using System.Text;

namespace Ome.Engine.Frontend.NativeMenu;

/// <summary>
/// The texts AGERC's 設定 dialog (dialog resource 9, agerc_settings_dialog_proc@0x10006400) writes into its pages, as
/// its wsprintfA formats build them (docs/engine-re.md, "設定 dialog").
/// </summary>
public static class AgercSettingsText
{
    /// <summary>Its tabs, in TCM_INSERTITEM order. The menu commands open it on tab 0 (0x9c6c ﾒｯｾｰｼﾞの設定),
    /// 1 (0x9c68 ｻｳﾝﾄﾞ設定), 2 (0x9c6e その他の設定) or 3 (0x9c6d このｺﾝﾋﾟｭｰﾀの動作環境).</summary>
    public static readonly string[] Tabs = ["ﾒｯｾｰｼﾞ", "ｻｳﾝﾄﾞ", "その他", "動作環境"];

    /// <summary>The tab a menu command opens the dialog on, or -1.</summary>
    public static int StartTab(int menuCommand) => menuCommand switch
    {
        AgercMenu.MessageSettings => 0,
        AgercMenu.SoundSettings => 1,
        AgercMenu.OtherSettings => 2,
        AgercMenu.SystemInfo => 3,
        _ => -1,
    };

    /// <summary>Page 100 edit 0x3ee for trackbar 0x414 (100 − message:MessageSpeed).</summary>
    public static string MessageSpeed(int position) => position == 100 ? "ﾉｰｳｴｲﾄ" : $"ｳｴｲﾄ{100 - position}";

    /// <summary>Page 100 edit 0x3ef for trackbar 0x415 (16 − message:MesWinAlpha): pos × 100 / 16, truncated.</summary>
    public static string WindowOpacity(int position) => $"{position * 100 / 16}%";

    /// <summary>Page 100 edits 0x3f0 and 0x3f1 for trackbars 0x416 and 0x417 (AutoMessageTime0/1 in tenths of a
    /// second).</summary>
    public static string AutoWait(int position) => $"{position / 10}.{position % 10}秒";

    /// <summary>Sound page edit 0x3ee for trackbar 0x418 (sound:MusicFadeOnVoicePlayingVolume).</summary>
    public static string Percent(int position) => $"{position}%";

    /// <summary>A volume trackbar's position for an IAGEService volume getter's basis points.</summary>
    public static int VolumePosition(int basisPoints) => 100 - basisPoints / 100;

    /// <summary>The basis points a volume trackbar position stands for.</summary>
    public static int VolumeBasisPoints(int position) => (100 - position) * 100;

    /// <summary>agerc_format_drive_space: <c>" %c: "</c>, then the total and the free space in MiB (bytes >> 20, as a
    /// 32-bit int), each as <c>" %dMByte"</c> up to 1000 and <c>" %d.%2.2dGByte"</c> (MiB / 1000, (MiB % 1000) / 10)
    /// beyond, joined by <c>" /"</c>.</summary>
    public static string DriveSpace(char drive, long totalBytes, long freeBytes) =>
        $" {drive}: {Size(totalBytes)} /{Size(freeBytes)}";

    private static string Size(long bytes)
    {
        int mib = unchecked((int)(bytes >> 20));
        return mib < 1001 ? $" {mib}MByte" : $" {mib / 1000}.{mib % 1000 / 10:00}GByte";
    }

    /// <summary>Page 105 edit 0x3f1: <c>" %dMByte / %dMByte"</c>, GlobalMemoryStatus's dwTotalPhys / 2^20 + 1 and
    /// IAGEService +0x78's VRAM bytes / 1,000,000. A 32-bit AGE without /LARGEADDRESSAWARE sees at most 2 GiB − 1 of
    /// physical memory, and +0x78 computes DxDiag's display memory in MB × 1,000,000 as a 32-bit int, which wraps for
    /// 2148 MB and more (4095 MB reads −199).</summary>
    public static string Memory(long physicalBytes, int displayMemoryMb)
    {
        int physical = (int)Math.Min(physicalBytes, int.MaxValue);
        int vram = unchecked(displayMemoryMb * 1_000_000) / 1_000_000;
        return $" {physical / (1 << 20) + 1}MByte / {vram}MByte";
    }

    /// <summary>Page 105 edit 0x3ef: <c>" %s / %dMHz"</c>, the CPUID brand string from its first non-space
    /// character (trailing spaces kept) and the measured clock.</summary>
    public static string Cpu(string brand, int megahertz) => $" {brand.TrimStart(' ')} / {megahertz}MHz";

    /// <summary>The CPUID brand string from leaves 0x80000002..4 (EAX, EBX, ECX, EDX each), up to its first NUL.</summary>
    public static string CpuBrand(ReadOnlySpan<int> registers)
    {
        var bytes = new byte[registers.Length * 4];
        for (int i = 0; i < registers.Length; i++) BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), registers[i]);
        int end = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }

    /// <summary>agerc_os_version_string for an NT system: GetVersionExA's version and service pack. Windows 8 and
    /// later report 6.2.9200 to an unmanifested program like AGE, so they all read " Windows Vista 以降  (6.2.9200)".
    /// </summary>
    public static string WindowsVersion(int major, int minor, int build, string servicePack)
    {
        string name = (major, minor) switch
        {
            (< 5, _) => " Windows NT ",
            (5, 0) => " Windows 2000 ",
            (5, 1) => " Windows XP ",
            (5, 2) => " Windows Server 2003 ",
            (6, 0) => " Windows Vista ",
            _ => " Windows Vista 以降 ",
        };
        return $"{name}{servicePack} ({major}.{minor}.{build})";
    }

    /// <summary>agerc_directx_version_string: HKLM\Software\Microsoft\DirectX's InstalledVersion (two dwords, each's
    /// top byte) and Version, or empty when Version is missing or empty.</summary>
    public static string DirectXVersion(ReadOnlySpan<byte> installedVersion, string? version)
    {
        if (string.IsNullOrEmpty(version)) return "";
        int major = installedVersion.Length >= 4 ? installedVersion[3] : 0;
        int minor = installedVersion.Length >= 8 ? installedVersion[7] : 0;
        return $" DirectX {(sbyte)major}.{(sbyte)minor} ({version})";
    }
}
