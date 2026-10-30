namespace PCL2Swap.Api
{
    public enum SwapLanguage
    {
        /// <summary>Follow the Windows UI language (Chinese systems stay Chinese).</summary>
        Auto,
        Chinese,
        English
    }

    /// <summary>
    /// Bilingual surface shared by the swap UI and addons. PCL2 has no localisation resources of
    /// its own, so English is delivered as a runtime translation layer keyed by the Chinese
    /// original text. <see cref="T(string)"/> returns the mapped English string when the active
    /// language is English and the input unchanged otherwise, which keeps call sites readable.
    /// </summary>
    public interface II18n
    {
        SwapLanguage Language { get; }

        /// <summary>"zh-CN" or "en-US" - the language actually applied right now.</summary>
        string CurrentLanguage { get; }

        bool IsEnglish { get; }

        /// <summary>Translates a Chinese original; unknown strings pass through untouched.</summary>
        string T(string chinese);

        /// <summary>Explicit two-language form, independent of the dictionary.</summary>
        string T(string chinese, string english);

        /// <summary>Extends the dictionary (used by addons that ship their own pages).</summary>
        void Register(string chinese, string english);

        /// <summary>Switches the language, persists it and re-applies the host translation.</summary>
        void SetLanguage(SwapLanguage language);
    }
}
