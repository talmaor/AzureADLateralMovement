//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Humanizer;
using Microsoft.Azure.CosmosDB.BulkExecutor;
using Microsoft.Azure.CosmosDB.BulkExecutor.BulkImport;
using Microsoft.Azure.CosmosDB.BulkExecutor.Graph;
using Microsoft.Azure.CosmosDB.BulkExecutor.Graph.Element;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using AzureAdLateralMovement;

namespace AzureAdLateralMovement.Utils
{
    public abstract class CosmosDbHelper : Module
    {
        private static string _databaseName = "TenantIdApp";
        private static readonly string CollectionName = "Entities";
        private static readonly int CollectionThroughput = 1000;
        private static Task _initializeAsyncTask;
        private static readonly DocumentClient Client;
        private static IBulkExecutor _graphBulkExecutor;
        private static bool _shouldCleanupOnStart = true;
        private static DocumentCollection _dataCollection;
        private static bool _initialized;

        public static readonly ActionBlock<IEnumerable<GremlinVertex>> RunImportVerticesBlock =
            new ActionBlock<IEnumerable<GremlinVertex>>(
                ImportVerticesAsync,
                new ExecutionDataflowBlockOptions
                {
                    BoundedCapacity = 10.Thousands(),
                    CancellationToken = CancellationToken.None,
                    MaxDegreeOfParallelism = 10
                });

        public static readonly ActionBlock<IEnumerable<GremlinEdge>> RunImportEdgesBlock =
            new ActionBlock<IEnumerable<GremlinEdge>>(
                ImportEdgesAsync,
                new ExecutionDataflowBlockOptions
                {
                    BoundedCapacity = 10.Thousands(),
                    CancellationToken = CancellationToken.None,
                    MaxDegreeOfParallelism = 1
                });

        private static readonly ConnectionPolicy ConnectionPolicy = new ConnectionPolicy
        {
            ConnectionMode = ConnectionMode.Direct,
            ConnectionProtocol = Protocol.Tcp
        };

        static CosmosDbHelper()
        {
            var endpointUrl = Startup.CosmosDbOptions.EndpointUrl;
            var authorizationKey = Startup.CosmosDbOptions.AuthorizationKey;
            Client = new DocumentClient(new Uri(endpointUrl), authorizationKey, ConnectionPolicy);
        }

        public static string CollectionPartitionKey { get; } = "pk";

        public static async Task InitializeCosmosDb(string tenantId)
        {
            _databaseName = tenantId;

            if (!_initialized)
            {
                _initialized = true;
                await ConfigureDataBase();
                _initializeAsyncTask = InitTask();
                await _initializeAsyncTask;
            }
        }

        private static async Task InitTask()
        {
            // Set retry options high for initialization (default values).
            Client.ConnectionPolicy.RetryOptions.MaxRetryWaitTimeInSeconds = 30;
            Client.ConnectionPolicy.RetryOptions.MaxRetryAttemptsOnThrottledRequests = 9;

            _graphBulkExecutor = new GraphBulkExecutor(Client, _dataCollection);
            await _graphBulkExecutor.InitializeAsync();
            Trace.TraceInformation("InitializeAsync");

            // Set retries to 0 to pass control to bulk executor.
            Client.ConnectionPolicy.RetryOptions.MaxRetryWaitTimeInSeconds = 0;
            Client.ConnectionPolicy.RetryOptions.MaxRetryAttemptsOnThrottledRequests = 0;
        }

