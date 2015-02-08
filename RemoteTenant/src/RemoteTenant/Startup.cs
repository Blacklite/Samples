using System;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Diagnostics;
using Microsoft.AspNet.Hosting;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Routing;
using Microsoft.AspNet.Security.Cookies;
using Microsoft.Data.Entity;
using Microsoft.Framework.ConfigurationModel;
using Microsoft.Framework.DependencyInjection;
using Microsoft.Framework.Logging;
using Microsoft.Framework.Logging.Console;
using RemoteTenant.Models;
using Blacklite.Framework.Multitenancy.Http;
using Autofac;
using Blacklite.Framework.Multitenancy;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net;
using Newtonsoft.Json;
using System.Linq;
using System.Reactive.Concurrency;
using System.Collections.Concurrent;
using System.Threading;
using System.Net.Http;

namespace RemoteTenant
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            // Setup configuration sources.
            Configuration = new Configuration()
                .AddJsonFile("config.json")
                .AddEnvironmentVariables();
        }

        public IConfiguration Configuration { get; set; }

        // This method gets called by the runtime.
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<ITenantIdentificationStrategy, PathTenantIdentificationStrategy>();
            services.AddApplicationOnlySingleton<ITenantRegistry, RemoteTenantRegistry>();
            services.AddInstance(Configuration);

            // Add MVC services to the services container.
            services
                .AddMvc()
                .AddMultitenancy();

            // Uncomment the following line to add Web API servcies which makes it easier to port Web API 2 controllers.
            // You need to add Microsoft.AspNet.Mvc.WebApiCompatShim package to project.json
            // services.AddWebApiConventions();
            return new ContainerBuilder()
                .Populate(services)
                .BuildMultitenancy();
        }

        // Configure is called after ConfigureServices is called.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerfactory)
        {
            // Configure the HTTP request pipeline.
            // Add the console logger.
            loggerfactory.AddConsole();

            // Add the following to the request pipeline only in development environment.
            if (string.Equals(env.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase))
            {
                app.UseErrorPage(ErrorPageOptions.ShowAll);
            }
            else
            {
                // Add Error handling middleware which catches all application specific errors and
                // send the request to the following path or controller action.
                app.UseErrorHandler("/Home/Error");
            }

            app.UseMultitenancy();

            // Add static files to the request pipeline.
            app.UseStaticFiles();

            // Add cookie-based authentication to the request pipeline.

            // Add MVC to the request pipeline.
            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller}/{action}/{id?}",
                    defaults: new { controller = "Home", action = "Index" });

                // Uncomment the following line to add a route for porting Web API 2 controllers.
                // routes.MapWebApiRoute("DefaultApi", "api/{controller}/{id?}");
            });



            // warmup
            app.ApplicationServices.GetService<ITenantRegistry>();
        }
    }

    public class RemoteTenantRegistry : ITenantRegistry, IDisposable
    {
        private readonly ConcurrentDictionary<string, ITenantRegistryItem> _items = new ConcurrentDictionary<string, ITenantRegistryItem>();
        private readonly string _tenantUrl;
        private readonly IList<IDisposable> _disposables = new List<IDisposable>();
        private readonly ILogger _logger;

        public RemoteTenantRegistry(IConfiguration configuration, ILoggerFactory factory)
        {
            Task.Run(() => RefreshTenantRegistry(100));
            var schedule = Scheduler.SchedulePeriodic(Scheduler.Default, new TimeSpan(0, 3, 0), async () => await RefreshTenantRegistry());
            _disposables.Add(schedule);
            _tenantUrl = configuration.Get("Tenant:Registry:Url");
            _logger = factory.Create("RemoteTenantRegistry");
        }

        public IEnumerable<ITenantRegistryItem> GetTenants()
        {
            var task = GetTenantsAsync();
            task.Wait();
            return task.Result;
        }

        private async Task RefreshTenantRegistry(int timeout = 1000)
        {
            var wc = new HttpClient();
            //wc.BaseAddress = _tenantUrl;

            string json;
            try
            {
                using (var cts = new CancellationTokenSource(timeout))
                {
                    using (var response = await wc.GetAsync(_tenantUrl, cts.Token))
                    {
                        json = await response.Content.ReadAsStringAsync();
                    }
                }
            }
            catch (TaskCanceledException ex)
            {
                // Fall over to another backup, perhaps local copy on disk, local list of tenants, etc.
                json = @"{}";
                _logger.WriteCritical($"Remote tenant repository failed to respond after {new TimeSpan(0, 0, 0, 0, timeout).TotalSeconds} seconds.");
            }

            var tenantRegistry = JsonConvert.DeserializeObject<IDictionary<string, bool>>(json);

            var newItems = tenantRegistry.Except(_items.Join(tenantRegistry, x => string.Join(":", x.Key, x.Value), x => string.Join(":", x.Key, x.Value), (a, b) => b));
            var deletedItems = _items.Except(tenantRegistry.Join(_items, x => x.Key, x => x.Key, (a, b) => b)).Select(x => x.Key);

            foreach (var item in newItems)
            {
                _items.AddOrUpdate(item.Key, x => new TenantRegistryItem(item.Key, item.Value), (x, old) => new TenantRegistryItem(item.Key, item.Value));
            }

            ITenantRegistryItem dummy;
            foreach (var item in deletedItems)
            {
                _items.TryRemove(item, out dummy);
            }
        }

        public async Task<IEnumerable<ITenantRegistryItem>> GetTenantsAsync()
        {
            return _items.Values.Where(x => x.IsEnabled);
        }

        public ITenantRegistryItem GetTenantItem(string tenantId)
        {
            var task = GetTenantItemAsync(tenantId);
            task.Wait();
            return task.Result;
        }

        public async Task<ITenantRegistryItem> GetTenantItemAsync(string tenantId)
        {
            ITenantRegistryItem item;
            if (!_items.TryGetValue(tenantId, out item))
            {
                await RefreshTenantRegistry(100);

                if (!_items.TryGetValue(tenantId, out item))
                {
                    item = new TenantRegistryItem(tenantId, false);
                    _items.TryAdd(tenantId, item);
                }
            }

            return item;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).          
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                foreach (var disposable in _disposables)
                    disposable.Dispose();

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources. 
        // ~RemoteTenantRegistry() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
