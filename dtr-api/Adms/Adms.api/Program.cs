using Adms.api;
using Adms.api.Extensions;
using Adms.api.Services;
using BuzlinkRepository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
//builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<AttendanceService>();
builder.Services.RegisterLibServices();

builder.Services.AddDbContext<AdmsContext>(option =>
{
    var connection = builder.Configuration.GetConnectionString("DefaultConnection");
    option.UseMySql(connection, ServerVersion.AutoDetect(connection))
    .LogTo(Console.WriteLine, LogLevel.Information)
                .EnableSensitiveDataLogging()
                .EnableDetailedErrors()
    ;
});


builder.Services.AddScoped<ITenantProvider, TenantProvider>();
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<AspAutoMapperProfile>());
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    //app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
