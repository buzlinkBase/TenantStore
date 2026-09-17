using Asp.Versioning.ApiExplorer;
using Mapster;
using MessagePack;
using MessagePack.AspNetCoreMvcFormatter;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Swashbuckle.AspNetCore.SwaggerGen;
using TenantStoreApi;
using TenantStoreApi.Core.Extensions;
using TenantStoreApi.Core.Protos.ServiceHandlers;
using TenantStoreApi.Exceptions;
using TenantStoreApi.Filters;
using TenantStoreApi.Middlewares;

internal class Program
{
    private static void Main(string[] args)
    {
        // AuthUrl/Domains:TenantUrl point at plain http:// addresses in production (bypassing
        // the TLS-terminating Nginx gateway for internal service-to-service gRPC calls) --
        // .NET's SocketsHttpHandler refuses to negotiate HTTP/2 over an unencrypted connection
        // (gRPC requires HTTP/2) unless this switch is set before any HttpClient/gRPC channel is
        // built, so it has to run first thing in Main.
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        var builder = WebApplication.CreateBuilder(args);

        // Kestrel's default on a plain (non-TLS) endpoint is HTTP/1.1-only -- gRPC requires
        // HTTP/2, so without this the server rejects/mishandles the very h2c calls the switch
        // above just enabled the client to attempt (this service is both a gRPC client, for
        // AuthUrl, and a gRPC server, for GetTenantService/other TenantApi RPCs).
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ConfigureEndpointDefaults(listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
            });

            // A single cleartext endpoint can't actually serve both protocols: without TLS/ALPN
            // there's no per-connection negotiation, so Kestrel commits every connection on the
            // Http1AndHttp2 endpoint above to HTTP/1.1 -- a prior-knowledge h2c request (what the
            // gRPC client above sends) gets rejected outright with HTTP_1_1_REQUIRED. This is
            // Microsoft's documented fix: a second endpoint set to Http2 only, dedicated to h2c
            // gRPC traffic. Grpc_AuthUrl must point here (port 8089), not at the main REST port.
            options.ListenAnyIP(8089, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
        // MassTransit's routine bus/transport lifecycle chatter (endpoint configuration, bus
        // start, message consumed) is Information-level and drowns out everything else in Seq --
        // baked in here rather than left to a per-environment Serilog:MinimumLevel:Override:
        // MassTransit env var, so it's never accidentally missing in a new environment.
        .MinimumLevel.Override("MassTransit", LogEventLevel.Warning)
        .CreateLogger();
        builder.Host.UseSerilog();

        Log.Information("Tenant api");
        Serilog.Debugging.SelfLog.Enable(Console.Error);

        var mpackOptions = MessagePackSerializerOptions.Standard
                .WithResolver(CompositeResolver.Create(
                    OneMessagePackResolver.Instance, // Your generated resolver
                    MessagePack.Resolvers.NativeDateTimeResolver.Instance,
                    MessagePack.Resolvers.ContractlessStandardResolver.Instance
                ))
                .WithCompression(MessagePackCompression.Lz4BlockArray);
        MessagePackSerializer.DefaultOptions = mpackOptions;
        builder.Services.AddControllers(options =>
        {
            options.Filters.Add<ResponseWrapperFilter>();
            options.RespectBrowserAcceptHeader = true;
            //options.InputFormatters.Insert(0, new MessagePackInputFormatter(mpackOptions));
            //options.OutputFormatters.Insert(0, new MessagePackOutputFormatter(mpackOptions));
        })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        }).AddMvcOptions(options =>
        {
            // This executes after AddNewtonoftJson has fully set up the defaults,
            // ensuring MessagePack is placed safely at the bottom (index 1 or higher)
            options.InputFormatters.Add(new MessagePackInputFormatter(mpackOptions));
            options.OutputFormatters.Add(new MessagePackOutputFormatter(mpackOptions));
        });

        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.SuppressModelStateInvalidFilter = true;
        });
        builder.Services.AddProblemDetails(c =>
        {
            //c.CustomizeProblemDetails = context =>
            //{
            //    context.ProblemDetails.Extensions.TryAdd("requestId", context.HttpContext.TraceIdentifier);
            //};
        });

        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        var config = new TypeAdapterConfig();
        config.Default.NameMatchingStrategy(NameMatchingStrategy.Flexible);

        builder.Services.AddPollyPolicies();
        builder.Services.AddSignalR();
        builder.Services.AddMapster(typeof(MapperProfileConfig).Assembly);
        builder.RegisterSelfServices();
        builder.RegisterCoreServices();
        //builder.TenantConfigKafka();
        builder.TenantConfigRabbitMq();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SchemaFilter<EnumSchemaFilter>();
            options.OperationFilter<SwaggerHeader>();
        });

        builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(@"/app/dp-keys"));

        var app = builder.Build();

        // One-time (idempotent) global RBAC catalog seed + MembershipRole backfill -- see
        // PermissionCatalogSeederService. Runs before the app starts accepting traffic.
        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<PermissionCatalogSeederService>()
                .EnsureSeededAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        // 1. FIRST: Fix headers from Nginx so .NET knows the real IP/Protocol immediately
        var forwardedOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost
        };
        forwardedOptions.KnownNetworks.Clear();
        forwardedOptions.KnownProxies.Clear();
        app.UseForwardedHeaders(forwardedOptions);
        // 2. SECOND: Catch global exceptions early
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        // 3. THIRD: API Documentation
        var apiVersionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
            options.EnablePersistAuthorization();
            options.DefaultModelsExpandDepth(-1);

            foreach (var description in apiVersionProvider.ApiVersionDescriptions)
            {
                // Notice the dot '.' making this a relative path layout
                options.SwaggerEndpoint($"./{description.GroupName}/swagger.json",
                                        $"TENANT API {description.ApiVersion}");
                options.ConfigObject.PersistAuthorization = true;
            }
            options.RoutePrefix = "swagger";
        });
        // 4. FOURTH: Centralized Logging (safely handles forwarded context)
        app.UseSerilogRequestLogging();
        // NOTE: Removed app.UseHttpsRedirection() to prevent proxy redirect loops.
        // 5. FIFTH: Core Routing & Outbound Header management
        app.UseRouting();
        app.UseCors("AllowAll");
        app.UseHeaderPropagation();
        // // Keep custom middlewares stacked here if uncommented
        // app.UseMiddleware<CorrelationIdMiddleware>(); 
        // app.UseMiddleware<ApiKeyMiddleware>();
        // 6. SIXTH: Security Handshake
        app.UseAuthentication();
        app.UseAuthorization();
        // 7. LAST: Map your execution endpoints
        app.MapGrpcService<TenantInfoServiceProvider>();
        app.MapControllers();
        app.Run();
    }
}