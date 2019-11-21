// Copyright (c) Microsoft. All rights reserved. Licensed under the MIT license. See LICENSE.txt in the project root for license information.

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using AzureActiveDirectoryApplication.TokenStorage;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Cookies;
using Microsoft.Owin.Security.OpenIdConnect;

#region BloodHountUsing

using static AzureActiveDirectoryApplication.Models.Extensions;

#endregion

namespace AzureActiveDirectoryApplication.Controllers
{
    public class HomeController : Controller
    {
        #region AzureADLateralMovement

        public async Task<ActionResult> AzureActiveDirectoryLateralMovement()
        {
            var azureActiveDirectoryApplication = new Models.AzureActiveDirectoryApplication(HttpContext);
            await azureActiveDirectoryApplication.RunAzureActiveDirectoryApplication();

            return View(new List<string>
            {
                Startup.OutputFolderLocation + nameof(azureActiveDirectoryApplication.DeviceOwners),
                Startup.OutputFolderLocation + nameof(azureActiveDirectoryApplication.DirectoryRoles),
                Startup.OutputFolderLocation + nameof(azureActiveDirectoryApplication.Domains),
                Startup.OutputFolderLocation + nameof(azureActiveDirectoryApplication.Groups),
                Startup.OutputFolderLocation + nameof(azureActiveDirectoryApplication.Users),
                Startup.OutputFolderLocation + nameof(azureActiveDirectoryApplication.InteractiveLogOns),
                Startup.OutputFolderLocation + nameof(azureActiveDirectoryApplication.AppSignIns)
            });
        }

        #endregion

        #region AppLogin

        public ActionResult Index()
        {
            if (Request.IsAuthenticated)
            {
                if (ClaimsPrincipal.Current.FindFirst("aud").Value != Startup.AppId)
                {
                    return View();
                }

                var userName = ClaimsPrincipal.Current.FindFirst("name").Value;
                var userId = ClaimsPrincipal.Current.FindFirst(ClaimTypes.NameIdentifier).Value;
                if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(userId)) return RedirectToAction("SignOut");

                // Since we cache tokens in the session, if the server restarts
                // but the browser still has a cached cookie, we may be
                // authenticated but not have a valid token cache. Check for this
                // and force signout.
                var tokenCache = new SessionTokenCache(userId, HttpContext);
                if (!tokenCache.HasData()) return RedirectToAction("SignOut");

                ViewBag.UserName = userName;
            }

            return View();
        }

        public void SignIn()
        {
            if (!Request.IsAuthenticated || 
                ClaimsPrincipal.Current.FindFirst("aud").Value != Startup.AppId)
            {
                HttpContext.GetOwinContext().Authentication.Challenge(
                    new AuthenticationProperties {RedirectUri = "/"},
                    OpenIdConnectAuthenticationDefaults.AuthenticationType);
            }
        }

        public void SignOut()
        {
            if (Request.IsAuthenticated &&
                ClaimsPrincipal.Current.FindFirst("aud").Value == Startup.AppId)
            {
                var userId = ClaimsPrincipal.Current.FindFirst(ClaimTypes.NameIdentifier).Value;

                if (!string.IsNullOrEmpty(userId))
                {
                    // Get the user's token cache and clear it
                    var tokenCache = new SessionTokenCache(userId, HttpContext);
                    tokenCache.Clear();
                }
            }

            // Send an OpenID Connect sign-out request. 
            HttpContext.GetOwinContext().Authentication.SignOut(
                CookieAuthenticationDefaults.AuthenticationType);
            Response.Redirect("/");
        }

        public ActionResult Error(string message, string debug)
        {
            ViewBag.Message = message;
            ViewBag.Debug = debug;
            return View("Error");
        }

        #endregion
    }
}