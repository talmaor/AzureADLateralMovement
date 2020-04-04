/* 
*  Copyright (c) Microsoft. All rights reserved. Licensed under the MIT license. 
*  See LICENSE in the source repository root for complete license information. 
*/

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using AzureActiveDirectoryApplication.Models;
using AzureActiveDirectoryApplication.Utils;
using AzureAdLateralMovement.Helpers;
using AzureAdLateralMovement.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;

namespace AzureAdLateralMovement.Controllers
{
    public class HomeController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IHostingEnvironment _env;
        private readonly IGraphSdkHelper _graphSdkHelper;

        public HomeController(IConfiguration configuration, IHostingEnvironment hostingEnvironment,
            IGraphSdkHelper graphSdkHelper)
        {
            _configuration = configuration;
            _env = hostingEnvironment;
            _graphSdkHelper = graphSdkHelper;
        }

        [AllowAnonymous]
        // Load user's profile.
        public async Task<IActionResult> Index(string email)
        {
            if (User.Identity.IsAuthenticated)
            {
                // Get users's email.
                email = email ?? User.FindFirst("preferred_username")?.Value;
                ViewData["Email"] = email;
            }

            return View();
        }

        [Authorize]
        public async Task<IActionResult> AzureAdLateralMovement()
        {
            var tenantId = ((ClaimsIdentity) User.Identity)
                .FindFirst("http://schemas.microsoft.com/identity/claims/tenantid").Value;
            await CosmosDbHelper.InitializeCosmosDb(tenantId);

            var graphClient = _graphSdkHelper.GetAuthenticatedClient((ClaimsIdentity) User.Identity);

            var azureActiveDirectoryHelper = new AzureActiveDirectoryHelper(graphClient, HttpContext);

            List<string> lateralMovementDataList = null;
            try
            {
                lateralMovementDataList = await azureActiveDirectoryHelper.RunAzureActiveDirectoryApplication();
            }
            catch (ServiceException e)
            {
                if (e.Error.Code == "TokenNotFound")
                {
                    foreach (var cookie in Request.Cookies.Keys) Response.Cookies.Delete(cookie);
                    return RedirectToAction(nameof(Index), "Home");
                }
            }
            catch (Exception e)
            {
                return RedirectToAction(nameof(Index), "Home");
            }

            return View(lateralMovementDataList);
        }

        [AllowAnonymous]
        public IActionResult Error()
        {
            return View();
        }

        public IActionResult RedirectToGraph()
        {
            if (User.Identity.IsAuthenticated)
            {
                var tenantId = ((ClaimsIdentity) User.Identity)
                    .FindFirst("http://schemas.microsoft.com/identity/claims/tenantid").Value;

                return Redirect($"http://azureadlateralmovementgraphexplorer.azurewebsites.net?tenantId={tenantId}");
            }

            return null;
        }
    }
}