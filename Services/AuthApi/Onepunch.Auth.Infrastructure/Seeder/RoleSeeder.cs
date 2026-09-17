//using Microsoft.AspNetCore.Identity;
//using OnePunch.Auth.Domain.Entities;

//namespace Onepunch.Auth.Infrastructure.Seeder;

//public class RoleSeeder
//{
//    public static async Task SeedRolesAsync(RoleManager<Role> roleManager)
//    {
//        string[] roles = { "Admin", "Manager", "User", "Member" };
//        foreach (var role in roles)
//        {
//            if (!await roleManager.RoleExistsAsync(role))
//                await roleManager.CreateAsync(new Role() { Name = role });
//        }
//    }
//}