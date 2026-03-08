using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.ViewModels
{
    public class UserStoryViewModel
    {
        public UserStory UserStory { get; set; } = new UserStory();
        public List<IFormFile>? Attachments { get; set; }
    }
}
