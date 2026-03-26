using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.ViewModels
{
    public class EpicViewModel
    {
        public Epic Epic { get; set; } = new Epic();
        public List<IFormFile>? Attachments { get; set; }
    }
}
