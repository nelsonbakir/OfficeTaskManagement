using Microsoft.AspNetCore.Identity;
using OfficeTaskManagement.Models;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Data
{
    public static class SeedData
    {
        public static async Task Initialize(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<User>>();

            string[] roleNames = { "Employee", "Project Lead", "Project Coordinator", "Manager", "Client" };

            foreach (var roleName in roleNames)
            {
                var roleExist = await roleManager.RoleExistsAsync(roleName);
                if (!roleExist)
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // Create default Manager
            var adminUser = await userManager.FindByEmailAsync("admin@example.com");
            if (adminUser == null)
            {
                var user = new User
                {
                    UserName = "admin@example.com",
                    Email = "admin@example.com",
                    FullName = "Admin User"
                };
                var createPowerUser = await userManager.CreateAsync(user, "YourDefaultAdminPassword@123!");
                if (createPowerUser.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, "Manager");
                }
            }
        }
    }
}
