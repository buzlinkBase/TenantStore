using Asp.Versioning.ApiExplorer;
using DTR.Api;
using DTR.Api.Filters;
using MessagePack;
using MessagePack.AspNetCoreMvcFormatter;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

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
                // Priority 2: Handling for dynamic/contractless if you still have old models
                MessagePack.Resolvers.ContractlessStandardResolver.Instance
            ))
            .WithCompression(MessagePackCompression.Lz4BlockArray);

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
            options.SuppressModelStateInvalidFilter = true;
        });
        builder.RegisterApi();
        //builder.RegisterMessageHandlers();
        builder.RegisterSelfServices();
        builder.Services.DTRServiceConfig();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        //ServicePointManager.DefaultConnectionLimit = 100;
        var app = builder.Build();
        var apiVersionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.DefaultModelsExpandDepth(-1);
            foreach (var description in apiVersionProvider.ApiVersionDescriptions)
            {
                options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json",
                                        $"DTR API {description.ApiVersion}");

                options.ConfigObject.PersistAuthorization = true;
            }
        });

        app.UseHttpsRedirection();
        //app.UseExceptionHandler();
        app.UseRouting();
        app.UseCors("AllowAll");
        //app.UseMiddleware<CorrelationIdMiddleware>(); 
        //app.UseMiddleware<ApiKeyMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseHeaderPropagation();
        app.MapControllers();
        app.Run();
    }
} 