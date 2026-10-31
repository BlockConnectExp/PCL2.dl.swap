using System;
using System.Collections.Generic;

namespace PCL2Swap.Api
{
    /// <summary>
    /// Module-owned localization pack. Values load from <c>i18n/en-US.json</c> and
    /// <c>i18n/zh-CN.json</c> inside the addon package; registrations stay scoped to this
    /// instance and disappear with the module instead of leaking into the host dictionary.
    /// </summary>
    public interface IAddonLocalization
    {
        /// <summary>Raised after <see cref="Reload"/> changes the active dictionary.</summary>
        event EventHandler Changed;

        string CurrentLocale { get; }
        bool IsEnglish { get; }
        IReadOnlyList<string> AvailableLocales { get; }
        int Count { get; }

        /// <summary>
        /// Resolves the current-language value. When English has no package or registered entry,
        /// <paramref name="english"/> is used; if that is omitted, the host dictionary is consulted.
        /// </summary>
        string T(string chinese, string english = null);

        /// <summary>Formats the current-language template after resolving both language variants.</summary>
        string Format(string chineseFormat, string englishFormat, params object[] arguments);

        bool TryTranslate(string chinese, out string translated);
        void Register(string chinese, string english);
        int RegisterMany(IReadOnlyDictionary<string, string> entries);

        /// <summary>Re-reads this addon's language pack from disk.</summary>
        bool Reload();
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
