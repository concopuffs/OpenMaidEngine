namespace Age.Engine.Sys4;

/// <summary>
/// Encoder for Eushully's 4 KiB-ring LZSS stream. Tokens are grouped under an LSB-first flag byte;
/// one bits are literals and zero bits are 12-bit ring offsets plus a four-bit length-minus-three.
/// </summary>
public static class LzssEncoder
{
    private const int RingSize = 0x1000;
    private const int RingMask = RingSize - 1;
    private const int InitialRingPosition = 0xfee;
    private const int MaxMatchLength = 18;
    private const int CandidateLimit = 128;

    /// <summary>
    /// Compress <paramref name="source"/>. When the encoded stream would not be smaller, return the
    /// original bytes; the native container marks that case by storing equal source/stored lengths.
    /// </summary>
    public static byte[] EncodeOrVerbatim(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty) return [];

        var output = new List<byte>(source.Length);
        var positionsByPrefix = new Dictionary<int, LinkedList<int>>();
        int position = 0;

        while (position < source.Length)
        {
            int flagIndex = output.Count;
            output.Add(0);
            byte flags = 0;

            for (int bit = 0; bit < 8 && position < source.Length; bit++)
            {
                (int candidate, int length) = FindMatch(source, position, positionsByPrefix);
                if (length >= 3)
                {
                    int ringOffset = (InitialRingPosition + candidate) & RingMask;
                    output.Add((byte)ringOffset);
                    output.Add((byte)(((ringOffset >> 4) & 0xf0) | (length - 3)));
                    AddPositions(source, position, length, positionsByPrefix);
                    position += length;
                }
                else
                {
                    flags |= (byte)(1 << bit);
                    output.Add(source[position]);
                    AddPositions(source, position, 1, positionsByPrefix);
                    position++;
                }
            }

            output[flagIndex] = flags;
        }

        return output.Count < source.Length ? output.ToArray() : source.ToArray();
    }

    private static (int Candidate, int Length) FindMatch(
        ReadOnlySpan<byte> source,
        int position,
        Dictionary<int, LinkedList<int>> positionsByPrefix)
    {
        if (position > source.Length - 3) return (-1, 0);
        int key = PrefixKey(source, position);
        if (!positionsByPrefix.TryGetValue(key, out var candidates)) return (-1, 0);

        int oldestAllowed = position - RingSize;
        while (candidates.First is { } first && first.Value < oldestAllowed)
            candidates.RemoveFirst();

        int bestCandidate = -1, bestLength = 0, inspected = 0;
        for (LinkedListNode<int>? node = candidates.Last;
             node is not null && inspected < CandidateLimit;
             node = node.Previous, inspected++)
        {
            int candidate = node.Value;
            int distance = position - candidate;
            if (distance <= 0 || distance > RingSize) continue;

            int limit = Math.Min(MaxMatchLength, source.Length - position);
            int length = 0;
            while (length < limit &&
                   source[position + length] == source[candidate + (length % distance)])
                length++;

            if (length > bestLength)
            {
                bestCandidate = candidate;
                bestLength = length;
                if (length == MaxMatchLength) break;
            }
        }
        return (bestCandidate, bestLength);
    }

    private static void AddPositions(
        ReadOnlySpan<byte> source,
        int start,
        int count,
        Dictionary<int, LinkedList<int>> positionsByPrefix)
    {
        int end = Math.Min(start + count, source.Length - 2);
        for (int position = start; position < end; position++)
        {
            int key = PrefixKey(source, position);
            if (!positionsByPrefix.TryGetValue(key, out var positions))
            {
                positions = new LinkedList<int>();
                positionsByPrefix.Add(key, positions);
            }
            positions.AddLast(position);
        }
    }

    private static int PrefixKey(ReadOnlySpan<byte> source, int position)
        => source[position] | source[position + 1] << 8 | source[position + 2] << 16;
}
