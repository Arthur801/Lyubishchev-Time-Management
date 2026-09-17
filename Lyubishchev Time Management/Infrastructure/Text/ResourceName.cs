namespace Lyubishchev_Time_Management.Infrastructure.Text;

public static class ResourceName
{
    public static bool TryNormalize(string? rawName, out string name, out string normalizedName)
    {
        name = rawName?.Trim() ?? string.Empty;
        normalizedName = name.ToUpperInvariant();
        return name.Length is > 0 and <= 100;
    }
}
