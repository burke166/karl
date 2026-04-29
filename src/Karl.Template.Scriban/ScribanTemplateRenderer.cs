using Karl.Models;
using Markdig;
using Scriban;

namespace Karl.Template.Scriban;

public sealed class ScribanTemplateRenderer : ITemplateRenderer
{
    private readonly MarkdownPipeline _markdownPipeline =
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

    public async Task<TemplateRenderResult> RenderAsync(
        string template,
        object? model = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scribanTemplate = global::Scriban.Template.Parse(template);

        if (scribanTemplate.HasErrors)
        {
            var errors = string.Join(Environment.NewLine, scribanTemplate.Messages);
            throw new InvalidOperationException($"Scriban template parse failed:{Environment.NewLine}{errors}");
        }

        var renderedMarkdown = await scribanTemplate.RenderAsync(
            model,
            member => member.Name);

        cancellationToken.ThrowIfCancellationRequested();

        var html = Markdown.ToHtml(renderedMarkdown, _markdownPipeline);
        var text = Markdown.ToPlainText(renderedMarkdown, _markdownPipeline);

        var preMailer = new PreMailer.Net.PreMailer(html);

        var inlineResult = preMailer.MoveCssInline(
            removeStyleElements: false,
            preserveMediaQueries: true);

        return new TemplateRenderResult(inlineResult.Html, text);
    }
}