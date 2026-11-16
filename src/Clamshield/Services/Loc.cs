using System.ComponentModel;

namespace Clamshield.Services;

/// <summary>Live language switching (English / Bahasa Indonesia). XAML binds to the indexer, code uses T/F.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static readonly Loc I = new();
    public event PropertyChangedEventHandler? PropertyChanged;

    string _lang = "en";
    public string Language
    {
        get => _lang;
        set
        {
            if (_lang == value) return;
            _lang = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }
    public bool IsId => _lang == "id";

    /// <summary>XAML: {Binding [key], Source={x:Static svc:Loc.I}}</summary>
    public string this[string key] =>
        LocStrings.ByKey.TryGetValue(key, out var v) ? (IsId ? v.id : v.en) : key;

    public static string T(string english) =>
        I.IsId && LocStrings.ByEn.TryGetValue(english, out var id) ? id : english;

    public static string F(string english, params object[] args)
    {
        try { return string.Format(T(english), args); } catch { return english; }
    }
}
