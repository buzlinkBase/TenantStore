using Asp.Versioning.Conventions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Auth.Core;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Cache;
using OnePunch.Auth.Core.Providers;
using StackExchange.Redis;
using System.Net;
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
        builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection("RabbitMqSettings"));
        builder.Services.AddSingleton<IConnectionMultiplexer>(sp => 
        ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
        builder.Services.AddScoped<ICacheService, RedisCacheService>();

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
            var tenantUrl = builder.Configuration["Domains:TenantUrl"]?.ToString() ?? "";
            options.Address = new Uri(tenantUrl);
        });
        builder.Services.AddDataProtection();
        builder.Services.AddLogging();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITenantProvider, TenantProviderAccessor>();
        builder.Services.AddIdentity<User, Domain.Entities.Role>(options =>
        {
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<AuthContext>()
        .AddDefaultTokenProviders();

        builder.Services.AddScoped<JwtService>();
        builder.Services.AddDbContext<AuthContext>((provider, options) =>
        {
            var defaultConn = builder.Configuration.GetConnectionString("AuthConnection");
            var connectionString = defaultConn!;
            options.UseMySql(connectionString, new MySqlServerVersion(new Version(9, 2, 0)));
            options.AddInterceptors(new SoftDeleteInterceptor());
            options.UseLazyLoadingProxies(true);
        });

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.WithOrigins("http://localhost:5173", "http://localhost:4200")
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
         .AddRoles<Domain.Entities.Role>()
         .AddEntityFrameworkStores<AuthContext>()
         .AddApiEndpoints();  

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
        builder.Services.AddAuthorizationBuilder()
         .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
        builder.Services.AddAuthentication(options =>
        {
            // Default to JWT for API requests
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            // IMPORTANT: This handles the temporary data Google sends back
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
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
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    Console.WriteLine("Auth failed: " + context.Exception.Message);
                    return Task.CompletedTask;
                },
                OnChallenge = async context =>
                {
                    // Skip the default response
                    context.HandleResponse();

                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";

                    var errorDetail = new ProblemDetails
                    {
                        Type = $"https://httpstatuses.com/{401}",
                        Title = "Unauthorized",
                        Status = (int)HttpStatusCode.Unauthorized,
                        Detail = "Unauthorized. Token is missing or invalid.",
                        Instance = $"{context.Request.Method} {context.Request.Path}"
                    };
                    var response = new ResponseModel<ProblemDetails>
                    {
                        Message = errorDetail.Detail,
                        Status = (int)HttpStatusCode.Unauthorized,
                        Data = errorDetail
                    };
                    await context.Response.WriteAsJsonAsync(response);
                },

                // ✅ Add this — fires when token is valid but user lacks permission
                OnForbidden = async context =>
                {
                    context.Response.StatusCode = 403;
                    context.Response.ContentType = "application/json";

                    var errorDetail = new ProblemDetails
                    {
                        Type = $"https://httpstatuses.com/{403}",
                        Title = "Forbidden",
                        Status = (int)HttpStatusCode.Forbidden,
                        Detail = "Forbidden. You do not have permission to access this resource.",
                        Instance = $"{context.Request.Method} {context.Request.Path}"
                    };
                    var response = new ResponseModel<ProblemDetails>
                    {
                        Message = errorDetail.Detail,
                        Status = (int)HttpStatusCode.Unauthorized,
                        Data = errorDetail
                    };

                    await context.Response.WriteAsJsonAsync(response);
                }
            };
        })
        //.AddCookie()
        .AddGoogle(options =>
        {
            options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
            options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
        })
        ;
    }
}
