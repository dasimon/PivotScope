using ExcelDna.Integration;
using PivotScope.Core.Globalization;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Unique point de passage vers COM. Excel est STA sur son thread principal
/// tandis que les messages WebView2 arrivent sur le thread UI : tout appel COM
/// fait ailleurs finit en RPC_E_SERVERCALL_RETRYLATER, de façon intermittente et
/// pénible à reproduire. La bascule de culture est appliquée ici, une fois.
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
