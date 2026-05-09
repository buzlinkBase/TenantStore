using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core.Protos;
using Onepunch.Common.Lib;
using OnePunch.Auth.Api;
using OnePunch.Auth.Api.Exceptions;
using OnePunch.Auth.Api.Filters;
using OnePunch.Auth.Api.Middlewares;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json;
using System.Text.Json.Serialization;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddControllers(options =>
        {
            options.Filters.Add<ResponseWrapperFilter>();
        })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            //options.JsonSerializerOptions.PropertyNamingPolicy = new SnakeCaseNamingPolicy();
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
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

        var app = builder.Build();
        var apiVersionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
            options.DefaultModelsExpandDepth(-1);
            options.EnablePersistAuthorization();
            foreach (var description in apiVersionProvider.ApiVersionDescriptions)
            {
                options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json",
                                        $"AUTH API {description.ApiVersion}");
                options.ConfigObject.PersistAuthorization = true; 
            }
        });

        app.UseStatusCodePages();
        app.UseExceptionHandler();
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