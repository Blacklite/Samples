using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNet.Mvc;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteTenant.Controllers
{
    public class TenantController : Controller
    {
        private static ConcurrentDictionary<string, bool> _tenants = new ConcurrentDictionary<string, bool>();

        public IActionResult Index()
        {
            Task.Delay(2000).Wait();
            return Json(_tenants.ToDictionary(x => x.Key, x => x.Value));
        }

        public IActionResult Add(string id, bool enabled = true)
        {
            var success = _tenants.TryAdd(id, true);

            return Json(new { Success = success });
        }

        public IActionResult Remove(string id)
        {
            bool success;
            _tenants.TryRemove(id, out success);

            return Json(new { Success = success });
        }
    }
}