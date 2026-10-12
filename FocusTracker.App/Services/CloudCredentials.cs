using System;
using System.Security.Cryptography;
using System.Text;

namespace FocusTracker.App.Services
{
    // Рядок підключення до хмарної бази зберігається зашифрованим DPAPI:
    // розшифрувати його може лише цей користувач Windows на цьому комп'ютері
    public static class CloudCredentials
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FocusTracker.CloudConnection");

        public static string Protect(string connectionString) =>
            Convert.ToBase64String(ProtectedData.Protect(
                Encoding.UTF8.GetBytes(connectionString), Entropy, DataProtectionScope.CurrentUser));

        public static string? Unprotect(string? protectedValue)
        {
            if (string.IsNullOrEmpty(protectedValue)) return null;
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                    Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser));
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                return null;
            }
        }
    }
}
