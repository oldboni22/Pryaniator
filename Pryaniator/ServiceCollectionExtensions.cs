using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Pryaniator;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPryaniator(params Assembly[] assemblies)
        {
            Mediator.Handlers = MediatorBuilder.CreateDictionary(assemblies);

            return services
                .AddScoped<IMediator>(sp => new Mediator(sp));
        }
    }
}
