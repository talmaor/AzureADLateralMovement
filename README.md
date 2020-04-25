## What is AzureADLateralMovement


AzureADLateralMovement allows to build Lateral Movement graph for Azure Active Directory entities - Users, Computers, Groups and Roles.
Using the Microsoft Graph API AzureADLateralMovement extracts interesting information and builds json files containing 
lateral movement graph data compatible with Bloodhound 2.2.0

Some of the  implemented features are :
* Extraction of Users, Computers, Groups, Roles and more.
* Transform the entities to Graph objects
* Inject the object to Azure CosmosDB Graph

![](/images/LMP.png)
Explanation: Terry Jeffords is a member of Device Administrators.
This group is admin on all the AAD joined machines including Desktop-RGR29LI 
Where the user Amy Santiago has logged-in in the last 2 hours and probably still has a session. 
This attack path can be exploited manually or by automated tools.

## Architecture

### The toolkit consists of several components
### MicrosoftGraphApi Helper
The MicrosoftGraphApi Helper is responsible for retrieving the required data from Graph API
### BloodHound Helper
Responsible for creating json files that can dropped on BloodHound 2.2.0 to extend the organization covered entities
### CosmosDbGraph Helper
In case you prefer using the Azure CosmosDb service instead of the BloodHound client, this module will push the data retrieved into a graph database service


## How to set up

### Steps

1. Download, compile and run
2. Browse to http://localhost:44302
3. Logon with AAD administrative account
4. Click on "AzureActiveDirectoryLateralMovement" to retrive data
5. Drag the json file into BloodHound 2.2.0 

![](/images/DragAndDrop.png)

### Configuration

An example configuration as below :
```
{
  "AzureAd": {
    "CallbackPath": "/signin-oidc",
    "BaseUrl": "https://localhost:44334",
    "Scopes": "Directory.Read.All AuditLog.Read.All",
    "ClientId": "<ClientId>",
    "ClientSecret": "<ClientSecret>",
    "GraphResourceId": "https://graph.microsoft.com/",
    "GraphScopes": "Directory.Read.All AuditLog.Read.All"
  },
  "Logging": {
    "IncludeScopes": false,
    "LogLevel": {
      "Default": "Warning"
    }
  },
  "CosmosDb": {
    "EndpointUrl": "https://<CosmosDbGraphName>.documents.azure.com:443/",
    "AuthorizationKey": "<AuthorizationKey>"
  } 
}
```

### Deployment
Before start using this tool you need to create an Application on the Azure Portal.
Go to Azure Active Directory -> App Registrations -> Register an application.

![](/images/registerapp.png)

After creating the application, copy the Application ID and change it on ```AzureOauth.config```.

The URL(external listener) that will be used for the application should be added as a Redirect URL.
To add a redirect url, go the application and click Add a Redirect URL.

![](/images/redirecturl.png)

The Redirect URL should be the URL that will be used to host the application endpoint, in this case ```https://localhost:44302/```

![](/images/url.png)

Make sure to check both the boxes as shown below :

![](/images/implicitgrant.png)


##  Security Considerations

The lateral movement graph allows investigate available attack paths truly available in the AAD environment.
The graph is combined by Nodes of Users, Groups and Devices, where the edges are connecting them by the logic of �AdminTo�, �MemberOf� and �HasSession� 
This logic is explained in details by the original research document: https://github.com/BloodHoundAD/Bloodhound/wiki

In the on-premise environment BloodHound collects data using SMAR and SMB protocols to each machine in the domain, and LDAP to the on-premise AD.  

In Azure AD environment, the relevant data regarding Azure AD device, users and logon sessions can be retrieved using Microsoft Graph API. 
Once the relevant data is gathered it is possible to build similar graph of connections for users, groups and Windows machines registered in the Azure Active Directory. 

To retrive the data and build the graph data this project uses:
Azure app 
Microsoft Graph API
Hybrid AD+AAD domain environment synced using pass-through authentication
BloodHound UI and entities objects 

![](/images/implicitgrant.png)

## The AAD graph is based on the following data 

Devices - AAD joined Windows devices only and their owner's

Users - All AD or AAD users

Administrative roles and Groups - All memberships of roles and groups

Local Admin - The following are default local admins in AAD joined device 
	- Global administrator role 
	- Device administrator role 
	- The owner of the machine

Sessions - All logins for Windows machines 

## References

Exploring graph queries on top of Azure Cosmos DB with Gremlin https://github.com/talmaor/GraphExplorer
SharpHound - The C# Ingestor https://github.com/BloodHoundAD/BloodHound/wiki/Data-Collector
Quickstart: Build a .NET Framework or Core application using the Azure Cosmos DB Gremlin API account https://docs.microsoft.com/en-us/azure/cosmos-db/create-graph-dotnet
How to: Use the portal to create an Azure AD application and service principal that can access resources https://docs.microsoft.com/en-us/azure/active-directory/develop/howto-create-service-principal-portal
