using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core.Messaging;
using Onepunch.Auth.Core.Protos;
using Onepunch.Common.Lib;
using OnePunch.Auth.Api;
using OnePunch.Auth.Api.Middlewares;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json;
using System.Text.Json.Serialization;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            //options.JsonSerializerOptions.PropertyNamingPolicy = new SnakeCaseNamingPolicy();
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        }); 

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
        builder.Services.AddSwaggerGen();
        //builder.Services.AddSwaggerGen(options =>
        //{
        //    options.SchemaFilter<EnumSchemaFilter>();
        //    options.OperationFilter<SwaggerHeader>(); 
        //});
        builder.Services.AddPollyPolicies();
        builder.RegisterSelftServices();
        builder.Services.RegisterCoreServices();
        builder.Services.AddAutoMapper(typeof(MappingProfile));

        var app = builder.Build();
        var apiVersionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            //options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
            options.DefaultModelsExpandDepth(-1);
            foreach (var description in apiVersionProvider.ApiVersionDescriptions)
            {
                options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json",
                                        $"AUTH API {description.ApiVersion}");
                options.ConfigObject.PersistAuthorization = true; 
            }
        });

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