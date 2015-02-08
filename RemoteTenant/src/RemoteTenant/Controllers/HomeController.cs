using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNet.Mvc;
using Blacklite.Framework.Multitenancy;

namespace RemoteTenant.Controllers
{
    public class HomeController : Controller
    {
        private readonly ITenant _registry;
        public HomeController(ITenant registry)
        {
            _registry = registry;
        }
        public IActionResult Index()
        {
            return View(null, _registry.Id);
        }

        public IActionResult About()
        {
            ViewBag.Message = "Your application description page.";

            return View();
        }

        public IActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }

        public IActionResult Error()
        {
            return View("~/Views/Shared/Error.cshtml");
        }
    }
}