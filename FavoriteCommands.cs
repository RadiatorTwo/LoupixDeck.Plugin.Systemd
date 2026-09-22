using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// Opens the units folder. It takes no unit, because the folder shows the favorites.
/// </summary>
internal sealed class OpenUnitsCommand(
    UnitRegistry registry,
    SystemdSettings settings,
    IPluginHost host) : IPluginCommand
{
    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = SystemdCommands.OpenUnits,
        DisplayName = "Open Units Folder",
        Group = SystemdCommands.Group,
        Icon = SystemdCommands.PickerGlyph,
        Description = "Open a folder with the favorite units and their state"
    };

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton | ButtonTargets.SimpleButton;

    public Task Execute(CommandContext ctx)
    {
        try
        {
            ctx.Host.OpenFolder(new UnitFolderProvider(registry, settings, host));
        }
        catch (Exception ex)
        {
            ctx.Host.Logger.Warn($"{Descriptor.CommandName} failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Adds or removes the unit in its parameter from the favorites. These are how the favorites list
/// is edited without typing unit names: the command menu offers them next to every unit.
/// </summary>
internal sealed class FavoriteCommand(
    CommandDescriptor descriptor,
    bool add,
    UnitRegistry registry,
    SystemdSettings settings) : UnitCommandBase(descriptor, registry, settings)
{
    protected override Task ExecuteCore(CommandContext ctx)
    {
        UnitId id = ResolveUnit(ctx);

        if (!id.IsValid)
        {
            ShowOverlay(ctx, ctx.Host.Tr("No unit"));
            return Task.CompletedTask;
        }

        if (add)
        {
            bool added = Settings.AddFavorite(id);
            Registry.Track(id);
            ShowOverlay(ctx, ctx.Host.Tr(added ? "Added to favorites" : "Already a favorite"));
            return Task.CompletedTask;
        }

        bool removed = Settings.RemoveFavorite(id);
        ShowOverlay(ctx, ctx.Host.Tr(removed ? "Removed from favorites" : "Not a favorite"));
        return Task.CompletedTask;
    }
}

/// <summary>Builds the folder and favorites commands.</summary>
internal static class FavoriteCommands
{
    public static IEnumerable<IPluginCommand> Create(UnitRegistry registry, SystemdSettings settings, IPluginHost host)
    {
        yield return new OpenUnitsCommand(registry, settings, host);

        yield return new FavoriteCommand(
            SystemdCommands.UnitDescriptor(
                SystemdCommands.AddFavorite,
                "Add Unit to Favorites",
                "Put the unit into the units folder",
                hiddenFromMenu: true),
            add: true,
            registry,
            settings);

        yield return new FavoriteCommand(
            SystemdCommands.UnitDescriptor(
                SystemdCommands.RemoveFavorite,
                "Remove Unit from Favorites",
                "Take the unit out of the units folder",
                hiddenFromMenu: true),
            add: false,
            registry,
            settings);
    }
}
