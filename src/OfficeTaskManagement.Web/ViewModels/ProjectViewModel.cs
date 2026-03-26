using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.ViewModels
{
    public class ProjectViewModel
    {
        public Project Project { get; set; } = new Project();
        public IFormFile? Logo { get; set; }
        public List<IFormFile>? Attachments { get; set; }
    }
}
