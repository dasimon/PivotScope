using ExcelDna.Integration;
using PivotScope.Core.Globalization;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Single gateway to COM. Excel is STA on its main thread while WebView2
/// messages arrive on the UI thread: any COM call made elsewhere ends in
/// RPC_E_SERVERCALL_RETRYLATER, intermittently and in a way that is
/// painful to reproduce. The culture switch is applied here, once.
/// </summary>
public static class ExcelThread
{
    public static Task<T> RunAsync<T>(Func<T> comWork)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        ExcelAsyncUtil.QueueAsMacro(() =>
        {
            try
            {
                using (InvariantFormattingScope.Enter())
                {
                    tcs.SetResult(comWork());
                }
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    public static Task RunAsync(Action comWork)
        => RunAsync(() => { comWork(); return true; });
}
