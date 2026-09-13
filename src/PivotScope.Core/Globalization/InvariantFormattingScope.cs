using System.Globalization;

namespace PivotScope.Core.Globalization;

/// <summary>
/// On a French Excel, the COM APIs that take formula strings expect the
/// English format: "1.5", not "1,5". The original add-in scattered this switch
/// across its whole UI; here it is confined to one place, and applied only at
/// the COM boundary.
/// </summary>
public static class InvariantFormattingScope
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Switches the current thread's culture, restored on Dispose.</summary>
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
