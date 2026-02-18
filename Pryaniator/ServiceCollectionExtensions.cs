using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Pryaniator;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPryaniator(params Assembly[] assemblies)
        {

            return services
                .AddSingleton(MediatorBuilder.CreateDictionary(assemblies))
                .AddScoped<IMediator, Mediator>();
        }
    }
}
