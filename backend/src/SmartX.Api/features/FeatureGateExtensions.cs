using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace SmartX.Api.Features;

public static class FeatureGateExtensions
{

    public static RouteGroupBuilder RequireFeature(
        this RouteGroupBuilder group,
        Func<SmartXFeatures, bool> selector,
        string featureName,
        string plannedIn)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            var features = context.HttpContext.RequestServices
                .GetRequiredService<IOptionsMonitor<SmartXFeatures>>().CurrentValue;

            if (selector(features))
            {
                return await next(context);
            }

            return TypedResults.Problem(
                title: $"{featureName} is not enabled",
                detail: $"This deliverable is scheduled for {plannedIn}. The route exists so clients " +
                        "can be built against the final contract, but it does not serve data yet.",
                statusCode: StatusCodes.Status501NotImplemented,
                extensions: new Dictionary<string, object?>
                {
                    ["feature"] = featureName,
                    ["status"] = "planned",
                    ["plannedIn"] = plannedIn
                });
        });

        group.WithMetadata(new PlannedFeatureAttribute(featureName, plannedIn));
        return group;
    }
}

[AttributeUsage(AttributeTargets.All)]
public sealed class PlannedFeatureAttribute : Attribute
{
    public PlannedFeatureAttribute(string feature, string plannedIn)
    {
        Feature = feature;
        PlannedIn = plannedIn;
    }

    public string Feature { get; }
    public string PlannedIn { get; }
}
