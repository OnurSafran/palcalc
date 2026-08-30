using PalCalc.UI.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PalCalc.UI.Tests;

[TestClass]
public class LocalizationContractTests
{
    private static readonly Regex PlaceholderPattern = new(@"\{([^}]+)\}", RegexOptions.Compiled);

    // `LocalizationCodes.resx` stores each code's '|'-separated format-argument
    // names, not display text. A code whose declared arguments disagree with its
    // text either throws on Bind (declared but not supplied) or renders a raw
    // "{placeholder}" (supplied but not declared), so the two must stay in sync.
    [TestMethod]
    public void EveryLocalizationCodeDeclaresExactlyTheArgumentsItsTextUses()
    {
        var failures = new List<string>();

        foreach (var code in Enum.GetValues<LocalizationCodes>())
        {
            var declared = Translator.Translations[code].Parameters
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            var used = PlaceholderPattern
                .Matches(Translator.Localizations[TranslationLocale.en][code])
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            if (!declared.SequenceEqual(used, StringComparer.Ordinal))
            {
                failures.Add(
                    $"{code}: declares [{string.Join(", ", declared)}] but its text uses [{string.Join(", ", used)}]");
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    // Every no-argument code must be bindable without arguments; this is the call
    // shape used by XAML and by the `Localized(code)` helpers.
    [TestMethod]
    public void EveryArgumentFreeCodeBindsWithoutThrowing()
    {
        var failures = new List<string>();

        foreach (var code in Enum.GetValues<LocalizationCodes>())
        {
            if (Translator.Translations[code].Parameters.Count != 0)
                continue;

            try
            {
                Assert.IsFalse(string.IsNullOrEmpty(code.Bind().Value), $"{code} resolved to empty text.");
            }
            catch (Exception ex)
            {
                failures.Add($"{code}: {ex.Message}");
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }
}
