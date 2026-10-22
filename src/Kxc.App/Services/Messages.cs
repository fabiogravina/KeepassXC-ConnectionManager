using System.Globalization;
using Kxc.App.Resources;
using Kxc.Core.Connections;
using Kxc.Core.Entries;
using Kxc.Core.Vault;

namespace Kxc.App.Services;

/// <summary>Testi per l'utente: cosa è successo e cosa fare.</summary>
internal static class Messages
{
    public static string For(VaultError error) => error switch
    {
        VaultError.CliNotFound => Strings.Error_CliNotFound,
        VaultError.CliVersionTooOld => Strings.Error_CliVersionTooOld,
        VaultError.DatabaseNotFound => Strings.Error_DatabaseNotFound,
        VaultError.KeyFileNotFound => Strings.Error_KeyFileNotFound,
        VaultError.UnsupportedFormat => Strings.Error_UnsupportedFormat,
        VaultError.InvalidCredentials => Strings.Error_InvalidCredentials,
        VaultError.EntryNotFound => Strings.Error_EntryNotFound,
        VaultError.Locked => Strings.Error_Locked,
        VaultError.Timeout => Strings.Error_Timeout,
        _ => Strings.Error_ReadFailed,
    };

    public static string For(ConnectionException error, string protocolName)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Error switch
        {
            ConnectionError.Vault when error.VaultError is { } vault => For(vault),
            ConnectionError.ClientNotFound => Format(Strings.Error_ClientNotFound, protocolName),
            ConnectionError.NotSupported => Format(Strings.Error_ProtocolNotSupported, protocolName),
            ConnectionError.InvalidEntry => Strings.Warning_InvalidData,
            _ => Format(Strings.Error_LaunchFailed, protocolName),
        };
    }

    public static string? For(EntryWarning warning) => warning switch
    {
        EntryWarning.MissingHost => Strings.Warning_MissingHost,
        EntryWarning.InvalidConnectionData => Strings.Warning_InvalidData,
        EntryWarning.AmbiguousPath => Strings.Warning_AmbiguousPath,
        _ => null,
    };

    public static string Format(string format, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);

    public static string ProtocolName(ConnectionProtocols protocol) => protocol switch
    {
        ConnectionProtocols.Rdp => "RDP",
        ConnectionProtocols.Ssh => "SSH",
        ConnectionProtocols.Sftp => "SFTP",
        _ => string.Empty,
    };
}
