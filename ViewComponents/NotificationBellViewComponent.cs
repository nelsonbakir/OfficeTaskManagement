using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using System.Linq;
using System.Threading.Tasks;

namespace OfficeTaskManagement.ViewComponents
{
    public class NotificationBellViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;

        public NotificationBellViewComponent(ApplicationDbContext context, UserManager<User> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var user = await _userManager.GetUserAsync(HttpContext.User);
            if (user == null)
            {
                return View("Default", new NotificationViewModel { UnreadCount = 0 });
            }

            var unreadCount = await _context.Notifications
                .Where(n => n.UserId == user.Id && !n.IsRead)
                .CountAsync();

            var latestNotifications = await _context.Notifications
                .Where(n => n.UserId == user.Id)
                .OrderByDescending(n => n.CreatedAt)
                .Take(5)
                .ToListAsync();

            var model = new NotificationViewModel
            {
                UnreadCount = unreadCount,
                Notifications = latestNotifications
            };

            return View("Default", model);
        }
    }

    public class NotificationViewModel
    {
        public int UnreadCount { get; set; }
        public System.Collections.Generic.List<Notification> Notifications { get; set; } = new();
    }
}
