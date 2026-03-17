using Asp.Versioning;
using BuzlinkRepository;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.Tokens;
using System.Net.Http.Headers;
using System.Text;
using TenantStoreApi.Core.Providers;
using TenantStoreApi.Infrastructure;

public static class ServiceRegistrations
{
    public static void RegisterSelfServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddGrpcClient<CheckEmailService.CheckEmailServiceClient>(options =>
        {
            var authUrl = builder.Configuration["AuthUrl"]?.ToString() ?? "";
            options.Address = new Uri(authUrl);
        }).AddHeaderPropagation();

        var doToken = builder.Configuration["DigitalOcean:ApiToken"];
        builder.Services.AddHttpClient<DigitalOceanDbService>(client =>
        {
            client.BaseAddress = new Uri("https://api.digitalocean.com/v2/databases/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", doToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        })
         .AddStandardResilienceHandler();
        builder.Services.Configure<RouteOptions>(options => { options.LowercaseUrls = true; });
        builder.Services.Configure<HMacSetting>(builder.Configuration.GetSection("HMacSettings"));
        builder.Services.Configure<CryptoSetting>(builder.Configuration.GetSection("Crypto"));
        builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection("RabbitMqSettings"));
        builder.Services.AddHeaderPropagation(options =>
        {
            options.Headers.Add("User-Agent");
            options.Headers.Add("Authorization");
            options.Headers.Add("X-Tenant-ID");
            options.Headers.Add("X-Api-Key");
        });
        builder.Services.AddGrpc(); 
        builder.Services.AddLogging();
        builder.Services.Configure<RouteOptions>(options => { options.LowercaseUrls = true; });
        builder.Services.AddScoped<ITenantProvider, TenantProviderAccessor>();
        builder.Services.AddDbContext<TenantContext>((provider, options) =>
        {
            var defaultConn = builder.Configuration.GetConnectionString("DbConnection");
            options.UseMySql(defaultConn, ServerVersion.AutoDetect(defaultConn));
            options.AddInterceptors(new SoftDeleteInterceptor());
            options.UseLazyLoadingProxies(true);
        });

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });
        builder.Services
            .AddApiVersioning(options =>
            {
                options.ReportApiVersions = true;
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.DefaultApiVersion = new ApiVersion(1, 0);
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
         .AddJwtBearer(options =>
         {
             options.TokenValidationParameters = new TokenValidationParameters
             {
                 ValidateIssuer = true,
                 ValidIssuer = "Onepunch",
                 ValidateAudience = true,
                 ValidAudience = "Onepunch.AuthService",
                 ValidateLifetime = true,
                 ValidateIssuerSigningKey = true,
                 IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["JwtSettings:SigningKey"]!))
             };
         });
    }
}
