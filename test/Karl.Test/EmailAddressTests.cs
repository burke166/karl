using Karl.Models;

namespace Karl.Test;

public class EmailAddressTests
{
    [Fact]
    public void ToString_WithoutName_ReturnsAddressOnly()
    {
        var address = new EmailAddress("dev@example.com");

        Assert.Equal("dev@example.com", address.ToString());
    }

    [Fact]
    public void ToString_WithName_ReturnsMailboxFormat()
    {
        var address = new EmailAddress("dev@example.com", "Dev User");

        Assert.Equal("Dev User <dev@example.com>", address.ToString());
    }

    [Fact]
    public void Constructor_WithNullAddress_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new EmailAddress(null!));
    }
}
