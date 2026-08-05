using Karl.Models;

namespace Karl.Test;

public class EmailAttachmentTests
{
    [Fact]
    public async Task FromFile_DefaultsFileNameFromPath()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var filePath = Path.Combine(tempDirectory.Path, "invoice.pdf");
        await File.WriteAllTextAsync(filePath, "content");

        var attachment = EmailAttachment.FromFile(filePath);

        Assert.Equal("invoice.pdf", attachment.FileName);
    }

    [Fact]
    public void FromFile_ExplicitFileName_OverridesPathDerivedName()
    {
        var attachment = EmailAttachment.FromFile(@"C:\some\invoice.pdf", fileName: "custom.pdf");

        Assert.Equal("custom.pdf", attachment.FileName);
    }

    [Theory]
    [InlineData("invoice.pdf", "application/pdf")]
    [InlineData("notes.txt", "text/plain")]
    [InlineData("data.csv", "text/csv")]
    [InlineData("data.json", "application/json")]
    [InlineData("data.xml", "application/xml")]
    [InlineData("page.html", "text/html")]
    [InlineData("page.htm", "text/html")]
    [InlineData("photo.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("photo.gif", "image/gif")]
    [InlineData("archive.zip", "application/zip")]
    [InlineData("doc.doc", "application/msword")]
    [InlineData("doc.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("sheet.xls", "application/vnd.ms-excel")]
    [InlineData("sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("slides.ppt", "application/vnd.ms-powerpoint")]
    [InlineData("slides.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("doc.odt", "application/vnd.oasis.opendocument.text")]
    [InlineData("sheet.ods", "application/vnd.oasis.opendocument.spreadsheet")]
    [InlineData("slides.odp", "application/vnd.oasis.opendocument.presentation")]
    public void FromFile_InfersContentTypeFromExtension(string fileName, string expectedContentType)
    {
        var attachment = EmailAttachment.FromFile(fileName);

        Assert.Equal(expectedContentType, attachment.ContentType);
    }

    [Fact]
    public void FromFile_UnknownExtension_FallsBackToOctetStream()
    {
        var attachment = EmailAttachment.FromFile("mystery.xyz");

        Assert.Equal("application/octet-stream", attachment.ContentType);
    }

    [Fact]
    public void FromFile_ExplicitContentType_OverridesInferredValue()
    {
        var attachment = EmailAttachment.FromFile("invoice.pdf", contentType: "application/x-custom");

        Assert.Equal("application/x-custom", attachment.ContentType);
    }

    [Fact]
    public async Task FromFile_OpenReadAsync_CanBeCalledMultipleTimes_ReturnsFreshStreamEachTime()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var filePath = Path.Combine(tempDirectory.Path, "invoice.pdf");
        await File.WriteAllTextAsync(filePath, "first-read second-read");

        var attachment = EmailAttachment.FromFile(filePath);

        await using (var first = await attachment.Source.OpenReadAsync())
        {
            Assert.Equal(0, first.Position);
        }

        await using (var second = await attachment.Source.OpenReadAsync())
        {
            Assert.Equal(0, second.Position);
        }
    }

    [Fact]
    public async Task FromFile_Source_OwnsStream()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var filePath = Path.Combine(tempDirectory.Path, "invoice.pdf");
        await File.WriteAllTextAsync(filePath, "content");

        var attachment = EmailAttachment.FromFile(filePath);

        Assert.True(attachment.Source.OwnsStream);
    }

    [Fact]
    public void FromStream_RequiresExplicitFileName()
    {
        using var stream = new MemoryStream();

        Assert.Throws<ArgumentException>(() => EmailAttachment.FromStream(stream, string.Empty));
    }

    [Fact]
    public void FromStream_RequiresNonNullStream()
    {
        Assert.Throws<ArgumentNullException>(() => EmailAttachment.FromStream(null!, "file.txt"));
    }

    [Fact]
    public void FromStream_Source_DoesNotOwnStream()
    {
        using var stream = new MemoryStream();

        var attachment = EmailAttachment.FromStream(stream, "file.txt");

        Assert.False(attachment.Source.OwnsStream);
    }

    [Fact]
    public async Task FromStream_OpenReadAsync_SecondCall_ThrowsInvalidOperationException()
    {
        using var stream = new MemoryStream();
        var attachment = EmailAttachment.FromStream(stream, "file.txt");

        await attachment.Source.OpenReadAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => attachment.Source.OpenReadAsync());
    }

    [Fact]
    public void FromStream_InfersContentTypeFromFileName()
    {
        using var stream = new MemoryStream();

        var attachment = EmailAttachment.FromStream(stream, "report.pdf");

        Assert.Equal("application/pdf", attachment.ContentType);
    }
}
