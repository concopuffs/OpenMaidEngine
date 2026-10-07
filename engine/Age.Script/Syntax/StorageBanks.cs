namespace Age.Script.Syntax;

/// <summary>Raw storage-bank spellings and their SYS4 operand type tags (spec §6.2).</summary>
public static class StorageBanks
{
    public static readonly IReadOnlyDictionary<string, int> TypeByBank = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["G"] = 0x3, ["GF"] = 0x4, ["GS"] = 0x5, ["GP"] = 0x6, ["GSP"] = 0x8,
        ["L"] = 0x9, ["LF"] = 0xA, ["LS"] = 0xB, ["LP"] = 0xC, ["LFP"] = 0xD, ["LSP"] = 0xE,
    };

    public static readonly IReadOnlyDictionary<int, string> BankByType =
        TypeByBank.ToDictionary(pair => pair.Value, pair => pair.Key);

    /// <summary>Header F0..F5 order: the local bank type tag counted by each field.</summary>
    public static readonly int[] LocalBankTypes = [0x9, 0xA, 0xB, 0xC, 0xD, 0xE];
}
