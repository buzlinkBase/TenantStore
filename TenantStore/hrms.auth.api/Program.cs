using Asp.Versioning.ApiExplorer; 
using OnePunch.Auth.Api;
using OnePunch.Auth.Api.Middlewares; 
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
        builder.Services.AddSwaggerGen(c =>
        {
            c.SchemaFilter<EnumSchemaFilter>();
        });

        builder.RegisterMessageHandlers();
        builder.RegisterSelftServices();
        builder.Services.RegisterCoreServices();
        builder.Services.AddAutoMapper(typeof(MappingProfile));

        builder.Services.AddHttpClient<TenantHttpClient>(client =>
        {
            var TenantUrl = builder.Configuration["TenantUrl"];
            client.BaseAddress = new Uri(TenantUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });


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
        app.UseMiddleware<ApiKeyMiddleware>();
        app.UseAuthentication();  
        app.UseAuthorization();  
        app.UseMiddleware<ResponseWrapperMiddleware>();
        app.MapControllers();
        app.Run();
    }
}