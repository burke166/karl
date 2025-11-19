using Karl.Models;

namespace Karl;

public interface ITemplateRenderer
{
    Task<TemplateRenderResult> RenderAsync(string template, object? model = null, CancellationToken cancellationToken = default);
}
