using Microsoft.AspNetCore.Http;
using OfficeTaskManagement.Models;
using System.Collections.Generic;

namespace OfficeTaskManagement.ViewModels
{
    public class TestCaseViewModel
    {
        public TestCase TestCase { get; set; } = new TestCase();
        public List<IFormFile>? Attachments { get; set; }
    }
}
