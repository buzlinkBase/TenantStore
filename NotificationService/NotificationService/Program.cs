using Asp.Versioning.ApiExplorer;
using System.Text.Json;
using System.Text.Json.Serialization;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            //options.JsonSerializerOptions.PropertyNamingPolicy = new SnakeCaseNamingPolicy();
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });


builder.Services.AddSingleton<PollyPolicy>();
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
                                $"Notification Service API {description.ApiVersion}");

        options.ConfigObject.PersistAuthorization = true;
    }
});
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
