using Asp.Versioning.ApiExplorer;
using Mapster;
using MessagePack;
using MessagePack.AspNetCoreMvcFormatter;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Serilog;
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
        var builder = WebApplication.CreateBuilder(args);

        Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
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
            options.InputFormatters.Insert(0, new MessagePackInputFormatter(mpackOptions));
            options.OutputFormatters.Insert(0, new MessagePackOutputFormatter(mpackOptions));
            options.Filters.Add<ResponseWrapperFilter>();
        })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
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
        // Configure the HTTP request pipeline.
        //if (app.Environment.IsDevelopment())
        //{
        //app.UseDeveloperExceptionPage();
        var apiVersionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
            options.EnablePersistAuthorization();
            options.DefaultModelsExpandDepth(-1);
            foreach (var description in apiVersionProvider.ApiVersionDescriptions)
            {
                options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json",
                                        $"TENANT API {description.ApiVersion}");

                options.ConfigObject.PersistAuthorization = true;
            }
        });

        app.UseHttpsRedirection();
        app.UseStatusCodePages();
        app.UseExceptionHandler();
        app.UseRouting();
        app.UseCors("AllowAll");
        //app.UseMiddleware<CorrelationIdMiddleware>(); 
        //app.UseMiddleware<ApiKeyMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseHeaderPropagation();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGrpcService<TenantInfoServiceProvider>();
        app.MapControllers();
        app.Run();
    }
}