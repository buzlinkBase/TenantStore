using Asp.Versioning.Conventions;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Auth.Core;
using Onepunch.Auth.Infrastructure;
using Onepunch.Auth.Infrastructure.Data;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.DTO;
using OnePunch.Auth.Core.Messaging;
using OnePunch.Auth.Core.Providers;
using System.Text;

namespace OnePunch.Auth.Api;

public static class ServiceRegistrations
{
    public static void RegisterSelftServices(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<RouteOptions>(options => { options.LowercaseUrls = true; });
        builder.Services.Configure<HMacSetting>(builder.Configuration.GetSection("HMacSettings"));
        builder.Services.Configure<CryptoSetting>(builder.Configuration.GetSection("Crypto"));
        builder.Services.Configure<Domains>(builder.Configuration.GetSection("Domains"));
        builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
        builder.Services.Configure<KafkaSettings>(builder.Configuration.GetSection("KafkaSettings"));
        builder.Services.AddHeaderPropagation(options =>
        {
            options.Headers.Add("User-Agent");
            options.Headers.Add("Authorization");
            options.Headers.Add("X-Tenant-ID");
            options.Headers.Add("X-Api-Key");
        });

        builder.Services.AddGrpc();
        builder.Services.AddGrpcClient<GetTenantService.GetTenantServiceClient>(options =>
        {
            var tenantUrl = builder.Configuration["TenantUrl"]?.ToString() ?? "";
            options.Address = new Uri(tenantUrl);
        });

        //builder.Services.AddHttpContextAccessor();
        builder.Services.AddDataProtection();
        builder.Services.AddLogging();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITenantProvider, TenantProvider>();
        builder.Services.AddScoped<WebTenantContextAccessor>();
        builder.Services.AddScoped<MessagingTenantContextAccessor>();
        builder.Services.AddScoped<ITenantContextAccessor>(sp =>
        {
            var httpContext = sp.GetRequiredService<IHttpContextAccessor>();
            if (httpContext.HttpContext != null)
            {
                return sp.GetRequiredService<WebTenantContextAccessor>();
            }
            return sp.GetRequiredService<MessagingTenantContextAccessor>();
        });

        //builder.Services.AddHostedService<CleanupOutboxWorker>();
        //builder.Services.AddHostedService<OutboxWorker>();
        //builder.Services.AddHostedService<TenantCreatedWorker>();
        var bootstrapServers = builder.Configuration["KafkaSettings:BootstrapServers"] ?? "";
        builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<ProducerService>(sp, bootstrapServers));
        builder.Services.AddIdentity<User, Role>(options =>
        {
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<AuthContext>()
        .AddDefaultTokenProviders();

        builder.Services.AddScoped<JwtService>();
        builder.Services.AddDbContext<AuthContext>((provider, options) =>
        {
            var defaultConn = builder.Configuration.GetConnectionString("DbConnection");
            var connectionString = defaultConn!;

            var tenantAccessor = provider.GetRequiredService<ITenantContextAccessor>();
            var tenantId = tenantAccessor.GetTenantId();
            var tenantProvider = provider.GetRequiredService<ITenantProvider>();
            if (tenantProvider.TenantId == Guid.Empty)
                tenantProvider.SetTenantId(tenantId);


            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
            options.AddInterceptors(new ApplyTenantInterceptor(tenantProvider));
            options.AddInterceptors(new SoftDeleteInterceptor());
            options.UseLazyLoadingProxies(true);
            options.ReplaceService<IModelCacheKeyFactory, Onepunch.Auth.Infrastructure.TenantModelCacheKeyFactory>();
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

        builder.Services.AddIdentityCore<User>(options =>
         {
             options.Password.RequireDigit = true;
             options.Password.RequireLowercase = true;
             options.Password.RequireNonAlphanumeric = true;
             options.Password.RequireUppercase = true;
             options.Password.RequiredLength = 6;
             options.Password.RequiredUniqueChars = 1;
             options.User.RequireUniqueEmail = true;
             options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
             options.Lockout.MaxFailedAccessAttempts = 5;
             options.Lockout.AllowedForNewUsers = true;
             options.ClaimsIdentity.SecurityStampClaimType = "AspNet.Identity.SecurityStamp";
             //options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
         })
         .AddEntityFrameworkStores<AuthContext>()
         .AddApiEndpoints(); // Optional, enables MapIdentityApi
        builder.Services.AddApiVersioning(
                options =>
                {
                    options.DefaultApiVersion = new ApiVersion(1.0);
                    options.AssumeDefaultVersionWhenUnspecified = true;
                    // reporting api versions will return the headers
                    // "api-supported-versions" and "api-deprecated-versions"
                    options.ReportApiVersions = true;
                    options.ApiVersionReader = ApiVersionReader.Combine(
                           new UrlSegmentApiVersionReader(),
                           new QueryStringApiVersionReader("api-version"),
                           new HeaderApiVersionReader("X-Version"),
                           new MediaTypeApiVersionReader("x-version"));
                })
            .AddMvc(
                options =>
                {
                    // automatically applies an api version based on the name of
                    // the defining controller's namespace
                    options.Conventions.Add(new VersionByNamespaceConvention());
                })
            .AddApiExplorer(setup =>
            {
                setup.GroupNameFormat = "'v'VVV";
                setup.SubstituteApiVersionInUrl = true;
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
