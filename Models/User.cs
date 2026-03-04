using Microsoft.AspNetCore.Identity;

namespace OfficeTaskManagement.Models
{
    public class User : IdentityUser
    {
        public string? FullName { get; set; }
    }
}
