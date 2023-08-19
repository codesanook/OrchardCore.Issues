using OrchardCore.Data.Migration;
using OrchardCore.ContentManagement;
using OrchardCore.Layers.Services;
using OrchardCore.Rules;
using OrchardCore.DisplayManagement.Notify;
using Microsoft.AspNetCore.Mvc.Localization;
using OrchardCore.Menu.Models;
using YesSql;
using System.Text.RegularExpressions;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.Autoroute.Models;
using OrchardCore.Title.Models;
using OrchardCore.Settings;
using OrchardCore.Markdown.Models;
using OrchardCore.Alias.Models;
using OrchardCore.Contents.Models;
using OrchardCore.Taxonomies.Settings;
using YesSql.Sql;
using OrchardCore.ContentManagement.Records;
using OrchardIssue1.Theme.Indexes;
using System.Threading.Tasks;
using System.Linq;

namespace OrchardIssue1.Theme;
public class Migrations : DataMigration
{
    private static readonly Regex pattern = new Regex(@"\s+", RegexOptions.Compiled);
    private readonly IContentManager _contentManager;
    private readonly IContentHandleManager _contentHandleManager;
    private readonly ISession _session;
    private readonly ILayerService _layerService;
    private readonly IConditionIdGenerator _conditionIdGenerator;

    private readonly INotifier _notifier;
    private readonly IHtmlLocalizer<Migrations> H;
    private readonly IContentDefinitionManager _contentDefinitionManager;
    private readonly ISiteService _siteService;

    public Migrations(
        INotifier notifier,
        IHtmlLocalizer<Migrations> localizer,
        IConditionIdGenerator conditionIdGenerator,
        IContentManager contentManager,
        IContentHandleManager contentHandleManager,
        ISession session,
        IContentDefinitionManager contentDefinitionManager,
        ISiteService siteService,
        ILayerService layerService
    )
    {
        _contentManager = contentManager;
        _contentHandleManager = contentHandleManager;
        _session = session;
        _notifier = notifier;
        H = localizer;
        _conditionIdGenerator = conditionIdGenerator;
        _contentDefinitionManager = contentDefinitionManager;
        _siteService = siteService;
        _layerService = layerService;
    }

    public async Task<int> CreateAsync()
    {
        await CreateHomePageContentItemAsync();
        await _layerService.CreateHomepageLayerIfNotExistAsync(_conditionIdGenerator);
        await CreateMenuAsync();

        CreateContentItemByTermIndexTable();
        return 1;
    }

    public void CreateContentItemByTermIndexTable()
    {
        SchemaBuilder.CreateMapIndexTable<ContentItemByTermIndex>(table => table
            .Column<string>(nameof(ContentItemByTermIndex.ContentItemId), c => c.WithLength(26))
            .Column<string>(nameof(ContentItemByTermIndex.TermContentItemId), c => c.WithLength(26))
            .Column<string>(nameof(ContentItemByTermIndex.Url), c => c.WithDefault(AutoroutePart.MaxPathLength))
            .Column<string>(nameof(ContentItemByTermIndex.DisplayText), c => c.WithDefault(ContentItemIndex.MaxDisplayTextSize))
            .Column<bool>(nameof(ContentItemByTermIndex.Published), c => c.WithDefault(true))
            .Column<bool>(nameof(ContentItemByTermIndex.Latest), c => c.WithDefault(false))
        );
    }

    private void CreatePostType(ContentItem categoriesTaxonomy)
    {
        // Create a part which a name as type to contain taxonomy field
        const string contentType = "Post";
        _contentDefinitionManager.AlterPartDefinition(contentType, builder => builder
            .Attachable(false)
            .WithField(
                "Category", // A post has only one category
                field => field
                    .OfType("TaxonomyField")
                    .WithSettings(new ContentPartFieldSettings()
                    {
                        DisplayName = "Category",
                        Position = "0", // Set position of a field in an editor
                                        // Editor = "Tags",
                                        // DisplayMode = "Tags",
                    })
                    .WithSettings(new TaxonomyFieldSettings()
                    {
                        TaxonomyContentItemId = categoriesTaxonomy.ContentItemId,
                        Unique = true,
                        LeavesOnly = true,
                    })
            )
        );

        var urlPattern = new[] {
            "{% assign category = ContentItem.Content.Post.Category | taxonomy_terms | first %}",
            "{{ 'categories' }}/{{ category | display_text | slugify }}/{{ ContentItem | display_text | slugify }}"
        };

        _contentDefinitionManager.AlterTypeDefinition(
            contentType,
            type => type
                .WithPart(nameof(TitlePart))
                .WithPart(nameof(MarkdownBodyPart), part => part.WithEditor("Wysiwyg"))
                .WithPart(nameof(AutoroutePart), part => part.WithSettings(
                    new AutoroutePartSettings()
                    {
                        Pattern = string.Join("\n", urlPattern),
                        AllowCustomPath = true,
                        AllowUpdatePath = true,
                    })
                )
                .WithPart(contentType) // a part to contain fields
                .Creatable()
                .Listable()
                .Versionable(false)
        );
    }

    private async Task<ContentItem> CreateMenuItem(string displayText, string basedUrl = null)
    {
        var contactUsMenuItem = await _contentManager.NewAsync("LinkMenuItem");
        contactUsMenuItem.DisplayText = displayText;

        var childUrl = pattern.Replace(displayText.Trim(), "-").ToLower();
        var urlSegments = new[] { basedUrl, childUrl }.Where(url => !string.IsNullOrEmpty(url));
        var resolvedUrl = $"~/{string.Join('/', urlSegments)}";
        contactUsMenuItem.Alter<LinkMenuItemPart>(p => p.Url = resolvedUrl);
        return contactUsMenuItem;
    }

    private async Task CreateMenuAsync()
    {
        // Create home menu item
        var homeMenuItem = await _contentManager.NewAsync("LinkMenuItem");
        homeMenuItem.DisplayText = "Home";
        homeMenuItem.Alter<LinkMenuItemPart>(p => { p.Url = "~/"; });
        await _contentManager.CreateAsync(homeMenuItem, VersionOptions.Published);

        // Create main menu
        const string mainMenuName = "Main Menu";
        var mainMenu = await _contentManager.NewAsync("Menu");
        mainMenu.Alter<TitlePart>(p => p.Title = mainMenuName);
        mainMenu.DisplayText = mainMenuName;
        mainMenu.Alter<AliasPart>(p => p.Alias = "main-menu");

        mainMenu.Alter<MenuItemsListPart>(p => p.MenuItems.AddRange(new[] { homeMenuItem }));
        await _contentManager.CreateAsync(mainMenu, VersionOptions.Published);
    }

    private async Task CreateHomePageContentItemAsync()
    {
        // Define a new content type for a home page
        _contentDefinitionManager.AlterTypeDefinition(
            "HomePage",
            cfg => cfg
                .WithPart(nameof(CommonPart))
                .WithPart(nameof(AutoroutePart))
                .Versionable(false)
                .Listable()
            );

        // Create a static content item and use as a home page
        var contentItem = await _contentManager.NewAsync("HomePage");
        // We need to set DisplayText because it will be used to create a HTML page title.
        // See src/OrchardCore.Modules/OrchardCore.Contents/Views/ContentsMetadata.cshtml
        contentItem.DisplayText = "Home page";
        var autoroutePart = contentItem.Alter<AutoroutePart>(p =>
        {
            p.SetHomepage = true;
            p.Path = "home"; // We need this because we can access this item even thought it is no longer a home page.
        });

        await _contentManager.CreateAsync(contentItem, VersionOptions.Published);
    }
}
