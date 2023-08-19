using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using OrchardCore.Autoroute.Models;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.Data;
using OrchardCore.Taxonomies.Fields;
using YesSql.Indexes;

namespace OrchardIssue1.Theme.Indexes
{
    public class ContentItemByTermIndex : MapIndex
    {
        public string ContentItemId { get; set; }
        public string TermContentItemId { get; set; }
        public bool Published { get; set; }
        public bool Latest { get; set; }
        public string Url { get; set; }
        public string DisplayText { get; set; }
    }

    public class ContentItemByTermIndexProvider : IndexProvider<ContentItem>, IScopedIndexProvider
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly HashSet<string> _ignoredTypes = new HashSet<string>();
        private IContentDefinitionManager _contentDefinitionManager;

        public ContentItemByTermIndexProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public override void Describe(DescribeContext<ContentItem> context)
        {
            context.For<ContentItemByTermIndex>()
                .Map(contentItem =>
                {
                    // Remove index records of soft deleted items.
                    if (!contentItem.Published && !contentItem.Latest)
                    {
                        return null;
                    }

                    // Can we safely ignore this content item?
                    if (_ignoredTypes.Contains(contentItem.ContentType))
                    {
                        return null;
                    }

                    // Lazy initialization because of ISession cyclic dependency
                    _contentDefinitionManager ??= _serviceProvider.GetRequiredService<IContentDefinitionManager>();

                    // Search for Taxonomy fields
                    var contentTypeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);

                    // This can occur when content items become orphaned, particularly layer widgets when a layer is removed, before its widgets have been unpublished.
                    if (contentTypeDefinition == null)
                    {
                        _ignoredTypes.Add(contentItem.ContentType);
                        return null;
                    }

                    var fieldDefinitions = contentTypeDefinition
                        .Parts.SelectMany(x => x.PartDefinition.Fields.Where(f => f.FieldDefinition.Name == nameof(TaxonomyField)))
                        .ToArray();

                    // This type doesn't have any TaxonomyField, ignore it
                    if (fieldDefinitions.Length == 0)
                    {
                        _ignoredTypes.Add(contentItem.ContentType);
                        return null;
                    }

                    var results = new List<ContentItemByTermIndex>();
                    var autoRoutePart = contentItem.As<AutoroutePart>();


                    // Get all field values
                    foreach (var fieldDefinition in fieldDefinitions)
                    {
                        var jPart = (JObject)contentItem.Content[fieldDefinition.PartDefinition.Name];

                        if (jPart == null)
                        {
                            continue;
                        }

                        var jField = jPart[fieldDefinition.Name] as JObject;

                        if (jField == null)
                        {
                            continue;
                        }

                        var field = jField.ToObject<TaxonomyField>();

                        foreach (var termContentItemId in field.TermContentItemIds)
                        {
                            results.Add(new ContentItemByTermIndex
                            {
                                ContentItemId = contentItem.ContentItemId,
                                TermContentItemId = termContentItemId,
                                Published = contentItem.Published,
                                Latest = contentItem.Latest,
                                Url = autoRoutePart.Path,
                                DisplayText = contentItem.DisplayText
                            });
                        }
                    }

                    return results;
                });
        }
    }
}
