using Asp.Versioning.ApiExplorer;
using BuzlinkRepository;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Text.Json;
using System.Text.Json.Serialization;
using TenantStoreApi;
using TenantStoreApi.Core.Extensions;
using TenantStoreApi.Core.Utilities;
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

        builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            //options.JsonSerializerOptions.PropertyNamingPolicy = new SnakeCaseNamingPolicy();
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

        builder.Services.AddHttpClient<AuthHttpClient>(client =>
        {
            var authUrl = builder.Configuration["AuthUrl"];
            client.BaseAddress = new Uri(authUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }); 

        builder.Services.AddAutoMapper(typeof(MapperProfileConfig).Assembly);
        builder.RegisterMessageHandlers();
        builder.RegisterSelfServices();
        builder.Services.RegisterCoreServices();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();
        //BzServiceProvider.Instance.SetServiceProvider(app.Services);

        // Configure the HTTP request pipeline.
        //if (app.Environment.IsDevelopment())
        //{
        //app.UseDeveloperExceptionPage();
        var apiVersionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            //options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
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
        app.UseAuthentication();
        app.UseAuthorization();
        //app.UseMiddleware<ResponseWrapperMiddleware>();
        app.MapControllers();
        app.Run();
    }
}