using System.Net;

namespace M365.SecurityAssessment.Api;

public static class SyntheticPreviewPolicy
{
    public static void ValidateConfiguration(string environmentName, bool enabled)
    {
        if (enabled && !IsDevelopment(environmentName))
        {
            throw new InvalidOperationException("Synthetic preview can only be enabled in Development.");
        }
    }

    public static bool IsEnabled(string environmentName, bool enabled) => enabled && IsDevelopment(environmentName);

    public static bool IsLoopback(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        return IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
    }

    private static bool IsDevelopment(string environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
}