using System;
using System.IO;

namespace Core;

public static class Whitelist
{
    private static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');

    public static bool IsTrusted(ProcessInfo? info)
    {
        if (info == null) return false;

        // Path-based: anything under Windows directory is considered trusted
        if (!string.IsNullOrWhiteSpace(info.ExecutablePath))
        {
            try
            {
                var full = Path.GetFullPath(info.ExecutablePath);
                if (full.StartsWith(WindowsDir, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch { }
        }

        // Company-based quick trust list
        var company = (info.CompanyName ?? string.Empty).Trim();
        if (company.Length > 0)
        {
            if (company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) return true;
            if (company.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return true;
            if (company.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return true;
            if (company.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase)) return true;
        }

        // Process-name based minimal set
        var name = (info.ProcessName ?? string.Empty).ToLowerInvariant();
        switch (name)
        {
            case "explorer":
            case "shellexperiencehost":
            case "applicationframehost":
            case "audiodg":
            case "svchost":
                return true;
        }

        return false;
    }
}


