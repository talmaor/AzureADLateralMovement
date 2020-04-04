using System;
using System.Collections.Generic;
using System.Linq;
using AzureActiveDirectoryApplication.Models;
using AzureActiveDirectoryApplication.Models.BloodHound;
using AzureAdLateralMovement.Models.BloodHound;
using AzureAdLateralMovement.Utils;
using Microsoft.Azure.CosmosDB.BulkExecutor.Graph.Element;
using Microsoft.Graph;
using MoreLinq.Extensions;
using Application = Microsoft.Graph.Application;
using DirectoryRole = Microsoft.Graph.DirectoryRole;
using Domain = AzureActiveDirectoryApplication.Models.BloodHound.Domain;
using Group = Microsoft.Graph.Group;
using User = Microsoft.Graph.User;

namespace AzureActiveDirectoryApplication.Utils
{
    public static class CosmosDbGraphHelper
    {
        private static readonly Dictionary<string, List<string>> AzureDictionaryRolesToPermissionsMapping;

        private static void TryGetPermissions(DirectoryRole _, out List<string> permissions)
        {
            if (_?.DisplayName == null || AzureDictionaryRolesToPermissionsMapping == null)
                permissions = null;
            else
                AzureDictionaryRolesToPermissionsMapping.TryGetValue(_.DisplayName, out permissions);
        }

