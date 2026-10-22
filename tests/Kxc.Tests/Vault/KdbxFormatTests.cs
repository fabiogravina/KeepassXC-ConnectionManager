using Kxc.Tests.TestData;
using Kxc.Vault;

namespace Kxc.Tests.Vault;

public class KdbxFormatTests
{
    [Theory]
    [InlineData(4, KdbxFormatKind.Kdbx4)]
    [InlineData(3, KdbxFormatKind.Kdbx3)]
    [InlineData(2, KdbxFormatKind.Unknown)]
    public void Detects_kdbx_major_version(ushort major, KdbxFormatKind expected)
    {
        var header = new byte[] { 0x03, 0xD9, 0xA2, 0x9A, 0x67, 0xFB, 0x4B, 0xB5, 0x01, 0x00, (byte)major, 0x00 };

        Assert.Equal(expected, KdbxFormat.Detect(header));
    }

    [Fact]
    public void Detects_keepass_1_databases()
    {
        var header = new byte[] { 0x03, 0xD9, 0xA2, 0x9A, 0x65, 0xFB, 0x4B, 0xB5, 0x00, 0x00, 0x03, 0x00 };

        Assert.Equal(KdbxFormatKind.Kdb1, KdbxFormat.Detect(header));
        Assert.False(KdbxFormat.IsSupported(KdbxFormatKind.Kdb1));
    }

    [Fact]
    public void Other_files_are_unknown()
    {
        Assert.Equal(KdbxFormatKind.Unknown, KdbxFormat.Detect("<?xml version="u8));
    }

    [Fact]
    public void Fixture_generated_by_keepassxc_cli_is_kdbx3()
    {
        Assert.Equal(KdbxFormatKind.Kdbx3, KdbxFormat.Detect(Fixture.DatabasePath));
    }

    [Fact]
    public void Kdbx4_fixture_created_in_keepassxc_is_kdbx4()
    {
        Assert.SkipUnless(File.Exists(Fixture.Kdbx4DatabasePath), Fixture.Kdbx4MissingReason);
        Assert.Equal(KdbxFormatKind.Kdbx4, KdbxFormat.Detect(Fixture.Kdbx4DatabasePath));
    }
}