        private static async Task ConfigureDataBase()
        {
            // Cleanup on start if set in config.
            try
            {
                if (_shouldCleanupOnStart)
                {
                    var database = GetDatabaseIfExists(Client, _databaseName);
                    if (database != null) await Client.DeleteDatabaseAsync(database.SelfLink);

                    Trace.TraceInformation("Creating database {0}", _databaseName);
                    database = await Client.CreateDatabaseAsync(new Database {Id = _databaseName});

                    Trace.TraceInformation("Creating collection {0} with {1} RU/s", CollectionName,
                        CollectionThroughput);
                    _dataCollection = await CreatePartitionedCollectionAsync(Client, _databaseName, CollectionName,
                        CollectionThroughput, CollectionPartitionKey);
                    _shouldCleanupOnStart = false;
                }
                else
                {
                    _dataCollection = GetCollectionIfExists(Client, _databaseName, CollectionName);
                    if (_dataCollection == null) throw new Exception("The data collection does not exist");
                }
            }
            catch (Exception de)
            {
                Trace.TraceError("Unable to initialize, exception message: {0}", de.Message);
                throw;
            }
        }

        private static async Task ImportVerticesAsync(IEnumerable<GremlinVertex> gremlinVertices)
        {
            Trace.TraceInformation(nameof(ImportVerticesAsync));
            var token = new CancellationTokenSource().Token;
            BulkImportResponse vResponse = null;

            while (Client == null || _graphBulkExecutor == null || _initializeAsyncTask?.IsCompleted != true)
                await Task.Delay(100.Milliseconds(), token);

            try
            {
                if (gremlinVertices != null)
                    vResponse = await _graphBulkExecutor.BulkImportAsync(
                        gremlinVertices,
                        true,
                        true,
                        null,
                        null,
                        token);
            }
            catch (DocumentClientException de)
            {
                Trace.TraceError("Document client exception: {0}", de);
            }
            catch (Exception e)
            {
                Trace.TraceError("Exception: {0}", e);
            }

            Trace.TraceInformation("END" + nameof(ImportVerticesAsync));
        }

        private static async Task ImportEdgesAsync(IEnumerable<GremlinEdge> gremlinEdge)
        {
            Trace.TraceInformation(nameof(ImportEdgesAsync));
            var token = new CancellationTokenSource().Token;

            while (Client == null || _graphBulkExecutor == null || _initializeAsyncTask?.IsCompleted != true)
                await Task.Delay(10.Milliseconds(), token);

            try
            {
                if (gremlinEdge != null)
                    await _graphBulkExecutor.BulkImportAsync(
                        gremlinEdge,
                        true,
                        true,
                        null,
                        null,
                        token);
            }
            catch (DocumentClientException de)
            {
                Trace.TraceError("Document client exception: {0}", de);
            }
            catch (Exception e)
            {
                Trace.TraceError("Exception: {0}", e);
            }

            Trace.TraceInformation("END" + nameof(ImportEdgesAsync));
        }

        private static DocumentCollection GetCollectionIfExists(DocumentClient client, string databaseName,
            string collectionName)
        {
            if (GetDatabaseIfExists(client, databaseName) == null) return null;

            return client.CreateDocumentCollectionQuery(UriFactory.CreateDatabaseUri(databaseName))
                .Where(c => c.Id == collectionName).AsEnumerable().FirstOrDefault();
        }

        private static Database GetDatabaseIfExists(DocumentClient client, string databaseName)
        {
            return client.CreateDatabaseQuery().Where(d => d.Id == databaseName).AsEnumerable().FirstOrDefault();
        }

        private static async Task<DocumentCollection> CreatePartitionedCollectionAsync(DocumentClient client,
            string databaseName,
            string collectionName, int collectionThroughput, string collectionPartitionKey)
        {
            var partitionKey = new PartitionKeyDefinition
            {
                Paths = new Collection<string> {$"/{collectionPartitionKey}"}
            };
            var collection = new DocumentCollection {Id = collectionName, PartitionKey = partitionKey};

            try
            {
                collection = await client.CreateDocumentCollectionAsync(
                    UriFactory.CreateDatabaseUri(databaseName),
                    collection,
                    new RequestOptions {OfferThroughput = collectionThroughput});
            }
            catch (Exception e)
            {
                throw e;
            }

            return collection;
        }

        public class CosmosDbOptions
        {
            public string EndpointUrl { get; set; }

            public string AuthorizationKey { get; set; }
        }
    }
}