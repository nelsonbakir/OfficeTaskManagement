namespace OfficeTaskManagement.ViewModels.UserManagement
{
    public class UserListViewModel
    {
        public string Id { get; set; }
        public string Email { get; set; }
        public string FullName { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
        public bool IsActive { get; set; }
        public DateTime? CreatedDate { get; set; }
        public int? TotalTasks { get; set; }
        public int? CompletedTasks { get; set; }
    }
}
