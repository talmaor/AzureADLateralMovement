using System;
using System.Collections.Generic;
using System.IO;
using System.Web;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Newtonsoft.Json;
using Nito.AspNetBackgroundTasks;

namespace AzureActiveDirectoryApplication
{
    public class MvcApplication : HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            var azurePermissions = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(
                File.ReadAllText(
                    Server.MapPath("~/App_Data/AzurePermissions.json")));

            Application.Lock();
            Application["AzureDictionaryRolesToPermissionsMapping"] = azurePermissions;
            Application.UnLock();
        }
    }
}