using Asp.Versioning.ApiExplorer;
using MessagePack;
using MessagePack.AspNetCoreMvcFormatter;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Onepunch.Common.Lib;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json;
using System.Text.Json.Serialization;
using TenantStoreApi;
using TenantStoreApi.Core.Extensions;
using TenantStoreApi.Core.Protos.ServiceHandlers;
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

        var mpackOptions = MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(
                // Priority 1: Compiled code (Fastest)
                OneMessagePackResolver.Instance,
                MessagePack.Resolvers.NativeDateTimeResolver.Instance,
                // Priority 2: Handling for dynamic/contractless if still have old models
                MessagePack.Resolvers.ContractlessStandardResolver.Instance
            ))
            .WithCompression(MessagePackCompression.Lz4BlockArray);
        MessagePackSerializer.DefaultOptions = mpackOptions;

        builder.Services.AddControllers(options =>
        {
            options.Filters.Add<ResponseWrapperFilter>();
            var mpackOptions = ContractlessStandardResolver.Options
                .WithCompression(MessagePackCompression.Lz4BlockArray);
            options.InputFormatters.Add(new MessagePackInputFormatter(mpackOptions));
            options.OutputFormatters.Add(new MessagePackOutputFormatter(mpackOptions));
        }).AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            // Stops the default framework behavior of returning a 400 immediately
            options.SuppressModelStateInvalidFilter = true;
        });
        //builder.Services.AddProblemDetails(c =>
        //{
        //    //c.CustomizeProblemDetails = context =>
        //    //{
        //    //    context.ProblemDetails.Extensions.TryAdd("requestId", context.HttpContext.TraceIdentifier);
        //    //};
        //});
        //builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        //var config = new MapperConfiguration(cfg =>
        //{
        //    cfg.SourceMemberNamingConvention = new PascalCaseNamingConvention();
        //    cfg.DestinationMemberNamingConvention = new LowerUnderscoreNamingConvention();
        //    cfg.AddProfile<MappingProfile>();
        //    cfg.AddProfile<AspAutoMapperProfile>();
        //});
        //builder.Services.AddSingleton<IMapper>(config.CreateMapper()); 

        builder.Services.AddPollyPolicies();
        builder.Services.AddAutoMapper(typeof(MapperProfileConfig).Assembly);
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


        //Console.WriteLine("--- LOADING DIAGNOSTIC ---");
        //foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        //{
        //    if (assembly.FullName.Contains("RabbitMQ.Client"))
        //    {
        //        Console.WriteLine($"Name: {assembly.FullName}");
        //        Console.WriteLine($"Location: {assembly.Location}");
        //    }
        //}
        //Console.WriteLine("--------------------------");


        //Console.WriteLine("--- DETECTIVE DIAGNOSTIC START ---");
        //var rabbitAssemblies = AppDomain.CurrentDomain.GetAssemblies()
        //    .Where(a => a.FullName.Contains("RabbitMQ.Client"));

        //foreach (var asm in rabbitAssemblies)
        //{
        //    Console.WriteLine($"LOADED: {asm.FullName}");
        //    Console.WriteLine($"PATH: {asm.Location}");
        //}
        //Console.WriteLine("--- DETECTIVE DIAGNOSTIC END ---");


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
        //app.UseExceptionHandler();
        app.UseRouting();
        app.UseCors("AllowAll");
        //app.UseMiddleware<CorrelationIdMiddleware>(); 
        //app.UseMiddleware<ApiKeyMiddleware>();
        app.UseHeaderPropagation();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGrpcService<TenantInfoServiceProvider>();
        app.MapControllers();
        app.Run();
    }
}