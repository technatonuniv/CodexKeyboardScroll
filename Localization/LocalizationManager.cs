using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CodexKeyboardScroll
{
    internal sealed class LocalizationManager
    {
        internal const string SystemLanguageCode = "system";
        private const string ResourcePrefix = "CodexKeyboardScroll.Localization.Resources.";
        private const string ResourceSuffix = ".lang";

        private readonly Assembly assembly;
        private readonly Dictionary<string, Dictionary<UiText, string>> packs;
        private readonly LanguageDefinition[] languages;
        private readonly Dictionary<UiText, string> english;
        private Dictionary<UiText, string> current;
        private CultureInfo formatCulture;

        internal LocalizationManager(string requestedLanguageCode)
        {
            assembly = Assembly.GetExecutingAssembly();
            packs = LoadPacks();
            Dictionary<UiText, string> englishPack;
            if (!packs.TryGetValue("en", out englishPack))
            {
                throw new InvalidOperationException("The English language pack is missing.");
            }
            english = englishPack;

            languages = packs
                .Select(pair => new LanguageDefinition(
                    pair.Key,
                    GetValue(pair.Value, UiText.LanguageName, pair.Key)))
                .OrderBy(language => language.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            SetLanguage(requestedLanguageCode);
        }

        internal string RequestedLanguageCode { get; private set; }
        internal string EffectiveLanguageCode { get; private set; }
        internal bool IsRightToLeft { get { return EffectiveLanguageCode == "ar"; } }
        internal IEnumerable<LanguageDefinition> Languages { get { return languages; } }

        internal void SetLanguage(string requestedLanguageCode)
        {
            string requested = string.IsNullOrWhiteSpace(requestedLanguageCode)
                ? SystemLanguageCode
                : requestedLanguageCode.Trim();
            string effective = requested.Equals(SystemLanguageCode, StringComparison.OrdinalIgnoreCase)
                ? ResolveLanguageCode(CultureInfo.CurrentUICulture.Name)
                : ResolveLanguageCode(requested);

            RequestedLanguageCode = requested.Equals(SystemLanguageCode, StringComparison.OrdinalIgnoreCase)
                ? SystemLanguageCode
                : effective;
            EffectiveLanguageCode = effective;
            current = packs[effective];
            try { formatCulture = CultureInfo.GetCultureInfo(effective); }
            catch (CultureNotFoundException) { formatCulture = CultureInfo.InvariantCulture; }
        }

        internal string Text(UiText key)
        {
            string value;
            return current.TryGetValue(key, out value) ? value : english[key];
        }

        internal string Format(UiText key, params object[] arguments)
        {
            return string.Format(formatCulture, Text(key), arguments);
        }

        internal string LanguageName(string code)
        {
            Dictionary<UiText, string> pack;
            return packs.TryGetValue(code, out pack)
                ? GetValue(pack, UiText.LanguageName, code)
                : code;
        }

        internal string SystemLanguageName
        {
            get { return LanguageName(ResolveLanguageCode(CultureInfo.CurrentUICulture.Name)); }
        }

        internal bool ValidateAll(out string error)
        {
            foreach (KeyValuePair<string, Dictionary<UiText, string>> pack in packs)
            {
                foreach (UiText key in Enum.GetValues(typeof(UiText)))
                {
                    if (!pack.Value.ContainsKey(key))
                    {
                        error = pack.Key + " is missing " + key + ".";
                        return false;
                    }
                    if (!HaveMatchingPlaceholders(english[key], pack.Value[key]))
                    {
                        error = pack.Key + " has mismatched placeholders for " + key + ".";
                        return false;
                    }
                    if (!IsValidFormatString(pack.Value[key]))
                    {
                        error = pack.Key + " has an invalid format string for " + key + ".";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        private Dictionary<string, Dictionary<UiText, string>> LoadPacks()
        {
            var result = new Dictionary<string, Dictionary<UiText, string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string resourceName in assembly.GetManifestResourceNames())
            {
                if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                    || !resourceName.EndsWith(ResourceSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                string code = resourceName.Substring(
                    ResourcePrefix.Length,
                    resourceName.Length - ResourcePrefix.Length - ResourceSuffix.Length);
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                using (var reader = new StreamReader(stream))
                {
                    result.Add(code, ParsePack(reader));
                }
            }
            return result;
        }

        private static Dictionary<UiText, string> ParsePack(TextReader reader)
        {
            var result = new Dictionary<UiText, string>();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                UiText key;
                if (separator <= 0
                    || !Enum.TryParse(line.Substring(0, separator).Trim(), false, out key))
                {
                    throw new FormatException("Invalid language resource line: " + line);
                }
                if (result.ContainsKey(key))
                {
                    throw new FormatException("Duplicate language resource key: " + key);
                }
                result.Add(key, Unescape(line.Substring(separator + 1).Trim()));
            }
            return result;
        }

        private string ResolveLanguageCode(string cultureName)
        {
            Dictionary<UiText, string> ignored;
            if (packs.TryGetValue(cultureName, out ignored))
            {
                return packs.Keys.First(key => key.Equals(cultureName, StringComparison.OrdinalIgnoreCase));
            }

            string normalized = (cultureName ?? string.Empty).Replace('_', '-');
            if (normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return normalized.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0
                    || normalized.EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
                    || normalized.EndsWith("-HK", StringComparison.OrdinalIgnoreCase)
                    || normalized.EndsWith("-MO", StringComparison.OrdinalIgnoreCase)
                        ? "zh-Hant"
                        : "zh-Hans";
            }
            if (normalized.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
            {
                return "pt-BR";
            }

            string neutral = normalized.Split('-')[0];
            return packs.TryGetValue(neutral, out ignored) ? neutral : "en";
        }

        private static string GetValue(
            IDictionary<UiText, string> pack,
            UiText key,
            string fallback)
        {
            string value;
            return pack.TryGetValue(key, out value) ? value : fallback;
        }

        private static string Unescape(string value)
        {
            return value.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
        }

        private static bool HaveMatchingPlaceholders(string left, string right)
        {
            return PlaceholderSet(left).SetEquals(PlaceholderSet(right));
        }

        private static HashSet<int> PlaceholderSet(string value)
        {
            var result = new HashSet<int>();
            for (int index = 0; index < value.Length - 2; index++)
            {
                if (value[index] == '{' && char.IsDigit(value[index + 1]) && value[index + 2] == '}')
                {
                    result.Add(value[index + 1] - '0');
                }
            }
            return result;
        }

        private static bool IsValidFormatString(string value)
        {
            try
            {
                string.Format(CultureInfo.InvariantCulture, value, new object[10]);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    internal sealed class LanguageDefinition
    {
        internal LanguageDefinition(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }

        internal string Code { get; private set; }
        internal string DisplayName { get; private set; }
    }
}
