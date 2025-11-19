using Karl.Models;
using Markdig;
using Scriban;

namespace Karl.Templates.Scriban;

public class ScribanTemplateRenderer : ITemplateRenderer
{
    private readonly MarkdownPipeline _markdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public Task<TemplateRenderResult> RenderAsync(string template, object? model = null, CancellationToken cancellationToken = default)
    {
        var scribanTemplate = Template.Parse(template);
        var rendered = scribanTemplate.Render(model, member => member.Name);

        var html = Markdown.ToHtml(rendered, _markdownPipeline);
        var text = Markdown.ToPlainText(rendered, _markdownPipeline);

        var pm = new PreMailer.Net.PreMailer(html);

        var result = pm.MoveCssInline(removeStyleElements: false, preserveMediaQueries: true);
        return Task.FromResult(new TemplateRenderResult(result.Html, text));
    }
}
