using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

public sealed class SystemdPlugin : LoupixPlugin
{
    private IPluginHost? _host;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "systemd",
        Name = "Systemd",
        Version = new Version(1, 0, 0),
        SdkVersion = new Version(1, 24, 0),
        Author = "RadiatorTwo",
        Description = "Monitors and controls systemd user and system units over the native D-Bus API."
    };

    public override void Initialize(IPluginHost host)
    {
        _host = host;
    }

    public override IEnumerable<IPluginCommand> GetCommands() => [];

    /// <summary>
    /// Translates English text the plugin builds while running. Descriptor text is
    /// translated by the host through the same strings.&lt;code&gt;.json files.
    /// </summary>
    private string Tr(string english) => _host?.Tr(english) ?? english;
}