        public static void GroupMembership<T>(Group _,  List<T> groupMembers) where T : GroupMember
        {
            try
            {
                var gremlinVertices = new List<GremlinVertex>();
                var gremlinEdges = new List<GremlinEdge>();

                var vertex = new GremlinVertex(_.Id, nameof(AzureAdLateralMovement.Models.BloodHound.Group));
                vertex.AddProperty(CosmosDbHelper.CollectionPartitionKey, _.Id.GetHashCode());
                vertex.AddProperty(nameof(_.DisplayName), _.DisplayName?.ToUpper() ?? string.Empty);
                gremlinVertices.Add(vertex);

                groupMembers.ForEach(member =>
                {
                    var gremlinEdge = new GremlinEdge(
                        _.Id + member.Id,
                        member is  GroupOwner ? "Owner" : "MemberOf",
                        member.Id,
                        _.Id,
                        member.Type,
                        nameof(AzureAdLateralMovement.Models.BloodHound.Group),
                        member.Id.GetHashCode(),
                        _.Id.GetHashCode());

                    gremlinEdges.Add(gremlinEdge);
                });

                CosmosDbHelper.RunImportVerticesBlock.Post(gremlinVertices);
                CosmosDbHelper.RunImportEdgesBlock.Post(gremlinEdges);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public static void DirectoryRoleMembership(DirectoryRole _,
            List<GroupMember> members)
        {
            try
            {
                var gremlinVertices = new List<GremlinVertex>();
                var gremlinEdges = new List<GremlinEdge>();

                var vertex = new GremlinVertex(_.Id, nameof(DirectoryRole));
                vertex.AddProperty(CosmosDbHelper.CollectionPartitionKey, _.Id.GetHashCode());
                vertex.AddProperty(nameof(_.DisplayName), _.DisplayName?.ToUpper() ?? string.Empty);

                gremlinVertices.Add(vertex);

                members.ForEach(member =>
                {
                    gremlinEdges.Add(new GremlinEdge(
                        _.Id + member.Id,
                        "MemberOf",
                        member.Id,
                        _.Id,
                        nameof(User),
                        nameof(DirectoryRole),
                        member.Id.GetHashCode(),
                        _.Id.GetHashCode()));
                });

                CosmosDbHelper.RunImportVerticesBlock.Post(gremlinVertices);
                CosmosDbHelper.RunImportEdgesBlock.Post(gremlinEdges);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public static void DirectoryRolePermissions(DirectoryRole _,
            List<string> userIds,
            HashSet<string> administrators)
        {
            try
            {
                var gremlinEdges = new List<GremlinEdge>();
                TryGetPermissions(_, out var permissions);

                if (permissions?.Contains("microsoft.aad.directory/users/password/update") == true)
                    userIds.Where(userId => !administrators.Contains(userId)).ForEach(
                        userId =>
                            gremlinEdges.Add(
                                new GremlinEdge(
                                    _.Id + userId,
                                    "ForceChangePassword",
                                    _.Id,
                                    userId,
                                    nameof(AzureAdLateralMovement.Models.BloodHound.DirectoryRole),
                                    nameof(User),
                                    _.Id.GetHashCode(),
                                    userId.GetHashCode()
                                )));

                CosmosDbHelper.RunImportEdgesBlock.Post(gremlinEdges);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public static void DeviceOwners(Device _,
            List<DirectoryObject> ownerList,
            List<DirectoryRole> directoryRoles)
        {
            try
            {
                var gremlinVertices = new List<GremlinVertex>();
                var gremlinEdges = new List<GremlinEdge>();
                var deviceOwnerGroups =
                    directoryRoles.Where(__ =>
                        AzureActiveDirectoryHelper.DeviceOwnerGroupDisplayNames.Contains(__.DisplayName));
                var vertex = new GremlinVertex(_.Id, nameof(Computer));

                vertex.AddProperty(CosmosDbHelper.CollectionPartitionKey, _.Id.GetHashCode());
                vertex.AddProperty(nameof(_.DisplayName), _.DisplayName?.ToUpper() ?? string.Empty);
                gremlinVertices.Add(vertex);
                ownerList.ForEach(__ =>
                {
                    var user = (User) __;
                    var gremlinEdge = new GremlinEdge(
                        user.Id + _.Id,
                        "AdminTo",
                        user.Id,
                        _.Id,
                        nameof(User),
                        nameof(Computer),
                        user.Id.GetHashCode(),
                        _.Id.GetHashCode());

                    gremlinEdges.Add(gremlinEdge);
                });
                deviceOwnerGroups.ForEach(directoryRole =>
                {
                    var gremlinEdge = new GremlinEdge(
                        directoryRole.Id + _.Id,
                        "AdminTo",
                        directoryRole.Id,
                        _.Id,
                        nameof(DirectoryRole),
                        nameof(Computer),
                        directoryRole.Id.GetHashCode(),
                        _.Id.GetHashCode());

                    gremlinEdges.Add(gremlinEdge);
                });

                CosmosDbHelper.RunImportVerticesBlock.Post(gremlinVertices);
                CosmosDbHelper.RunImportEdgesBlock.Post(gremlinEdges);
            }
            catch (ClientException ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public static void InteractiveLogOns(InteractiveLogon _,
            Dictionary<string, string> deviceObjectIdToDeviceId)
        {
            try
            {
                var gremlinEdges = new List<GremlinEdge>();
                deviceObjectIdToDeviceId.TryGetValue(_.DeviceId, out var deviceId);
                if (deviceId == null) return;

                var gremlinEdge = new GremlinEdge(
                    deviceId + _.UserId,
                    "HasSession",
                    deviceId,
                    _.UserId,
                    nameof(Computer),
                    nameof(User),
                    deviceId.GetHashCode(),
                    _.UserId.GetHashCode());

                gremlinEdges.Add(gremlinEdge);

                CosmosDbHelper.RunImportEdgesBlock.Post(gremlinEdges);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public static void Users(User user)
        {
            try
            {
                var gremlinVertices = new List<GremlinVertex>();
                var userVertex = new GremlinVertex(user.Id, nameof(User));
                userVertex.AddProperty(CosmosDbHelper.CollectionPartitionKey, user.Id.GetHashCode());
                userVertex.AddProperty(nameof(user.UserPrincipalName), user.UserPrincipalName ?? string.Empty);
                userVertex.AddProperty(nameof(user.Mail), user.Mail ?? string.Empty);
                userVertex.AddProperty(nameof(user.DisplayName), user.DisplayName?.ToUpper() ?? string.Empty);
                gremlinVertices.Add(userVertex);

                CosmosDbHelper.RunImportVerticesBlock.Post(gremlinVertices);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public static void AppOwnership(Application app,
            List<DirectoryObject> owners, string applicationAdministratorRoleId = "534a2975-e5b9-4ea1-b7da-deaec0a7c0aa")
        {
            try
            {
                var gremlinVertices = new List<GremlinVertex>();
                var gremlinEdges = new List<GremlinEdge>();

                var vertex = new GremlinVertex(app.AppId, nameof(Models.BloodHound.Application));
                vertex.AddProperty(CosmosDbHelper.CollectionPartitionKey, app.AppId.GetHashCode());
                vertex.AddProperty(nameof(app.DisplayName), app.DisplayName?.ToUpper() ?? string.Empty);
                gremlinVertices.Add(vertex);

                owners.ForEach(owner =>
                {
                    var gremlinEdge = new GremlinEdge(
                        owner.Id + app.AppId,
                        "Owner",
                        owner.Id,
                        app.AppId,
                        nameof(User),
                        nameof(Models.BloodHound.Application),
                        owner.Id.GetHashCode(),
                        app.AppId.GetHashCode());

                    gremlinEdges.Add(gremlinEdge);
                });

                var gremlinEdge2 = new GremlinEdge(
                    applicationAdministratorRoleId + app.AppId,
                    "Owner",
                    applicationAdministratorRoleId,
                    app.AppId,
                    nameof(DirectoryRole),
                    nameof(Models.BloodHound.Application),
                    applicationAdministratorRoleId.GetHashCode(),
                    app.AppId.GetHashCode()
                );

                gremlinEdges.Add(gremlinEdge2);

                CosmosDbHelper.RunImportVerticesBlock.Post(gremlinVertices);
                CosmosDbHelper.RunImportEdgesBlock.Post(gremlinEdges);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public static void Applications(
            string appDisplayName,
            string appId,
            HashSet<string> permissionsSet,
            HashSet<string> userIdSet,
            string principalId,
            string homePage,
            string appOwnerOrganizationId)
        {
            try
            {
                var gremlinVertices = new List<GremlinVertex>();
                var gremlinEdges = new List<GremlinEdge>();

                var vertex = new GremlinVertex(appId, nameof(Models.BloodHound.Application));
                vertex.AddProperty(CosmosDbHelper.CollectionPartitionKey, appId.GetHashCode());
                vertex.AddProperty(nameof(Application.DisplayName), appDisplayName?.ToUpper() ?? string.Empty);
                vertex.AddProperty(nameof(permissionsSet), permissionsSet.ToDelimitedString(",") ?? string.Empty);
                vertex.AddProperty(nameof(principalId), principalId ?? string.Empty);
                vertex.AddProperty(nameof(homePage), homePage ?? string.Empty);
                vertex.AddProperty(nameof(appOwnerOrganizationId), appOwnerOrganizationId ?? string.Empty);
                gremlinVertices.Add(vertex);

                if (appOwnerOrganizationId != null)
                {
                    var vertexDomain = new GremlinVertex(appOwnerOrganizationId, nameof(Models.BloodHound.Domain));
                    vertexDomain.AddProperty(CosmosDbHelper.CollectionPartitionKey, appOwnerOrganizationId.GetHashCode());
                    vertexDomain.AddProperty(nameof(Application.DisplayName), appOwnerOrganizationId.ToUpper());
                    gremlinVertices.Add(vertexDomain);

                    gremlinEdges.Add(
                        new GremlinEdge(
                            appOwnerOrganizationId + appId,
                            "Owner",
                            appOwnerOrganizationId,
                            appId,
                            nameof(Models.BloodHound.Domain),
                            nameof(Application),
                            appOwnerOrganizationId.GetHashCode(),
                            appId.GetHashCode()
                        ));
                }
                

                if (permissionsSet.Any(_ =>
                    string.Equals(_, "Directory.AccessAsUser.All", StringComparison.OrdinalIgnoreCase)))
                    
                    userIdSet.ForEach(userId =>
                        gremlinEdges.Add(
                            new GremlinEdge(
                                appId + userId,
                                "AccessAsUser",
                                appId,
                                userId,
                                nameof(Models.BloodHound.Application),
                                nameof(User),
                                appId.GetHashCode(),
                                userId.GetHashCode()
                            ))
                    );


                CosmosDbHelper.RunImportVerticesBlock.Post(gremlinVertices);
                CosmosDbHelper.RunImportEdgesBlock.Post(gremlinEdges);

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}