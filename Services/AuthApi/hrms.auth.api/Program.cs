using Asp.Versioning.ApiExplorer;
using MessagePack;
using MessagePack.AspNetCoreMvcFormatter;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core.Interfaces;
using Onepunch.Auth.Core.Protos;
using Onepunch.Auth.Domain; 
using Onepunch.Common.Lib;
using OnePunch.Auth.Api;
using OnePunch.Auth.Api.Exceptions;
using OnePunch.Auth.Api.Filters;
using OnePunch.Auth.Api.Middlewares;
using Refit;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json;
using System.Text.Json.Serialization;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
        .CreateLogger();
        builder.Host.UseSerilog();

        Log.Information("Auth api");
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
        }).AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        }).AddMvcOptions(options =>
        {
            options.InputFormatters.Add(new MessagePackInputFormatter(mpackOptions));
            options.OutputFormatters.Add(new MessagePackOutputFormatter(mpackOptions));
        });

        builder.Services.AddProblemDetails(c =>
        {
            //c.CustomizeProblemDetails = context =>
            //{
            //    context.ProblemDetails.Extensions.TryAdd("requestId", context.HttpContext.TraceIdentifier);
            //};
        });

        builder.Services.AddRefitClient<IAccountMembershipClient>(new RefitSettings
        {
            ContentSerializer = new Onepunch.Common.Lib.MessagePackContentSerializer(mpackOptions)
        })
        .ConfigureHttpClient(c =>
        {
            var tenantUrl = builder.Configuration["Domains:TenantUrl"]!;
            c.BaseAddress = new Uri(tenantUrl);

        })
        .AddHeaderPropagation();

        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
        //builder.Services.AddSwaggerGen();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SchemaFilter<EnumSchemaFilter>();
            //options.OperationFilter<SwaggerHeader>();
        });
        builder.Services.AddPollyPolicies();
        builder.Services.AddSignalR();
        builder.RegisterSelftServices();
        builder.Services.RegisterCoreServices();
        builder.AuthConfigRabbitMq();
        builder.Services.AddMapster(typeof(MappingConfig).Assembly);
        builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(@"/app/dp-keys"));

        var app = builder.Build();
        await app.SeedRolesAsync();
        // 1. Configure and enable Forwarded Headers
        var forwardedOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost
        };
        // CRITICAL: Clear these collections so .NET trusts Nginx running on localhost
        forwardedOptions.KnownNetworks.Clear();
        forwardedOptions.KnownProxies.Clear();
        app.UseForwardedHeaders(forwardedOptions);
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        var apiVersionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
            options.DefaultModelsExpandDepth(-1);
            options.EnablePersistAuthorization();
            foreach (var description in apiVersionProvider.ApiVersionDescriptions)
            {
                options.SwaggerEndpoint($"./{description.GroupName}/swagger.json",
                                $"AUTH API {description.ApiVersion}");
                options.ConfigObject.PersistAuthorization = true;
            }
            options.RoutePrefix = "swagger";
        });
        app.UseSerilogRequestLogging();
        app.UseRouting();
        app.UseCors("AllowAll");
        //app.UseMiddleware<ApiKeyMiddleware>();  
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseHeaderPropagation();
        app.MapGrpcService<CheckEmailHandler>();
        app.MapControllers();
        app.Run();
    }
}