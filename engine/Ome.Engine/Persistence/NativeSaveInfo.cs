using System.Buffers.Binary;
using System.Text;

namespace Ome.Engine.Persistence;

/// <summary>
/// What one native save file says about itself, for the first save read of a game that is being added
/// (docs/adding-a-game.md, step 7): its identity, its version stamp, its bank counts and graphics record size, and
/// whether the codec carries it through an encode unchanged. <see cref="MismatchesWith"/> sets it against what a
/// profile and its engine build expect.
/// </summary>
/// <param name="Metadata">The container's header.</param>
/// <param name="PayloadBytes">The decoded payload's size.</param>
/// <param name="TailBytes">The bytes that follow the container: a numbered save's text history.</param>
/// <param name="Numbered">The payload as a numbered save (layout 3), or null when it is not one.</param>
/// <param name="NumberedDecodeError">Why it is not one.</param>
/// <param name="ReencodedSectionsAgree">A numbered save decoded, encoded and decoded again has the same sections.
/// </param>
/// <param name="ReencodedBytesAgree">The encode gives the payload's own bytes. A native save can differ where its
/// writer steps over a section or the padding of a graphics record, since it never clears its buffer.</param>
public sealed record NativeSaveInfo(
    NativeSaveMetadata Metadata,
    int PayloadBytes,
    int TailBytes,
    NativeNumberedSaveState? Numbered,
    string? NumberedDecodeError,
    bool ReencodedSectionsAgree,
    bool ReencodedBytesAgree)
{
    /// <summary>The four characters at header +0x04, the engine version that wrote the file ("424J").</summary>
    public string VersionStamp
    {
        get
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, Metadata.CompatibilityId);
            var text = new StringBuilder(4);
            foreach (byte value in bytes) text.Append(value is >= 0x20 and <= 0x7e ? (char)value : '?');
            return text.ToString();
        }
    }

    /// <summary>The six bank counts of a numbered save, in the file's order: integers, floats, strings, integer
    /// pointers, float pointers, string pointers.</summary>
    public int[]? BankCounts => Numbered is { } state
        ? [state.IntegerGlobals.Count, state.FloatGlobals.Count, state.StringGlobals.Count,
           state.PointerGlobals.Count, state.FloatPointers.Count, state.StringPointers.Count]
        : null;

    public static NativeSaveInfo Read(ReadOnlySpan<byte> file)
    {
        NativeSaveDocument document = NativeSaveContainerCodec.Decode(file);
        NativeNumberedSaveState? numbered = null;
        string? error = null;
        bool sectionsAgree = false, bytesAgree = false;
        try
        {
            numbered = NativeNumberedSaveCodec.Decode(document.Payload);
            byte[] reencoded = NativeNumberedSaveCodec.Encode(numbered);
            bytesAgree = reencoded.AsSpan().SequenceEqual(document.Payload);
            sectionsAgree = NativeSaveDiff.Compare(document, document with { Payload = reencoded }).Differences.Count == 0;
        }
        catch (InvalidDataException exception)
        {
            numbered = null;
            error = exception.Message;
        }
        return new NativeSaveInfo(document.Metadata, document.Payload.Length, file.Length - document.BytesConsumed,
                                  numbered, error, sectionsAgree, bytesAgree);
    }

    /// <summary>Where the file is not what <paramref name="identity"/> (a profile's facts with its engine build's
    /// save rules) expects: one sentence each, none when it agrees. AGE itself refuses a file on its magic, game id
    /// and save versions only (<see cref="NativeSaveIdentity.Validate"/>); the stamp, the bank counts and the record
    /// size are what the port writes, so they are compared too.</summary>
    public IReadOnlyList<string> MismatchesWith(NativeSaveIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var mismatches = new List<string>();
        if (Metadata.Magic != identity.Magic)
            mismatches.Add($"the magic is {Metadata.Magic}, and the profile's is {identity.Magic}");
        if (!identity.AcceptsGameId(Metadata.GameId))
            mismatches.Add($"the game id is '{Metadata.GameId}', and the profile's is '{identity.GameId}'");
        if (Metadata.SaveVersion1 != identity.SaveVersion1 || Metadata.SaveVersion2 != identity.SaveVersion2)
            mismatches.Add($"the save version is {Metadata.SaveVersion1}.{Metadata.SaveVersion2}, and the profile's is "
                           + $"{identity.SaveVersion1}.{identity.SaveVersion2}");
        if (Metadata.CompatibilityId != identity.CompatibilityId)
        {
            Span<byte> expected = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(expected, identity.CompatibilityId);
            mismatches.Add($"the version stamp is \"{VersionStamp}\", and the engine build's is "
                           + $"\"{Encoding.ASCII.GetString(expected)}\"");
        }
        if (Numbered is { } state)
        {
            if (identity.BankDimensions is { } dimensions && !dimensions.ToArray().SequenceEqual(BankCounts!))
                mismatches.Add($"the bank counts are {string.Join(", ", BankCounts!)}, and the profile's are "
                               + string.Join(", ", dimensions.ToArray()));
            if (state.GraphicsRecordSize != identity.NumberedGfxRecordSize)
                mismatches.Add($"the graphics record size is 0x{state.GraphicsRecordSize:x}, and the engine build's is "
                               + $"0x{identity.NumberedGfxRecordSize:x}");
        }
        return mismatches;
    }
}
