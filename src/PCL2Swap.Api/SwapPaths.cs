using System;
using System.IO;
using System.Text;

namespace PCL2Swap.Api
{
    /// <summary>Canonical on-disk locations shared by the host and the addon SDK.</summary>
    public static class SwapPaths
    {
        public static string Root => Path.Combine(Path.GetTempPath(), "PCL2Swap");
        public static string AddonsDirectory => Path.Combine(Root, "addons");
        public static string LogsDirectory => Path.Combine(Root, "logs");
        public static string AddonDirectory(string id) => Path.Combine(AddonsDirectory, Sanitize(id ?? "unnamed"));
        public static string AddonConfigFile(string id) =>
            Path.Combine(AddonDirectory(id), "config.json");

        /// <summary>Keeps an addon id from escaping its directory.</summary>
        public static string Sanitize(string id)
        {
            var builder = new StringBuilder(id.Length);
            foreach (var ch in id)
            {
                var keep = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') ||
                           (ch >= '0' && ch <= '9') || ch == '.' || ch == '-' || ch == '_';
                builder.Append(keep ? ch : '_');
            }
            var result = builder.ToString().Trim('.');
            return string.IsNullOrEmpty(result) ? "unnamed" : result;
        }
    }

    /// <summary>
    /// Minimal semver-range check for manifest "apiVersion" values: empty/"*" always match;
    /// tokens are space separated and support "&gt;=1.0", "&gt;1.0", "&lt;2.0", "&lt;=2.0", "=1.0"
    /// and a bare version (treated as "=").
    /// </summary>
    public static class ApiVersionRange
    {
        public static bool IsSatisfied(string range, Version hostVersion)
        {
            if (hostVersion == null) return true;
            if (string.IsNullOrWhiteSpace(range) || range.Trim() == "*") return true;

            foreach (var rawToken in range.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = rawToken.Trim();
                string op;
                string number;
                if (token.StartsWith(">=", StringComparison.Ordinal)) { op = ">="; number = token.Substring(2); }
                else if (token.StartsWith("<=", StringComparison.Ordinal)) { op = "<="; number = token.Substring(2); }
                else if (token.StartsWith(">", StringComparison.Ordinal)) { op = ">"; number = token.Substring(1); }
                else if (token.StartsWith("<", StringComparison.Ordinal)) { op = "<"; number = token.Substring(1); }
                else if (token.StartsWith("=", StringComparison.Ordinal)) { op = "="; number = token.Substring(1); }
                else { op = "="; number = token; }

                var bound = ParseVersion(number);
                if (bound == null) return false;
                var comparison = Compare(hostVersion, bound);
                switch (op)
                {
                    case ">=": if (comparison < 0) return false; break;
                    case ">": if (comparison <= 0) return false; break;
                    case "<=": if (comparison > 0) return false; break;
                    case "<": if (comparison >= 0) return false; break;
                    default: if (comparison != 0) return false; break;
                }
            }
            return true;
        }

        public static Version ParseVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var parts = text.Trim().Split('.');
            var numbers = new int[4];
            for (var i = 0; i < parts.Length && i < 4; i++)
            {
                if (!int.TryParse(parts[i], out numbers[i])) return null;
            }
            return new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        }

        private static int Compare(Version left, Version right) =>
            left == null || right == null ? 0 : left.CompareTo(right);
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
