using Asp.Versioning;
using BuzlinkRepository;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Net;
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

        builder.Services.AddDbContext<TenantContext>(options =>
        {
            var defaultConn = builder.Configuration.GetConnectionString("TenantConnection");
            options.UseMySql(defaultConn, new MySqlServerVersion(new Version(9, 2, 0)));
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
        builder.Services.AddAuthorizationBuilder()
         .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

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
         });
    }
}
