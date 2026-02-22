using Asp.Versioning;
using BuzlinkRepository;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Common.Lib;
using System.Text;
using TenantStoreApi.Core.Providers;
using TenantStoreApi.Infrastructure;

public static class ServiceRegistrations
{
    public static void RegisterSelfServices(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<RouteOptions>(options => { options.LowercaseUrls = true; });
        builder.Services.Configure<HMacSetting>(builder.Configuration.GetSection("HMacSettings"));
        builder.Services.Configure<CryptoSetting>(builder.Configuration.GetSection("Crypto"));
        builder.Services.Configure<KafkaSettings>(builder.Configuration.GetSection("KafkaSettings"));
       
        builder.Services.AddHeaderPropagation(options =>
        {
            options.Headers.Add("User-Agent");
            options.Headers.Add("Authorization");
            options.Headers.Add("X-Tenant-ID");
            options.Headers.Add("X-Api-Key");
        });
        builder.Services.AddGrpc();
        builder.Services.AddGrpcClient<CheckEmailService.CheckEmailServiceClient>(options =>
        {
            var authUrl = builder.Configuration["AuthUrl"]?.ToString() ?? "";
            options.Address = new Uri(authUrl);
        }).AddHeaderPropagation();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddLogging();
        builder.Services.Configure<RouteOptions>(options => { options.LowercaseUrls = true; });

        builder.Services.AddScoped<ITenantProvider, TenantProvider>();
        builder.Services.AddScoped<ITenantContextAccessor, WebTenantContextAccessor>();
        builder.Services.AddHostedService<UserConfirmedWorker>();
        builder.Services.AddHostedService<OutboxWorker>(); 
        var bootstrapServers = builder.Configuration["KafkaSettings:BootstrapServers"] ?? "";
        builder.Services.AddSingleton<ProducerService>(sp =>
                ActivatorUtilities.CreateInstance<ProducerService>(sp, bootstrapServers));

        builder.Services.AddDbContext<TenantContext>((sp, options) =>
        {
            var connectionString = builder.Configuration.GetConnectionString("DbConnection");
            var tp = sp.GetRequiredService<ITenantProvider>();
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
            options.AddInterceptors(new ApplyTenantInterceptor(tp), new SoftDeleteInterceptor());
        });

        builder.Services.AddDbContext<TenantContext>((provider, options) =>
        {
            var tenantAccessor = provider.GetRequiredService<ITenantContextAccessor>();
            var tenantProvider = provider.GetRequiredService<ITenantProvider>();
            var tenantId = tenantAccessor.GetTenantId();
            var defaultConn = builder.Configuration.GetConnectionString("DbConnection");
            var connectionString = defaultConn!;
            if (tenantProvider.TenantId == Guid.Empty)
                tenantProvider.SetTenantId(tenantId);

            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
            options.AddInterceptors(new ApplyTenantInterceptor(tenantProvider));
            options.AddInterceptors(new SoftDeleteInterceptor());
            options.UseLazyLoadingProxies(true);
            options.ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>();
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