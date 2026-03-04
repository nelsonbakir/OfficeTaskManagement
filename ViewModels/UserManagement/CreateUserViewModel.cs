namespace OfficeTaskManagement.ViewModels.UserManagement
{
    public class CreateUserViewModel
    {
        public string Email { get; set; }
        public string FullName { get; set; }
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
        public List<string> SelectedRoles { get; set; } = new List<string>();
        public List<string> AvailableRoles { get; set; } = new List<string>();
    }
}
