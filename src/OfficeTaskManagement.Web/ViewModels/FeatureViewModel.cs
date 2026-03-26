using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.ViewModels
{
    public class FeatureViewModel
    {
        public Feature Feature { get; set; } = new Feature();
        public List<IFormFile>? Attachments { get; set; }
    }
}
