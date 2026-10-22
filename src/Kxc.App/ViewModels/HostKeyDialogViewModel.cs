using Kxc.App.Resources;
using Kxc.App.Services;
using Kxc.Core.Connections;

namespace Kxc.App.ViewModels;

public sealed class HostKeyDialogViewModel : ViewModelBase
{
    public HostKeyDialogViewModel(HostKeyQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        Message = Messages.Format(Strings.HostKey_Message, question.Host);
        FingerprintLabel = question.Fingerprint is null
            ? Strings.HostKey_RawPrompt
            : Messages.Format(Strings.HostKey_Fingerprint, question.KeyType ?? "SSH");
        Fingerprint = question.Fingerprint ?? question.RawPrompt;
    }

    public string Message { get; }

    public string FingerprintLabel { get; }

    public string Fingerprint { get; }
}
