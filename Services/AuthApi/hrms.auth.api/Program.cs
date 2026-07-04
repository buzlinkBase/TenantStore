using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core.Protos;
using Onepunch.Auth.Infrastructure.Seeder;
using Onepunch.Common.Lib;
using OnePunch.Auth.Api;
using OnePunch.Auth.Api.Exceptions;
using OnePunch.Auth.Api.Filters;
using OnePunch.Auth.Api.Middlewares;
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

        builder.Services.AddControllers(options =>
        {
            options.Filters.Add<ResponseWrapperFilter>();
        })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            //options.JsonSerializerOptions.PropertyNamingPolicy = new SnakeCaseNamingPolicy();
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        });
        builder.Services.AddProblemDetails(c =>
        {
            //c.CustomizeProblemDetails = context =>
            //{
            //    context.ProblemDetails.Extensions.TryAdd("requestId", context.HttpContext.TraceIdentifier);
            //};
        });
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
        app.MapGrpcService<CheckEmailHandler>();
        app.MapControllers();
        app.Run();
    }
}