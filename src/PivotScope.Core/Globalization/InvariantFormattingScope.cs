using System.Globalization;

namespace PivotScope.Core.Globalization;

/// <summary>
/// Sur un Excel français, les API COM qui prennent des chaînes de formule
/// attendent le format anglais : « 1.5 », pas « 1,5 ». L'add-in d'origine
/// dispersait cette bascule dans toute son interface ; on la confine ici, et on
/// ne l'applique qu'à la frontière COM.
/// </summary>
public static class InvariantFormattingScope
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Bascule la culture du thread courant, restaurée au Dispose.</summary>
    public static IDisposable Enter() => new Scope();

    private sealed class Scope : IDisposable
    {
        private readonly CultureInfo _previous;

        public Scope()
        {
            _previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = EnUs;
        }

        public void Dispose() => Thread.CurrentThread.CurrentCulture = _previous;
    }
}
