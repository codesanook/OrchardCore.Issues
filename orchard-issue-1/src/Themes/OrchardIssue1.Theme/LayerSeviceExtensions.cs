using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OrchardCore.Layers.Models;
using OrchardCore.Layers.Services;
using OrchardCore.Rules;
using OrchardCore.Rules.Models;

namespace OrchardIssue1.Theme;

public static class LayerServiceExtensions
{
    public const string HomepageLayerName = "Homepage";

    public static async Task CreateHomepageLayerIfNotExistAsync(this ILayerService layerService, IConditionIdGenerator conditionIdGenerator)
    {
        // Prepare a layer
        var layersDocument = await layerService.LoadLayersAsync();
        var homepageLayer = layersDocument.Layers.FirstOrDefault(x => x.Name == HomepageLayerName);
        if (homepageLayer == null)
        {
            homepageLayer = new Layer
            {
                Name = HomepageLayerName,
                Description = "Widgets in this layer are only displayed on the homepage."
            };

            var homepageCondition = new HomepageCondition() { Value = true, Name = "HomepageCondition", };
            conditionIdGenerator.GenerateUniqueId(homepageCondition);
            homepageLayer.LayerRule = new Rule() { Conditions = new List<Condition>() { homepageCondition } };
            conditionIdGenerator.GenerateUniqueId(homepageLayer.LayerRule);

            layersDocument.Layers.Add(homepageLayer);
            await layerService.UpdateAsync(layersDocument);
        }
    }

}
