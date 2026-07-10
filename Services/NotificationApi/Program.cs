using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Mapster;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using OnePunch.Notification.Infrastructure;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);


Log.Logger = new LoggerConfiguration()
   .ReadFrom.Configuration(builder.Configuration)
   .CreateLogger();
builder.Host.UseSerilog();

Log.Information("Notif api");
Serilog.Debugging.SelfLog.Enable(Console.Error);


builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
builder.Services.AddSwaggerGen();
builder.Services.AddPollyPolicies();
builder.RegisterSelftServices();
builder.RegisterCoreServices();
//builder.NotifConfigKafka();
builder.NotifConfigRabbitMq();

builder.Services.AddMapster(typeof(MappingProfile).Assembly);
builder.Services.AddDbContext<NotifContext>(options =>
{
    var connectionstring = builder.Configuration.GetConnectionString("NotifConnection");
    options.UseMySql(connectionstring, new MySqlServerVersion(new Version(9, 2, 0)));
});
builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(@"/app/dp-keys"));

var app = builder.Build();
// 1. FIRST: Parse headers from Nginx on localhost immediately
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor
                     | ForwardedHeaders.XForwardedProto
                     | ForwardedHeaders.XForwardedHost
};
forwardedOptions.KnownNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);
// 2. SECOND: API Documentation
var apiVersionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
    options.DefaultModelsExpandDepth(-1);
    foreach (var description in apiVersionProvider.ApiVersionDescriptions)
    {
        options.SwaggerEndpoint($"./{description.GroupName}/swagger.json",
                               $"NOTIFICATION API {description.ApiVersion}");
        options.ConfigObject.PersistAuthorization = true;
    }
    options.RoutePrefix = "swagger";
});
// 3. THIRD: Central Logging 
app.UseSerilogRequestLogging();
// NOTE: Removed app.UseHttpsRedirection() to prevent Nginx proxy redirect loops
// 4. FOURTH: App Execution Setup 
app.UseRouting();
app.UseCors("AllowAll"); // Placed cleanly between Routing and Security Handshake
// 5. FIFTH: Security Handshake
app.UseAuthentication(); // Included in case your controllers require [Authorize] attributes
app.UseAuthorization();
// 6. LAST: Map execution endpoints
app.MapControllers();
app.Run();