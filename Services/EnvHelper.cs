using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DuAnCode.Web.Services
{
    public static class EnvHelper
    {
        private static string GetEnvPath() => Path.Combine(Directory.GetCurrentDirectory(), ".env");

        public static (string Email, string Password) GetSmtpConfig()
        {
            var path = GetEnvPath();
            string email = "", pass = "";
            if (File.Exists(path))
            {
                var lines = File.ReadAllLines(path);
                foreach (var line in lines)
                {
                    if (line.StartsWith("SMTP_EMAIL=")) email = line.Substring("SMTP_EMAIL=".Length).Trim();
                    if (line.StartsWith("SMTP_PASSWORD=")) pass = line.Substring("SMTP_PASSWORD=".Length).Trim();
                }
            }
            return (email, pass);
        }

        public static void SetSmtpConfig(string email, string password)
        {
            var path = GetEnvPath();
            var dict = new Dictionary<string, string>();
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var idx = line.IndexOf('=');
                    if (idx > 0) 
                    {
                        dict[line.Substring(0, idx).Trim()] = line.Substring(idx + 1).Trim();
                    }
                }
            }
            dict["SMTP_EMAIL"] = email ?? "";
            dict["SMTP_PASSWORD"] = password ?? "";

            var newLines = dict.Select(kv => $"{kv.Key}={kv.Value}");
            File.WriteAllLines(path, newLines);
        }
    }
}
