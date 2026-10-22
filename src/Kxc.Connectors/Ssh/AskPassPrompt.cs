using System.Text.RegularExpressions;
using Kxc.Core.Connections;

namespace Kxc.Connectors.Ssh;

internal enum AskPassPromptKind
{
    /// <summary>"utente@host's password:" o "Password:" (keyboard-interactive).</summary>
    Password,

    /// <summary>"The authenticity of host ... (yes/no/[fingerprint])?"</summary>
    HostKey,

    /// <summary>Tutto il resto (passphrase, "Please type 'yes', 'no'..."): sempre rifiutato.</summary>
    Other,
}

/// <summary>
/// Riconosce le domande di ssh. Fase 0: la domanda sulla host key passa dallo stesso helper e una
/// risposta sbagliata fa ripetere ssh all'infinito, quindi solo password e host key hanno risposta.
/// </summary>
internal static partial class AskPassPrompt
{
    public static AskPassPromptKind Classify(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        if (prompt.Contains("authenticity of host", StringComparison.OrdinalIgnoreCase)
            && prompt.Contains("(yes/no", StringComparison.OrdinalIgnoreCase))
        {
            return AskPassPromptKind.HostKey;
        }

        if (!prompt.Contains("passphrase", StringComparison.OrdinalIgnoreCase) && PasswordRegex().IsMatch(prompt))
        {
            return AskPassPromptKind.Password;
        }

        return AskPassPromptKind.Other;
    }

    public static HostKeyQuestion ParseHostKey(string prompt, string fallbackHost)
    {
        var host = HostRegex().Match(prompt);
        var key = FingerprintRegex().Match(prompt);
        return new HostKeyQuestion(
            host.Success ? host.Groups[1].Value : fallbackHost,
            key.Success ? key.Groups[1].Value : null,
            key.Success ? key.Groups[2].Value : null,
            prompt);
    }

    [GeneratedRegex(@"password[^:\n]*:\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PasswordRegex();

    [GeneratedRegex(@"authenticity of host '([^']+)'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostRegex();

    [GeneratedRegex(@"([A-Za-z0-9-]+) key fingerprint is (SHA256:[A-Za-z0-9+/=]+)", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintRegex();
}
