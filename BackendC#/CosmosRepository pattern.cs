using evicore.repository.cosmos.Interface;
using evicore.repository.cosmos.Service;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using evicore.repository.cosmos.Model;
using Microsoft.Extensions.DependencyInjection;

namespace evicore.repository.cosmos.Configuration;

/// <summary>
/// Register Cosmos Client with DI
/// </summary>
[ExcludeFromCodeCoverage]
public static class CosmosClientConfiguration
{
    /// <summary>
    /// Generic CosmosClient to Support Different Model insert
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="cosmosConfig"></param>
    /// <param name="serviceProvider"></param>
    /// <returns></returns>
    public static ICosmosDbService<T> InitializeCosmosClientInstance<T>(CosmosSettings cosmosConfig, IServiceProvider serviceProvider) where T : class
    {
        var cosmosOptions = cosmosConfig;
        var cosmosClient = new CosmosClient(cosmosConfig.Endpoint, cosmosConfig.Key, cosmosOptions.CosmosClientOptions);
        var cosmosDbService = new CosmosDbService<T>(cosmosClient.GetContainer(cosmosConfig.DatabaseName, cosmosConfig.ContainerName),
            serviceProvider.GetRequiredService<ILogger<CosmosDbService<T>>>(),
            cosmosConfig.RetryLockLimit);
        return cosmosDbService;
    }
}


using evicore.repository.cosmos.Interface;
using evicore.repository.cosmos.Configuration;
using evicore.repository.cosmos.Model;
using evicore.repository.cosmos.Service;
using evicore.repository.@interface;
using Microsoft.Extensions.DependencyInjection;

namespace evicore.repository.cosmos.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCosmosRepository<T>(this IServiceCollection serviceCollection, CosmosSettings config) where T : class
    {
        serviceCollection.AddSingleton<ICosmosDbService<T>>(
            provider => CosmosClientConfiguration.InitializeCosmosClientInstance<T>(
                config,
                provider
            ));
        serviceCollection.AddSingleton<IRepository<T>, CosmosRepository<T>>();
        return serviceCollection;
    }
}


using Microsoft.Azure.Cosmos;

namespace evicore.repository.cosmos.Interface;
public interface ICosmosDbService<T> where T : class
{
    Task<T> CreateItemAsync(T item, string partitionKey, CancellationToken cancellationToken = default);
    Task DeleteItemAsync(string id, string partitionKey, CancellationToken cancellationToken = default);
    Task<T> ReadItemAsync(string id, string partitionKey, CancellationToken cancellationToken = default);
    Task<T> UpdateItemAsync(T item, string id, string partitionKey, CancellationToken cancellationToken = default);

    Task<T> UpdateItemAsync(Func<T, T> update, string id, string partitionKey,
        CancellationToken cancellationToken = default);

    Task<T> UpdateItemAsync(Func<T, Task<T>> update, string id, string partitionKey,
        CancellationToken cancellationToken = default);

    Task<T> UpsertItemAsync(T item, string partitionKey, CancellationToken cancellationToken = default);

    Task<T> UpsertItemAsync(Func<T, T> upsert, string id, string partitionKey,
        CancellationToken cancellationToken = default);

