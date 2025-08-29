using System;
using System.Security.Cryptography.X509Certificates;

namespace Core;

public static class SignerInfo
{
    public static string? TryGetPublisher(string? filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;
            using var cert = new X509Certificate2(filePath);
            var subject = cert.GetNameInfo(X509NameType.SimpleName, false);
            return string.IsNullOrWhiteSpace(subject) ? null : subject;
        }
        catch
        {
            return null;
        }
    }
}


