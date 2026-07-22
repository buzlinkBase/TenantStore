using Microsoft.Extensions.DependencyInjection;
namespace Onepunch.Common.Lib;

[AttributeUsage(AttributeTargets.Class)]
public class ServiceRegistrationAttribute : Attribute
{
    public ServiceLifetime Lifetime { get; }
    public bool Exclude { get; }
    public ServiceRegistrationAttribute(ServiceLifetime lifetime = ServiceLifetime.Scoped, bool exclude = false)
    {
        Lifetime = lifetime;
        Exclude = exclude;
    }
}
