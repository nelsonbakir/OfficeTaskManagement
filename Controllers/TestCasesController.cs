using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services;
using OfficeTaskManagement.ViewModels;

namespace OfficeTaskManagement.Controllers
{
    [Authorize]
    public class TestCasesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IMediaService _mediaService;

        public TestCasesController(ApplicationDbContext context, IMediaService mediaService)
        {
            _context = context;
            _mediaService = mediaService;
        }

        // GET: TestCases
        public async Task<IActionResult> Index()
        {
            var applicationDbContext = _context.TestCases.Include(t => t.UserStory);
            return View(await applicationDbContext.ToListAsync());
        }

        // GET: TestCases/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var testCase = await _context.TestCases
                .Include(t => t.UserStory)
                .Include(t => t.Attachments).ThenInclude(a => a.UploadedBy)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (testCase == null)
            {
                return NotFound();
            }

            return View(testCase);
        }

        // GET: TestCases/Create
        public IActionResult Create(int? userStoryId)
        {
            ViewData["UserStoryId"] = new SelectList(_context.UserStories, "Id", "Title", userStoryId);
            return View(new TestCaseViewModel { TestCase = new TestCase { UserStoryId = userStoryId ?? 0 } });
        }

        // POST: TestCases/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TestCaseViewModel vm)
        {
            if (ModelState.IsValid)
            {
                _context.Add(vm.TestCase);
                await _context.SaveChangesAsync();

                // Handle Attachments
                if (vm.Attachments != null && vm.Attachments.Any())
                {
                    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    foreach (var file in vm.Attachments)
                    {
                        using (var stream = file.OpenReadStream())
                        {
                            var filePath = await _mediaService.UploadAsync(stream, file.FileName, file.ContentType);
                            _context.Attachments.Add(new Attachment
                            {
                                TestCaseId = vm.TestCase.Id,
                                FileName = file.FileName,
                                FilePath = filePath,
                                UploadedById = userId,
                                UploadedAt = DateTime.UtcNow
                            });
                        }
                    }
                    await _context.SaveChangesAsync();
                }

                return RedirectToAction(nameof(Details), new { id = vm.TestCase.Id });
            }
            ViewData["UserStoryId"] = new SelectList(_context.UserStories, "Id", "Title", vm.TestCase.UserStoryId);
            return View(vm);
        }

        // GET: TestCases/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var testCase = await _context.TestCases
                .Include(t => t.Attachments).ThenInclude(a => a.UploadedBy)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (testCase == null)
            {
                return NotFound();
            }
            ViewData["UserStoryId"] = new SelectList(_context.UserStories, "Id", "Title", testCase.UserStoryId);
            return View(new TestCaseViewModel { TestCase = testCase });
        }

        // POST: TestCases/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, TestCaseViewModel vm)
        {
            if (id != vm.TestCase.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(vm.TestCase);
                    
                    // Handle new attachments
                    if (vm.Attachments != null && vm.Attachments.Any())
                    {
                        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                        foreach (var file in vm.Attachments)
                        {
                            using (var stream = file.OpenReadStream())
                            {
                                var filePath = await _mediaService.UploadAsync(stream, file.FileName, file.ContentType);
                                _context.Attachments.Add(new Attachment
                                {
                                    TestCaseId = vm.TestCase.Id,
                                    FileName = file.FileName,
                                    FilePath = filePath,
                                    UploadedById = userId,
                                    UploadedAt = DateTime.UtcNow
                                });
                            }
                        }
                    }

                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!TestCaseExists(vm.TestCase.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Details), new { id = vm.TestCase.Id });
            }
            ViewData["UserStoryId"] = new SelectList(_context.UserStories, "Id", "Title", vm.TestCase.UserStoryId);
            return View(vm);
        }

        // GET: TestCases/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var testCase = await _context.TestCases
                .Include(t => t.UserStory)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (testCase == null)
            {
                return NotFound();
            }

            return View(testCase);
        }

        // POST: TestCases/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var testCase = await _context.TestCases.FindAsync(id);
            if (testCase != null)
            {
                _context.TestCases.Remove(testCase);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // POST: TestCases/DeleteAttachment/5
        [HttpPost]
        public async Task<IActionResult> DeleteAttachment(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var attachment = await _context.Attachments.FindAsync(id);
            if (attachment == null) return NotFound();

            var testCaseId = attachment.TestCaseId;
            if (testCaseId == null) return BadRequest("Attachment is not linked to a test case.");
            
            // Access check
            if (attachment.UploadedById != userId && !User.IsInRole("Manager") && !User.IsInRole("Project Lead"))
            {
                return Forbid();
            }

            await _mediaService.DeleteAsync(attachment.FilePath);
            _context.Attachments.Remove(attachment);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Edit), new { id = testCaseId.Value });
        }

        private bool TestCaseExists(int id)
        {
            return _context.TestCases.Any(e => e.Id == id);
        }
    }
}
