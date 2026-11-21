using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dairy_Management_System.Controllers
{
    /// <summary>
    /// Help & Support Controller providing role-aware operational manuals,
    /// architectural guidance, runsheet FAQs, and supervisor escalation contacts.
    /// Accessible to all authenticated platform users.
    /// </summary>
    [Authorize]
    public class HelpSupportController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            bool isAdmin = User.IsInRole("Admin");
            ViewBag.IsAdmin = isAdmin;
            return View();
        }
    }
}
