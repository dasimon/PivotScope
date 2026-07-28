using System.Globalization;
using PivotScope.Core.Globalization;

namespace PivotScope.Core.Tests;

public class InvariantFormattingScopeTests
{
    [Fact]
    public void Enter_SwitchesToEnUs_AndRestoresPreviousCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            using (InvariantFormattingScope.Enter())
            {
                Assert.Equal("en-US", Thread.CurrentThread.CurrentCulture.Name);
                // Le point de la discorde : en fr-FR, 1.5 se formate « 1,5 » et
                // les API COM d'Excel refusent la chaîne.
                Assert.Equal("1.5", 1.5d.ToString(Thread.CurrentThread.CurrentCulture));
            }

            Assert.Equal("fr-FR", Thread.CurrentThread.CurrentCulture.Name);
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
    }

    [Fact]
    public void Enter_RestoresCulture_EvenWhenBodyThrows()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            Action failingComCall = () =>
            {
                using (InvariantFormattingScope.Enter())
                {
                    throw new InvalidOperationException("échec COM simulé");
                }
            };

            Assert.Throws<InvalidOperationException>(failingComCall);

            Assert.Equal("fr-FR", Thread.CurrentThread.CurrentCulture.Name);
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
    }

    [Fact]
    public void Enter_IsReentrant_AndRestoresInOrder()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            using (InvariantFormattingScope.Enter())
            {
                using (InvariantFormattingScope.Enter())
                {
                    Assert.Equal("en-US", Thread.CurrentThread.CurrentCulture.Name);
                }
                Assert.Equal("en-US", Thread.CurrentThread.CurrentCulture.Name);
            }

            Assert.Equal("fr-FR", Thread.CurrentThread.CurrentCulture.Name);
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
    }
}
