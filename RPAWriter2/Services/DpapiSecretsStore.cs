using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Stores the API key encrypted with Windows DPAPI (current-user scope). The ciphertext lives
    /// in <c>%AppData%\RPAWriter2\&lt;version&gt;\secrets.dat</c>, never in the settings JSON and
    /// never duplicated per workspace.
    /// </summary>
    public sealed class DpapiSecretsStore : ISecretsStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RPAWriter2.ApiKey");

        private static string SecretsFile => AppDataPaths.Resolve("secrets.dat");

        public string LoadApiKey()
        {
            try
            {
                if (!File.Exists(SecretsFile))
                    return "";

                var cipher = File.ReadAllBytes(SecretsFile);
                var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return "";
            }
        }

        public void SaveApiKey(string apiKey)
        {
            try
            {
                if (string.IsNullOrEmpty(apiKey))
                {
                    ClearApiKey();
                    return;
                }

                var plain = Encoding.UTF8.GetBytes(apiKey);
                var cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

                var directory = Path.GetDirectoryName(SecretsFile);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllBytes(SecretsFile, cipher);
            }
            catch
            {
                // ignore write errors — the key stays in memory for this session only
            }
        }

        public void ClearApiKey()
        {
            try
            {
                if (File.Exists(SecretsFile))
                    File.Delete(SecretsFile);
            }
            catch
            {
                // ignore
            }
        }
    }
}
