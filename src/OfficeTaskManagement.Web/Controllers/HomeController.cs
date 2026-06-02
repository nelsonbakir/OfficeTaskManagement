using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OfficeTaskManagement.Models;
using System.Threading.Tasks;
using OfficeTaskManagement.Services.Authorization;

namespace OfficeTaskManagement.Controllers;

public class HomeController : Controller
{
    public async Task<IActionResult> Index([FromServices] OfficeTaskManagement.Services.Authorization.IPermissionService permSvc)
    {
        if (User.Identity is { IsAuthenticated: true })
        {
            var isManagerOrLead = await permSvc.HasPermissionAsync(User, Permissions.StrategicView) ||
                                  await permSvc.HasPermissionAsync(User, Permissions.WorkflowManage) ||
                                  await permSvc.HasPermissionAsync(User, Permissions.ProjectsManage);

            if (!isManagerOrLead)
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
