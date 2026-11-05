using System.Globalization;
using Age.Engine.Model;

namespace Age.Engine.Persistence;

/// <summary>
/// AGE's screenshot BMPs (engine_save_screenshot_bmp@0x407ce0; docs/engine-re.md, "Native menu bar"). F1 and the menu
/// bar's 表示画面を保存 (0x9c46) both save the presented frame as <c>CG%6.6d.BMP</c>:
/// <list type="bullet">
/// <item>in <c>system:SaveBMPPath</c> when it is set, otherwise in the Desktop (CSIDL_DESKTOPDIRECTORY, created when
/// missing);</item>
/// <item>under the first name that does not exist yet, probed from a counter that starts at 0 with the process and
/// advances on every probe; once it reaches 1,000,000 nothing more is written;</item>
/// <item>with op 0x1ae's writer: a bottom-up 24-bit BMP whose bfSize leaves out the 14-byte file header
/// (<see cref="NumberedThumbnailCodec"/>).</item>
/// </list>
/// Native shows nothing on success or failure, so a failed write only returns null.
/// </summary>
public sealed class ScreenshotBmpStore
{
    public const string SaveBmpPathSection = "system";
    public const string SaveBmpPathKey = "SaveBMPPath";
    /// <summary>The counter's bound: native gives up when the incremented counter reaches it.</summary>
    public const int NameLimit = 1_000_000;

    private readonly Func<string?> _desktopDirectory;
    private readonly object _lock = new();
    private int _counter;   // DAT_005c4544

    /// <param name="desktopDirectory">The Desktop folder, created on demand (SHGetFolderPathA with
    /// CSIDL_FLAG_CREATE); null when there is none.</param>
    /// <param name="initialCounter">The first name probed; a new process starts at 0.</param>
    public ScreenshotBmpStore(Func<string?> desktopDirectory, int initialCounter = 0)
    {
        ArgumentNullException.ThrowIfNull(desktopDirectory);
        ArgumentOutOfRangeException.ThrowIfNegative(initialCounter);
        _desktopDirectory = desktopDirectory;
        _counter = initialCounter;
    }

    /// <summary>The next counter value probed (native's DAT_005c4544).</summary>
    public int Counter
    {
        get { lock (_lock) return _counter; }
    }

    /// <summary>The folder a screenshot goes to: <paramref name="saveBmpPath"/>, or the Desktop when it is empty. Null
    /// when it is empty and there is no Desktop, where native's bare name lands in the working directory.</summary>
    public string? TargetDirectory(string saveBmpPath)
        => saveBmpPath.Length != 0 ? saveBmpPath : _desktopDirectory();

    public static string FileName(int counter)
        => "CG" + counter.ToString("000000", CultureInfo.InvariantCulture) + ".BMP";

    /// <summary>Save <paramref name="frame"/> under the first free name. The path written, or null when the counter
    /// ran out or the write failed.</summary>
    public string? Save(RgbaImage frame, string saveBmpPath)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(saveBmpPath);
        string? directory = TargetDirectory(saveBmpPath);
        lock (_lock)
        {
            while (true)
            {
                string name = FileName(_counter);
                string path = directory == null ? name : Path.Join(directory, name);
                bool exists = File.Exists(path);
                _counter++;
                if (_counter >= NameLimit) return null;
                if (exists) continue;
                try
                {
                    File.WriteAllBytes(path, NumberedThumbnailCodec.Encode(frame));
                    return path;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    return null;
                }
            }
        }
    }
}
