# M0 question 2: lists a Windows program's resources (types and names), its version fields and its manifest, without
# running it. Usage: pe-resources.ps1 PROGRAM.exe [...]
param([Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Paths)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class PeResources
{
    private const uint LoadAsDataFile = 0x00000002 | 0x00000020; // LOAD_LIBRARY_AS_DATAFILE | AS_IMAGE_RESOURCE
    private delegate bool EnumTypes(IntPtr module, IntPtr type, IntPtr param);
    private delegate bool EnumNames(IntPtr module, IntPtr type, IntPtr name, IntPtr param);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll")] private static extern bool EnumResourceTypesW(IntPtr module, EnumTypes callback, IntPtr param);
    [DllImport("kernel32.dll")] private static extern bool EnumResourceNamesW(IntPtr module, IntPtr type, EnumNames callback, IntPtr param);
    [DllImport("kernel32.dll")] private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module, IntPtr info);
    [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr data);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module, IntPtr info);

    private static string Id(IntPtr value)
    {
        long n = value.ToInt64();
        if ((n >> 16) == 0)
        {
            switch (n)
            {
                case 3: return "RT_ICON";
                case 14: return "RT_GROUP_ICON";
                case 16: return "RT_VERSION";
                case 24: return "RT_MANIFEST";
                default: return "#" + n;
            }
        }
        return Marshal.PtrToStringUni(value);
    }

    public static string[] List(string path, out string manifest)
    {
        manifest = null;
        IntPtr module = LoadLibraryExW(path, IntPtr.Zero, LoadAsDataFile);
        if (module == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var found = new List<string>();
        var types = new List<IntPtr>();
        try
        {
            EnumResourceTypesW(module, (m, t, p) => { types.Add(t); return true; }, IntPtr.Zero);
            foreach (IntPtr type in types)
            {
                var names = new List<string>();
                IntPtr manifestName = IntPtr.Zero;
                EnumResourceNamesW(module, type, (m, t, n, p) =>
                {
                    names.Add((n.ToInt64() >> 16) == 0 ? "#" + n.ToInt64() : Marshal.PtrToStringUni(n));
                    if (type.ToInt64() == 24 && manifestName == IntPtr.Zero) manifestName = n;
                    return true;
                }, IntPtr.Zero);
                found.Add(Id(type) + ": " + string.Join(", ", names));
                if (manifestName != IntPtr.Zero)
                {
                    IntPtr info = FindResourceW(module, manifestName, type);
                    IntPtr data = LockResource(LoadResource(module, info));
                    byte[] bytes = new byte[SizeofResource(module, info)];
                    Marshal.Copy(data, bytes, 0, bytes.Length);
                    manifest = Encoding.UTF8.GetString(bytes);
                }
            }
        }
        finally { FreeLibrary(module); }
        return found.ToArray();
    }
}
'@
foreach ($path in $Paths) {
    $full = (Resolve-Path $path).Path
    Write-Host "== $full"
    $manifest = $null
    [PeResources]::List($full, [ref]$manifest) | ForEach-Object { Write-Host "  $_" }
    $v = (Get-Item $full).VersionInfo
    Write-Host ("  version: FileVersion={0} ProductVersion={1} Product='{2}' Company='{3}' Description='{4}'" -f
        $v.FileVersion, $v.ProductVersion, $v.ProductName, $v.CompanyName, $v.FileDescription)
    if ($manifest) {
        $flat = ($manifest -replace '\s+', ' ').Trim()
        Write-Host ("  manifest: dpiAwareness={0} activeCodePage={1} supportedOS={2}" -f
            ($flat -match 'PerMonitorV2'), ($flat -match 'activeCodePage'), ($flat -match 'supportedOS'))
    } else {
        Write-Host "  manifest: (none)"
    }
}
