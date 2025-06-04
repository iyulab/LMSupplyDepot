namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for IServiceCollection
/// </summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Removes all registrations of service type T from the collection
    /// </summary>
    public static IServiceCollection RemoveAll<T>(this IServiceCollection services)
    {
        var serviceType = typeof(T);
        var descriptors = services.Where(d => d.ServiceType == serviceType).ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }

        return services;
    }
}