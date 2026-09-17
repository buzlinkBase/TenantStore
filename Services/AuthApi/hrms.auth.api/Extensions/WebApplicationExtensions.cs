//using Onepunch.Auth.Infrastructure.Seeder;

//namespace OnePunch.Auth.Api.Extensions;

//public static class WebApplicationExtensions
//{
//    public static async Task SeedRolesAsync(this WebApplication app)
//    {
//        using var scope = app.Services.CreateScope();
//        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
//        await RoleSeeder.SeedRolesAsync(roleManager);
//    }
//}