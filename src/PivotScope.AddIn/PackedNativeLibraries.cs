using System.Reflection;
using System.Runtime.InteropServices;

namespace PivotScope.AddIn;

/// <summary>
/// Lets SQLite find its native library inside the packed .xll.
///
/// SQLitePCLRaw imports "e_sqlite3", without an extension. Excel-DNA 1.9 looks a packed
/// native library up by the requested name, upper-cased, as is: "E_SQLITE3" never matches
/// the resource "E_SQLITE3.DLL" (AssemblyManager.NativeLibraryResolve). WebView2 asks for
/// "WebView2Loader.dll" and is found. Without this resolver the first SQLite call fails with
/// DllNotFoundException, so no SSAS session opens (each one opens state.db).
/// </summary>
internal static class PackedNativeLibraries
{
    private const string Sqlite = "e_sqlite3";

    public static void Register()
    {
        var provider = typeof(SQLitePCL.SQLite3Provider_e_sqlite3).Assembly;
        NativeLibrary.SetDllImportResolver(provider, Resolve);
    }

    // Asks again with the extension: Excel-DNA then extracts the packed DLL and loads it.
    // Any other name, and a failure, fall through to the default resolution.
    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
        => name == Sqlite && NativeLibrary.TryLoad(Sqlite + ".dll", assembly, searchPath, out var handle)
            ? handle
            : IntPtr.Zero;
}
