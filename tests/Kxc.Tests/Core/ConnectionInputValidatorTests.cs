using Kxc.Core.Entries;

namespace Kxc.Tests.Core;

public class ConnectionInputValidatorTests
{
    [Theory]
    [InlineData("srv-file.rossi.example", true)]
    [InlineData("localhost", true)]
    [InlineData("10.20.0.15", true)]
    [InlineData("fe80::1", true)]
    [InlineData("10", false)]
    [InlineData("1.2", false)]
    [InlineData("-oProxyCommand=calc", false)]
    [InlineData("srv .example", false)]
    [InlineData("srv;calc", false)]
    [InlineData("srv\u0007", false)]
    [InlineData("-host", false)]
    [InlineData("", false)]
    public void Validates_host_names_and_addresses(string host, bool valid)
    {
        Assert.Equal(valid, ConnectionInputValidator.IsValidHost(host));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    public void Port_is_between_1_and_65535(int port, bool valid)
    {
        Assert.Equal(valid, ConnectionInputValidator.IsValidPort(port));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("admin.rossi", true)]
    [InlineData("ROSSI\\admin", true)]
    [InlineData("admin@rossi.example", true)]
    [InlineData("-l", false)]
    [InlineData("admin\r\nfoo", false)]
    public void Validates_user_names(string user, bool valid)
    {
        Assert.Equal(valid, ConnectionInputValidator.IsValidUserName(user));
    }

    [Theory]
    [InlineData("ROSSI", true)]
    [InlineData("rossi.local", true)]
    [InlineData("-x", false)]
    [InlineData("ROSSI\nX", false)]
    public void Validates_domains(string domain, bool valid)
    {
        Assert.Equal(valid, ConnectionInputValidator.IsValidDomain(domain));
    }
}
