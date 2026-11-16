# Translations

Clamshield speaks English and Bahasa Indonesia today. This page explains how the texts are stored, how to fix or improve a translation, and what adding a third language would take.

## How it works

There are two kinds of text, and both are kept in `src/Clamshield/Services/LocStrings.cs`.

**Text in the interface (XAML)** uses a short key. In `MainWindow.axaml` you will see:

```xml
<TextBlock Text="{Binding [real_time_protection], Source={x:Static loc:Loc.I}}" />
```

`LocStrings.ByKey` maps that key to a pair of the English and the Indonesian text. The key is the English text in lowercase with everything that is not a letter or a digit turned into `_`, cut to 56 characters.

**Text in code** goes through `Loc.T("English text")` for plain texts and `Loc.F("{0} files", count)` for texts with numbers. `LocStrings.ByEn` maps the English text to the translation.

When the language changes, the interface updates at once, because XAML binds to the translator.

## Fixing a translation

Find the text in `LocStrings.cs` and change the Indonesian side. Keep the placeholders (`{0}`, `{1}`, `{0:N0}`) exactly as they are, in a place where they still make sense for the sentence.

If the text appears in both dictionaries, change both. A search for the English text finds them.

## Adding a new text

1. In XAML, write the binding with a key, as above.
2. Add the entry to `ByKey` with the key, the English text and the translation.
3. For a text used from code, add it to `ByEn`.
4. Run `dotnet test`. The two translation tests tell you if a key or an Indonesian text is missing.

Singular and plural forms are separate texts. Look at `Plural(...)` in `MainViewModel.Features.cs` and the texts `"1 file"` and `"{0:N0} files"` for the pattern.

## Writing style

Write the way a helpful person would say it. Keep sentences short and name the thing the user is looking at. Avoid marketing words and avoid exclamation marks. The Indonesian texts use "kamu", not "Anda".

## Adding another language

This is possible but needs a small change in the code first. `Loc` is built around two languages:

- `Loc.IsId` is a boolean, and `LocStrings` stores `(en, id)` pairs.
- Settings stores the language as `"en"` or `"id"`, and the Settings page has a two-item list.

To add a language, the dictionary pairs should become one dictionary per language, `Loc` should pick one by code, and the language list in Settings should be built from what is available. That is a contained change and a welcome one. Open an issue before starting so we can agree on the shape, then the translation itself is a copy of the English column with the right text on the other side.

Date and number formats follow the language through .NET's culture support, for example `id-ID`.
