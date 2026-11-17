using System.Diagnostics;
using Dairy_Management_System.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dairy_Management_System.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Admin Dashboard endpoint. Strictly protected for staff accounts with Admin role.
        /// </summary>
        [Authorize(Roles = "Admin")]
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Delivery Boy Portal endpoint. Strictly protected for staff accounts with DeliveryBoy role.
        /// </summary>
        [Authorize(Roles = "DeliveryBoy")]
        public IActionResult Privacy()
        {
            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