    Task<T> UpsertItemAsync(Func<T, Task<T>> upsert, string id, string partitionKey,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<T>> ReadItemsAsync(string query, IEnumerable<KeyValuePair<string, object>> parameters,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<TResult>> ReadItemsAsync<TResult>(string query,
        IEnumerable<KeyValuePair<string, object>> parameters, CancellationToken cancellationToken = default);

    FeedIterator<T> ReadItems(string query);

    FeedIterator<TResult> ReadItems<TResult>(string query);

    Task<IEnumerable<TResult>> ReadItemsAsync<TResult>(string query, CancellationToken cancellationToken = default);

    Task<long> GetAllItemsCount(CancellationToken cancellationToken = default);

    Task<IEnumerable<TResult>> ReadAllItemsAsync<TResult>(int? skip = null, int? take = null, CancellationToken cancellationToken = default);
    Task<T> PatchItem(string id, string partitionKey, List<PatchOperation> patchOperations);
}


using Microsoft.Azure.Cosmos;
namespace evicore.repository.cosmos.Model;

public class CosmosSettings
{
    /// <summary>
    /// Cosmos Endpoint
    /// <example>
    /// For example: https://localhost:8081
    /// </summary>
    public required string Endpoint { get; set; }

    /// <summary>
    /// Database name of the Cosmos
    /// </summary>
    public required string DatabaseName { get; set; }

    /// <summary>
    /// Cosmos Access Key
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Cosmos Container Name
    /// Required it be created before using it
    /// </summary>
    public required string ContainerName { get; set; }

    /// <summary>
    /// Retry limit before upsert fails
    /// </summary>
    public required int RetryLockLimit { get; set; } = 20;

    public CosmosClientOptions CosmosClientOptions { get; set; } = new()
    {
        ConnectionMode = ConnectionMode.Gateway
    };
}


using System.Net;
using evicore.repository.cosmos.Interface;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace evicore.repository.cosmos.Service;

internal class CosmosDbService<T> : ICosmosDbService<T> where T : class
{
    private readonly Container _container;
    private readonly ILogger<CosmosDbService<T>> _logger;
    private readonly int _retryLockLimit;

    public CosmosDbService(Container container, ILogger<CosmosDbService<T>> logger, int retryLockLimit = 20)
    {
        _container = container;
        _logger = logger;
        _retryLockLimit = retryLockLimit;
    }

    public async Task<T> CreateItemAsync(T item, string partitionKey, CancellationToken cancellationToken = default)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        var result = await _container.CreateItemAsync(item, new PartitionKey(partitionKey),
            cancellationToken: cancellationToken);
        _logger.LogDebug("CosmosDbService: {Type} added", item.GetType());
        return result?.Resource;
    }

    public async Task DeleteItemAsync(string id, string partitionKey, CancellationToken cancellationToken = default)
    {
        await _container.DeleteItemAsync<T>(id, new PartitionKey(partitionKey),
            cancellationToken: cancellationToken);
        _logger.LogDebug("CosmosDbService: {Id} deleted with partitionKey {PartitionKey}", id, partitionKey);
    }

    public async Task<T> ReadItemAsync(string id, string partitionKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<T>(id, new PartitionKey(partitionKey),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) //For handling item not found and other exceptions
        {
            if (ex.StatusCode == HttpStatusCode.NoContent || ex.StatusCode == HttpStatusCode.NotFound) return null;
            _logger.LogError(ex, "CosmosDbService: {Id} Error with partitionKey {PartitionKey}", id, partitionKey);
            throw;
        }
    }

    public async Task<IEnumerable<T>> ReadItemsAsync(string query,
        IEnumerable<KeyValuePair<string, object>> parameters, CancellationToken cancellationToken = default)
    {
        return await ReadItemsAsync<T>(query, parameters, cancellationToken);
    }

    public async Task<IEnumerable<TResult>> ReadItemsAsync<TResult>(string query,
        IEnumerable<KeyValuePair<string, object>> parameters, CancellationToken cancellationToken = default)
    {
        if (parameters == null || !parameters.Any()) throw new ArgumentNullException(nameof(parameters));
        var queryDefinition = parameters.Aggregate(new QueryDefinition(query),
            (current, parameter) => current.WithParameter(parameter.Key, parameter.Value));
        var iterator = _container.GetItemQueryIterator<TResult>(queryDefinition);
        var results = new List<TResult>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public FeedIterator<T> ReadItems(string query)
    {
        return _container.GetItemQueryIterator<T>(
            queryDefinition: new QueryDefinition(query));
    }

    public FeedIterator<TResult> ReadItems<TResult>(string query)
    {
        return _container.GetItemQueryIterator<TResult>(new QueryDefinition(query));
    }

    public async Task<IEnumerable<TResult>> ReadItemsAsync<TResult>(string query, CancellationToken cancellationToken = default)
    {
        var iterator = _container.GetItemQueryIterator<TResult>(queryText: query);
        var results = new List<TResult>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task<IEnumerable<TResult>> ReadAllItemsAsync<TResult>(int? skip = null, int? take = null, CancellationToken cancellationToken = default)
    {
        var query = "select * from c ";
        var queryDefinition = new QueryDefinition(query);

        if (skip != null && take is > 0)
        {
            query += "offset @skip limit @take";
            queryDefinition = new QueryDefinition(query);
            queryDefinition.WithParameter("@skip", skip);
            queryDefinition.WithParameter("@take", take);
        }

        var iterator = _container.GetItemQueryIterator<TResult>(queryDefinition);

        var results = new List<TResult>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task<T> PatchItem(string id, string partitionKey, List<PatchOperation> patchOperations)
    {
        _logger.LogDebug("CosmosDbService: {Id} start with patching {PartitionKey}", id, partitionKey);
        return await _container.PatchItemAsync<T>(
            id: id,
            partitionKey: new PartitionKey(id),
            patchOperations: patchOperations);
    }

    public async Task<long> GetAllItemsCount(CancellationToken cancellationToken = default)
    {
        var query = "select count(1) from c ";
        var iterator = _container.GetItemQueryIterator<long>(query);

        var results = new List<long>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results.FirstOrDefault();
    }

    public async Task<T> UpdateItemAsync(T item, string id, string partitionKey,
        CancellationToken cancellationToken = default)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        var result = await _container.ReplaceItemAsync(item, id, new PartitionKey(partitionKey),
            cancellationToken: cancellationToken);
        _logger.LogDebug("CosmosDbService: {Type} updated {Id} with partitionKey {PartitionKey}",
            item.GetType(), id, partitionKey);
        return result.Resource;
    }

    public async Task<T> UpdateItemAsync(Func<T, T> update, string id, string partitionKey,
        CancellationToken cancellationToken = default)
    {
        return await UpdateItemAsync(update, id, partitionKey, 0, cancellationToken);
    }

    public async Task<T> UpsertItemAsync(T item, string partitionKey, CancellationToken cancellationToken = default)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        var result = await _container.UpsertItemAsync(item, new PartitionKey(partitionKey),
            cancellationToken: cancellationToken);
        _logger.LogDebug("CosmosDbService: {Type} upserted with partitionKey {PartitionKey}", item.GetType(),
            partitionKey);
        return result.Resource;
    }

    /// <summary>
    ///     Creates a record of the item if record does not exist or Update the item based on the logic of the update Func.
    ///     The parameter of the update Func will be injected with the result of getting the record by ID. If there is
    ///     no record, null will be passed.
    /// </summary>
    /// <param name="upsert">A function that should return an item of type T.</param>
    /// <param name="id"></param>
    /// <param name="partitionKey"></param>
    /// <param name="cancellationToken"></param>
    public async Task<T> UpsertItemAsync(Func<T, T> upsert, string id, string partitionKey,
        CancellationToken cancellationToken = default)
    {
        return await UpsertItemAsync(upsert, id, partitionKey, 0, cancellationToken);
    }

    public async Task<T> UpsertItemAsync(Func<T, Task<T>> upsert, string id, string partitionKey,
        CancellationToken cancellationToken = default)
    {
        return await UpsertItemAsync(upsert, id, partitionKey, 0, cancellationToken);
    }

    public async Task<T> UpdateItemAsync(Func<T, Task<T>> update, string id, string partitionKey,
        CancellationToken cancellationToken = default)
    {
        return await UpdateItemAsync(update, id, partitionKey, 0, cancellationToken);
    }

    private async Task<T> UpdateItemAsync(Func<T, T> update, string id, string partitionKey,
        int retryCount, CancellationToken cancellationToken = default)
    {
        if (update is null) throw new ArgumentNullException(nameof(update));

        var record = await _container.ReadItemAsync<T>(id, new PartitionKey(partitionKey),
            cancellationToken: cancellationToken);
        ItemResponse<T> result;
        try
        {
            result = await _container.ReplaceItemAsync(update(record?.Resource), id, new PartitionKey(partitionKey),
                new ItemRequestOptions { IfMatchEtag = record?.ETag }, cancellationToken);
        }
        catch (CosmosException ex)
        {
            if (retryCount < _retryLockLimit && ex.StatusCode == HttpStatusCode.PreconditionFailed)
                return await UpdateItemAsync(update, id, partitionKey, ++retryCount, cancellationToken);

            _logger.LogError(ex, "CosmosDbService: {Id} Error updating with partitionKey {PartitionKey}", id,
                partitionKey);
            throw;
        }

        _logger.LogDebug("CosmosDbService: {Type} updated {Id} with partitionKey {PartitionKey}",
            result.Resource.GetType(), id, partitionKey);
        return result.Resource;
    }

    private async Task<T> UpdateItemAsync(Func<T, Task<T>> update, string id, string partitionKey,
        int retryCount, CancellationToken cancellationToken = default)
    {
        if (update is null) throw new ArgumentNullException(nameof(update));

        var record = await _container.ReadItemAsync<T>(id, new PartitionKey(partitionKey),
            cancellationToken: cancellationToken);
        ItemResponse<T> result;
        try
        {
            result = await _container.ReplaceItemAsync(await update(record?.Resource), id, new PartitionKey(partitionKey),
                new ItemRequestOptions { IfMatchEtag = record?.ETag }, cancellationToken);
        }
        catch (CosmosException ex)
        {
            if (retryCount < _retryLockLimit && ex.StatusCode == HttpStatusCode.PreconditionFailed)
                return await UpdateItemAsync(update, id, partitionKey, ++retryCount, cancellationToken);

            _logger.LogError(ex, "CosmosDbService: {Id} Error updating with partitionKey {PartitionKey}", id,
                partitionKey);
            throw;
        }

        _logger.LogDebug("CosmosDbService: {Type} updated {Id} with partitionKey {PartitionKey}",
            result.Resource.GetType(), id, partitionKey);
        return result.Resource;
    }

    private async Task<T> UpsertItemAsync(Func<T, T> upsert, string id, string partitionKey,
        int retryCount, CancellationToken cancellationToken = default)
    {
        if (upsert is null) throw new ArgumentNullException(nameof(upsert));

        ItemResponse<T> record = null;
        try
        {
            record = await _container.ReadItemAsync<T>(id, new PartitionKey(partitionKey),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            if (ex.StatusCode != HttpStatusCode.NotFound)
            {
                _logger.LogError(ex, "CosmosDbService: {Id} Error reading with partitionKey {PartitionKey}", id,
                    partitionKey);
                throw;
            }
        }

        ItemResponse<T> result;
        try
        {
            result = record?.Resource == null
                ? await _container.CreateItemAsync(upsert(null), new PartitionKey(partitionKey),
                    cancellationToken: cancellationToken)
                : await _container.UpsertItemAsync(upsert(record.Resource), new PartitionKey(partitionKey),
                    new ItemRequestOptions { IfMatchEtag = record.ETag }, cancellationToken);
        }
        catch (CosmosException ex)
        {
            if (retryCount < _retryLockLimit &&
                ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
                return await UpsertItemAsync(upsert, id, partitionKey, ++retryCount, cancellationToken);
            _logger.LogError(ex, "CosmosDbService: {Id} Error upserting with partitionKey {PartitionKey}", id,
                partitionKey);
            throw;
        }

        _logger.LogDebug("CosmosDbService: {Type} upserted with partitionKey {PartitionKey}",
            result?.Resource?.GetType(),
            partitionKey);
        return result?.Resource;
    }

    private async Task<T> UpsertItemAsync(Func<T, Task<T>> upsert, string id, string partitionKey,
        int retryCount, CancellationToken cancellationToken = default)
    {
        if (upsert is null) throw new ArgumentNullException(nameof(upsert));

        ItemResponse<T> record = null;
        try
        {
            record = await _container.ReadItemAsync<T>(id, new PartitionKey(partitionKey),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            if (ex.StatusCode != HttpStatusCode.NotFound)
            {
                _logger.LogError(ex, "CosmosDbService: {Id} Error reading with partitionKey {PartitionKey}", id,
                    partitionKey);
                throw;
            }
        }

        ItemResponse<T> result;
        try
        {
            result = record?.Resource == null
                ? await _container.CreateItemAsync(await upsert(null), new PartitionKey(partitionKey),
                    cancellationToken: cancellationToken)
                : await _container.UpsertItemAsync(await upsert(record.Resource), new PartitionKey(partitionKey),
                    new ItemRequestOptions { IfMatchEtag = record.ETag }, cancellationToken);
        }
        catch (CosmosException ex)
        {
            if (retryCount < _retryLockLimit &&
                ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
                return await UpsertItemAsync(upsert, id, partitionKey, ++retryCount, cancellationToken);
            _logger.LogError(ex, "CosmosDbService: {Id} Error upserting with partitionKey {PartitionKey}", id,
                partitionKey);
            throw;
        }

        _logger.LogDebug("CosmosDbService: {Type} upserted with partitionKey {PartitionKey}",
            result?.Resource?.GetType(),
            partitionKey);
        return result?.Resource;
    }
}


using System.Linq.Expressions;
using evicore.repository.cosmos.Extensions;
using evicore.repository.cosmos.Interface;
using evicore.repository.domain;
using evicore.repository.@interface;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace evicore.repository.cosmos.Service
{
    public class CosmosRepository<T> : IRepository<T> where T : class
    {
        private readonly ILogger _logger;
        private readonly ICosmosDbService<T> _cosmosDbService;

        public CosmosRepository(ICosmosDbService<T> cosmosDbService, ILogger<CosmosRepository<T>> logger)
        {
            _logger = logger;
            _cosmosDbService = cosmosDbService;
        }

        public async Task<T> ReadAsync(RepositoryIdentifiers ids, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Reading item with Id {Id} and PartitionKey {PartitionKey}", ids.Id, ids.PartitionKey);
            return await _cosmosDbService.ReadItemAsync(ids.Id, ids.PartitionKey, cancellationToken);
        }

        public async Task<IEnumerable<T>> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Reading all items");
            return await _cosmosDbService.ReadAllItemsAsync<T>(null, null,cancellationToken);
        }

        public async Task<IEnumerable<T>> QueryAsync(string queryString, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Querying items with query string {QueryString}", queryString);
            return await _cosmosDbService.ReadItemsAsync<T>(queryString, cancellationToken);
        }

        public async Task<T> CreateAsync(RepositoryIdentifiers ids, T record, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Creating item with Id {Id} and PartitionKey {PartitionKey}", ids.Id, ids.PartitionKey);
            return await _cosmosDbService.CreateItemAsync(record, ids.PartitionKey, cancellationToken);
        }

        public async Task<T> UpsertAsync(RepositoryIdentifiers ids, Func<T?, Task<T>> upsertFunction, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Upserting item with Id {Id} and PartitionKey {PartitionKey}", ids.Id, ids.PartitionKey);
            return await _cosmosDbService.UpsertItemAsync(upsertFunction, ids.Id, ids.PartitionKey, cancellationToken);
        }

        public async Task DeleteAsync(RepositoryIdentifiers ids, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Deleting item with Id {Id} and PartitionKey {PartitionKey}", ids.Id, ids.PartitionKey);
            await _cosmosDbService.DeleteItemAsync(ids.Id, ids.PartitionKey, cancellationToken);
        }

        public async Task<T> UpdateFieldAsync(RepositoryIdentifiers ids, Expression<Func<T, object>> selector, object value, CancellationToken cancellationToken = default)
        {
            if (PathHelper.GetSelectedType(selector).Name != value.GetType().Name)
                throw new ArgumentException("Types are not the same for Selector and Value passed in");
            var path = PathHelper.GetPath(selector);
            var updateOperation = PatchOperation.Replace(path, value);
            _logger.LogDebug("Updating field {Path} of item with Id {Id} and PartitionKey {PartitionKey}", path, ids.Id, ids.PartitionKey);
            return await _cosmosDbService.PatchItem(ids.Id, ids.PartitionKey, new List<PatchOperation> { updateOperation });
        }

    }
}



Thanks and Regards
Siraj

From: R, Sirajudeen (CTR) <Sirajudeen.R@evicore.com> 
Sent: Tuesday, August 20, 2024 8:29 PM
To: R, Sirajudeen (CTR) <Sirajudeen.R@evicore.com>
Subject: rpptrn



Thanks and Regards
Siraj

