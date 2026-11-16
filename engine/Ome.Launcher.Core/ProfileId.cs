namespace Ome.Launcher.Core;

/// <summary>A profile's id (launcher spec §4.1): the name of its folder, lowercase letters, digits and <c>-</c>. It
/// never changes with the profile's name.</summary>
public static class ProfileId
{
    public const int MaxLength = 64;

    public static bool IsValid(string? id)
        => id is { Length: > 0 and <= MaxLength } && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
           && id[0] != '-' && id[^1] != '-';
}
