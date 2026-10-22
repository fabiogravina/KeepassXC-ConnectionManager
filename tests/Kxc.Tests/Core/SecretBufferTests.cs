using Kxc.Core.Security;
using static Kxc.Tests.TestData.Fixture;

namespace Kxc.Tests.Core;

public class SecretBufferTests
{
    [Fact]
    public void Encodes_chars_as_utf8()
    {
        using var secret = SecretBuffer.FromChars("è€");

        Assert.True(secret.ContentEquals(Utf8("è€")));
    }

    [Fact]
    public void Trailing_newline_is_added_to_a_copy()
    {
        using var secret = SecretBuffer.FromChars("test");
        using var line = secret.WithTrailingNewline();

        Assert.True(line.ContentEquals(Utf8("test\n")));
        Assert.True(secret.ContentEquals(Utf8("test")));
    }

    [Fact]
    public void Memory_is_zeroed_on_dispose()
    {
        var secret = SecretBuffer.FromChars("segreto");
        var memory = secret.Memory;

        secret.Dispose();

        Assert.All(memory.ToArray(), b => Assert.Equal(0, b));
        Assert.Throws<ObjectDisposedException>(() => secret.Span.Length);
    }

    [Fact]
    public void Credentials_need_a_password_or_a_key_file()
    {
        Assert.Throws<ArgumentException>(() => new UnlockCredentials("db.kdbx", SecretBuffer.FromChars(""), null));

        using var keyOnly = new UnlockCredentials("db.kdbx", SecretBuffer.FromChars(""), "key.keyx");
        Assert.Null(keyOnly.Password);
    }
}
