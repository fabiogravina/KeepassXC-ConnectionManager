using System.Text;
using Kxc.Core.Platform;
using Kxc.Platform.Credentials;

namespace Kxc.Tests.Platform;

/// <summary>
/// Gestore credenziali vero di Windows, con destinazioni fittizie (.invalid) e persistenza di
/// sessione: niente resta su disco e i test ripuliscono comunque.
/// </summary>
public sealed class WindowsCredentialStoreTests : IDisposable
{
    private readonly string _target = "TERMSRV/kxc-test-" + Guid.NewGuid().ToString("N") + ".invalid";

    [Fact]
    public void S7_credentials_written_by_the_app_are_recognized_and_cleaned_up()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Gestore credenziali: solo Windows");
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new WindowsCredentialStore();
        Assert.Equal(CredentialOwner.None, store.Find(_target));

        store.WriteSessionCredential(_target, "prova", Encoding.UTF8.GetBytes("password-inventata"));
        Assert.Equal(CredentialOwner.ThisApp, store.Find(_target));

        Assert.True(store.RemoveLeftovers() >= 1);
        Assert.Equal(CredentialOwner.None, store.Find(_target));
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            new WindowsCredentialStore().Delete(_target);
        }
    }
}
