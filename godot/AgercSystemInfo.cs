using System;
using System.IO;
using System.Runtime.InteropServices;
using Ome.Engine.Frontend.NativeMenu;
using Godot;

/// <summary>
/// What the 設定 dialog's 動作環境 page (dialog 105, agerc_settings_system_info_page_proc@0x10005870) reads about the
/// computer, gathered as AGE would see it. Native measures the CPU clock from RDTSC over its 1.1 s timer and reads the
/// display memory from DxDiag; the port reads the clock Windows recorded at boot (the registry's ~MHz, which equals
/// native's measurement on an invariant TSC) and the adapter's dedicated memory from DirectX's adapter list, capped at
/// DxDiag's 4095 MB. Off Windows the Windows-only fields stay as native's fallbacks: no DirectX line, clock 0.
/// </summary>
public static class AgercSystemInfo
{
    /// <summary>Edit 0x3ee.</summary>
    public static string OsVersion()
    {
        if (!OperatingSystem.IsWindows()) return " " + RuntimeInformation.OSDescription;
        Version version = System.Environment.OSVersion.Version;
        // GetVersionExA without a compatibility manifest: Windows 8 and later report 6.2.9200 and no service pack.
        if (version.Major > 6 || (version.Major == 6 && version.Minor >= 2))
            return AgercSettingsText.WindowsVersion(6, 2, 9200, "");
        return AgercSettingsText.WindowsVersion(version.Major, version.Minor, version.Build,
                                                System.Environment.OSVersion.ServicePack);
    }

    /// <summary>Edit 0x3f2, from the 32-bit registry view a 32-bit AGE reads.</summary>
    public static string DirectXVersion()
    {
        if (!OperatingSystem.IsWindows()) return "";
        try
        {
            using var machine = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,
                                                                         Microsoft.Win32.RegistryView.Registry32);
            using var key = machine.OpenSubKey(@"Software\Microsoft\DirectX");
            return AgercSettingsText.DirectXVersion(key?.GetValue("InstalledVersion") as byte[] ?? [],
                                                    key?.GetValue("Version") as string);
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException
                                          or IOException)
        {
            return "";
        }
    }

    /// <summary>Edits 0x3f3 and 0x3f4: the drive holding <paramref name="path"/>.</summary>
    public static string DriveSpace(string path)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? path;
            var drive = new DriveInfo(root);
            return AgercSettingsText.DriveSpace(root.Length > 0 ? root[0] : ' ', drive.TotalSize, drive.TotalFreeSpace);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "";
        }
    }

    /// <summary>Edit 0x3ef.</summary>
    public static string Cpu() => AgercSettingsText.Cpu(CpuBrand(), CpuMegahertz());

    private static string CpuBrand()
    {
        if (!System.Runtime.Intrinsics.X86.X86Base.IsSupported) return "";
        (int top, _, _, _) = System.Runtime.Intrinsics.X86.X86Base.CpuId(unchecked((int)0x80000000), 0);
        if ((uint)top < 0x80000004) return "";
        var registers = new int[12];
        for (int leaf = 0; leaf < 3; leaf++)
        {
            (int eax, int ebx, int ecx, int edx) =
                System.Runtime.Intrinsics.X86.X86Base.CpuId(unchecked((int)0x80000002) + leaf, 0);
            registers[leaf * 4] = eax;
            registers[leaf * 4 + 1] = ebx;
            registers[leaf * 4 + 2] = ecx;
            registers[leaf * 4 + 3] = edx;
        }
        return AgercSettingsText.CpuBrand(registers);
    }

    private static int CpuMegahertz()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("~MHz") is int mhz ? mhz : 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException
                                          or IOException)
        {
            return 0;
        }
    }

    /// <summary>Edit 0x3f1.</summary>
    public static string Memory() =>
        AgercSettingsText.Memory(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, DisplayMemoryMb());

    // DxDiag's display memory for the adapter Godot renders with: DirectX's adapter list (one key per adapter
    // instance, stale ones included) gives its DedicatedVideoMemory; DxDiag reports at most 4095 MB.
    private static int DisplayMemoryMb()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        string adapter = RenderingServer.GetVideoAdapterName();
        try
        {
            using var directX = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\DirectX");
            if (directX == null) return 0;
            foreach (string name in directX.GetSubKeyNames())
            {
                using var key = directX.OpenSubKey(name);
                if (key?.GetValue("Description") as string != adapter) continue;
                if (key.GetValue("DedicatedVideoMemory") is long bytes) return (int)Math.Min(bytes >> 20, 4095);
            }
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException
                                          or IOException)
        {
        }
        return 0;
    }
}
