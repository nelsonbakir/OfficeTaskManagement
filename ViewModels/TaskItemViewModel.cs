using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using OfficeTaskManagement.Models;
using System.Collections.Generic;

namespace OfficeTaskManagement.ViewModels
{
    public class TaskItemViewModel
    {
        public TaskItem TaskItem { get; set; } = new TaskItem();
        public IFormFile? Attachment { get; set; }
        public SelectList? UsersList { get; set; }
        public SelectList? ProjectsList { get; set; }
        public SelectList? SprintsList { get; set; }
        public SelectList? FeaturesList { get; set; }
        public SelectList? ParentTasksList { get; set; }
        public SelectList? UserStoriesList { get; set; }
        public MultiSelectList? AreasList { get; set; }
        public List<int> SelectedAreaIds { get; set; } = new List<int>();
    }
}
