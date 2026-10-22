using Kxc.Core.Entries;

namespace Kxc.Tests.Core;

public class ConnectionProtocolRulesTests
{
    [Theory]
    [InlineData("rdp", ConnectionProtocols.Rdp)]
    [InlineData("RDP", ConnectionProtocols.Rdp)]
    [InlineData("SSH;Sftp", ConnectionProtocols.Ssh | ConnectionProtocols.Sftp)]
    [InlineData("rdp, ssh", ConnectionProtocols.Rdp | ConnectionProtocols.Ssh)]
    [InlineData("web;cliente", ConnectionProtocols.None)]
    [InlineData("", ConnectionProtocols.None)]
    [InlineData("rdpx", ConnectionProtocols.None)]
    public void Tags_are_case_insensitive_and_split_on_comma_or_semicolon(string tags, ConnectionProtocols expected)
    {
        var result = ConnectionProtocolRules.FromTags(ConnectionProtocolRules.SplitTags(tags));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(ConnectionProtocols.Rdp | ConnectionProtocols.Ssh | ConnectionProtocols.Sftp, ConnectionProtocols.Rdp)]
    [InlineData(ConnectionProtocols.Ssh | ConnectionProtocols.Sftp, ConnectionProtocols.Ssh)]
    [InlineData(ConnectionProtocols.Rdp | ConnectionProtocols.Sftp, ConnectionProtocols.Rdp)]
    [InlineData(ConnectionProtocols.Sftp, ConnectionProtocols.Sftp)]
    [InlineData(ConnectionProtocols.None, ConnectionProtocols.None)]
    public void Default_protocol_follows_rdp_ssh_sftp_priority(ConnectionProtocols available, ConnectionProtocols expected)
    {
        Assert.Equal(expected, ConnectionProtocolRules.Default(available));
    }

    [Fact]
    public void Default_ports_are_3389_and_22()
    {
        Assert.Equal(3389, ConnectionProtocolRules.DefaultPort(ConnectionProtocols.Rdp));
        Assert.Equal(22, ConnectionProtocolRules.DefaultPort(ConnectionProtocols.Ssh));
        Assert.Equal(22, ConnectionProtocolRules.DefaultPort(ConnectionProtocols.Sftp));
    }
}
