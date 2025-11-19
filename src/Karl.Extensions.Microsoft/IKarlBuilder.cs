using Microsoft.Extensions.DependencyInjection;

namespace Karl.Extensions.Microsoft;

public interface IKarlBuilder
{
    IServiceCollection Services { get; }
}

internal class KarlBuilder : IKarlBuilder
{
    public IServiceCollection Services { get; }

    public KarlBuilder(IServiceCollection services)
    {
        Services = services;
    }
}
