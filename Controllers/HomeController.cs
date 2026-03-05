using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        if (User.Identity is { IsAuthenticated: true })
        {
            if (User.IsInRole("Employee") && !User.IsInRole("Manager") && !User.IsInRole("Project Lead") && !User.IsInRole("Project Coordinator"))
            {
                return RedirectToAction("Index", "TaskItems");
            }
            return RedirectToAction("Index", "Analytics");
        }
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
