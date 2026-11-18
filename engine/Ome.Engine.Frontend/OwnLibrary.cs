using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// The folder of the native library this code was compiled into. A release compiles the engine with NativeAOT into one
/// library, which Godot loads from its data folder, beside the FFmpeg shim; there <see cref="AppContext.BaseDirectory"/>
/// names the executable's folder instead. Code the JIT compiles lies in no library, so under the JIT this is null.
/// </summary>
internal static class OwnLibrary
{
    private static readonly Lazy<string?> Folder = new(Find);

    public static string? Directory => Folder.Value;

    private static string? Find()
    {
        if (RuntimeFeature.IsDynamicCodeSupported) return null;
        // Under NativeAOT a type's handle is the address of its method table, which lies in the library's image.
        nint address = typeof(OwnLibrary).TypeHandle.Value;
        string? path = OperatingSystem.IsWindows() ? WindowsPath(address) : UnixPath(address);
        return path == null ? null : Path.GetDirectoryName(path);
    }

    private static string? WindowsPath(nint address)
    {
        const uint FromAddress = 0x4, UnchangedRefcount = 0x2;
        if (GetModuleHandleExW(FromAddress | UnchangedRefcount, address, out nint module) == 0) return null;
        const int Capacity = 32768;
        nint buffer = Marshal.AllocHGlobal(Capacity * sizeof(char));
        try
        {
            uint length = GetModuleFileNameW(module, buffer, Capacity);
            return length is 0 or >= Capacity ? null : Marshal.PtrToStringUni(buffer, (int)length);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string? UnixPath(nint address)
        => dladdr(address, out DlInfo info) != 0 && info.FileName != 0 ? Marshal.PtrToStringUTF8(info.FileName) : null;

    [DllImport("kernel32.dll")]
    private static extern int GetModuleHandleExW(uint flags, nint address, out nint module);

    [DllImport("kernel32.dll")]
    private static extern uint GetModuleFileNameW(nint module, nint buffer, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct DlInfo
    {
        public nint FileName, BaseAddress, SymbolName, SymbolAddress;
    }

    // glibc keeps libdl.so.2 and its dladdr after 2.34 moved them into libc, so this name serves the 2.28 floor and
    // newer releases alike.
    [DllImport("libdl.so.2")]
    private static extern int dladdr(nint address, out DlInfo info);
}
