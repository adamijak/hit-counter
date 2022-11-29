using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Adamijak.HitCounter;
public class Api
{
    public readonly Container hitContainer;
    public readonly Container siteContainer;
    public Api(IConfiguration config)
    {
        var cosmosClientOptions = new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
            EnableContentResponseOnWrite = false,
        };

        var cosmosClient = new CosmosClient(config["CosmosConnectionString"], cosmosClientOptions);

        hitContainer = cosmosClient.GetContainer(config["DatabaseId"], config["HitContainerId"]);
        siteContainer = cosmosClient.GetContainer(config["DatabaseId"], config["SiteContainerId"]);
    }

    [FunctionName("Post")]
    public async Task<IActionResult> Post([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sites/{siteId}")] HitRequest hitRequest, string siteId, ILogger log, CancellationToken cancelToken)
    {
        if (string.IsNullOrWhiteSpace(siteId) || string.IsNullOrWhiteSpace(hitRequest?.Fingerprint))
        {
            return new BadRequestResult();
        }

        try
        {
            var patch = new List<PatchOperation>()
            {
                PatchOperation.Increment("/hitCount", 1),
            };
            await hitContainer.PatchItemAsync<Hit>(hitRequest.Fingerprint, new PartitionKey(siteId), patch, null, cancelToken);
        }
        catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            try
            {
                await siteContainer.ReadItemAsync<dynamic>(siteId, new PartitionKey(siteId), null, cancelToken);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return new BadRequestResult();
            }

            var hit = new Hit
            {
                Id = hitRequest.Fingerprint,
                SiteId = siteId,
                HitCount = 1,
            };
            try
            {
                await hitContainer.CreateItemAsync(hit, new PartitionKey(hit.SiteId), null, cancelToken);
            }
            catch (CosmosException exc) when (exc.StatusCode == HttpStatusCode.Conflict)
            {
                log.LogError(exc, "Can not create item, it already exists.");
            }
        }

        return new OkResult();
    }

    [FunctionName("Get")]
    public async Task<ActionResult<HitResponse>> Get([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sites/{siteId}")] HttpRequest req, string siteId, ILogger log, CancellationToken cancelToken)
    {
        if (string.IsNullOrWhiteSpace(siteId))
        {
            return new BadRequestResult();
        }

        var hitCount = await hitContainer.GetItemLinqQueryable<Hit>(linqSerializerOptions: new CosmosLinqSerializerOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase })
            .Where(hit => hit.SiteId == siteId).CountAsync(cancelToken);

        return new HitResponse { HitCount = hitCount.Resource };
    }
}

