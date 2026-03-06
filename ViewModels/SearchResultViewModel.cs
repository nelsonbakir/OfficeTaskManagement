using System.Collections.Generic;

namespace OfficeTaskManagement.ViewModels
{
    public class SearchResultViewModel
    {
        public string Query { get; set; } = string.Empty;
        
        public List<SearchHit> Tasks { get; set; } = new List<SearchHit>();
        public List<SearchHit> Projects { get; set; } = new List<SearchHit>();
        public List<SearchHit> Sprints { get; set; } = new List<SearchHit>();
        public List<SearchHit> Epics { get; set; } = new List<SearchHit>();
        public List<SearchHit> Features { get; set; } = new List<SearchHit>();
        public List<SearchHit> Users { get; set; } = new List<SearchHit>();

        public int TotalHits => Tasks.Count + Projects.Count + Sprints.Count + Epics.Count + Features.Count + Users.Count;
    }

    public class SearchHit
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string ContextHint { get; set; } = string.Empty;
    }
}
