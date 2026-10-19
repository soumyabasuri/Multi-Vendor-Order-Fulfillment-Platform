using System.Diagnostics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MultiVendorPlatform.Infrastructure.Observability;

public static class DiagnosticsConfig
{
    public const string ServiceName = "MultiVendorPlatform.Api";
    public static readonly ActivitySource ActivitySource = new(ServiceName);
}

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddAppObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(DiagnosticsConfig.ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(DiagnosticsConfig.ServiceName)
                    .AddAspNetCoreInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                    })
                    .AddHttpClientInstrumentation();

                if (configuration.GetValue<bool>("OpenTelemetry:ConsoleExporter", false))
                {
                    tracing.AddConsoleExporter();
                }
            });

        return services;
    }
}
