using System.Net.NetworkInformation;
using Kxc.Core.Platform;

namespace Kxc.Platform.Network;

/// <summary>Legge la tabella delle connessioni TCP del sistema: nessun traffico di rete.</summary>
public sealed class TcpConnectionProbe : ITcpConnectionProbe
{
    public IReadOnlySet<string> EstablishedEndpoints(int remotePort) =>
        IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpConnections()
            .Where(c => c.State == TcpState.Established && c.RemoteEndPoint.Port == remotePort)
            .Select(c => c.RemoteEndPoint.ToString())
            .ToHashSet(StringComparer.Ordinal);
}
