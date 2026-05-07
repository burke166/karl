using Karl.Template.Scriban;

namespace Karl.Test;

public class ScribanTemplateRendererTests
{
    [Fact]
    public async Task RenderAsync_WithModel_SubstitutesTemplateValuesInHtmlAndText()
    {
        var sut = new ScribanTemplateRenderer();
        const string template = "Hello **{{ Name }}** from {{ Company }}!";
        var model = new
        {
            Name = "Karl",
            Company = "Acme"
        };

        var result = await sut.RenderAsync(template, model);

        Assert.Contains("Hello", result.Html);
        Assert.Contains("Karl", result.Html);
        Assert.Contains("Acme", result.Html);
        Assert.Contains("Hello Karl from Acme!", result.Text);
    }
}
