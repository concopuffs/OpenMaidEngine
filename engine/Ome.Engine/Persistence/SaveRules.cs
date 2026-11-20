using System.Buffers.Binary;
using System.Text;

namespace Ome.Engine.Persistence;

/// <summary>
/// The save facts that belong to an engine build rather than to a game (docs/superpowers/specs/2026-11-12-engine-layering-design.md
/// §7.5). The builds are declared in <c>Ome.Engine.Vm/EngineBuilds.cs</c>; a profile keeps the game's facts (magic,
/// game id, SYS4INI SAVEVERSION, bank dimensions).
/// </summary>
public sealed record SaveRules
{
    /// <param name="versionStamp">The four-character engine version the build keeps at <c>ctx+0x53b74</c> and writes
    /// at header +0x04 of every save file (docs/engine-re.md, "The compatibility id is the engine version").</param>
    /// <param name="numberedGfxRecordSize">The size of one graphics record in a numbered save, a constant in the
    /// build's save handlers (docs/sys4-format-notes.md; <see cref="NativeGfxSaveLayout"/>).</param>
    /// <param name="floatSectionsFromMinor">The save's minor version from which the build's layout 3 writes and
    /// restores the float globals and the float pointers, the second and fifth of a numbered save's six bank
    /// sections; null when it steps over both whatever the version (<see cref="KeepsFloatSections"/>).</param>
    public SaveRules(string versionStamp, int numberedGfxRecordSize, int? floatSectionsFromMinor = null)
    {
        ArgumentNullException.ThrowIfNull(versionStamp);
        if (versionStamp.Length != 4 || versionStamp.Any(character => character is < ' ' or > '~'))
            throw new ArgumentException(
                $"an engine version stamp is four printable ASCII characters, not '{versionStamp}'", nameof(versionStamp));
        if (!NativeGfxSaveLayout.IsSupported(numberedGfxRecordSize))
            throw new ArgumentException(
                $"unsupported numbered-save gfx record size 0x{numberedGfxRecordSize:x}", nameof(numberedGfxRecordSize));
        VersionStamp = versionStamp;
        NumberedGfxRecordSize = numberedGfxRecordSize;
        FloatSectionsFromMinor = floatSectionsFromMinor;
    }

    public string VersionStamp { get; }

    public int NumberedGfxRecordSize { get; }

    public int? FloatSectionsFromMinor { get; }

    /// <summary>
    /// Whether a numbered save of <paramref name="minorVersion"/> (SYS4INI SAVEVERSION modulo 100) holds its float
    /// globals and float pointers. Layout 3 always writes and restores the integers, the strings and the integer
    /// pointers, and never the string pointers. 4.24J steps over the floats and the float pointers too; 4.33G and
    /// 4.38D copy both when the minor version is above 0x13 (docs/sys4-format-notes.md, the six counts). A section
    /// native steps over holds what its buffer held, which it never clears, and a load leaves that bank zero.
    /// </summary>
    public bool KeepsFloatSections(int minorVersion)
        => FloatSectionsFromMinor is { } from && minorVersion >= from;

    /// <summary>The stamp as the header's little-endian dword (<see cref="NativeSaveIdentity.CompatibilityId"/>):
    /// "424J" is 0x4a343234.</summary>
    public uint CompatibilityId => BinaryPrimitives.ReadUInt32LittleEndian(Encoding.ASCII.GetBytes(VersionStamp));
}
