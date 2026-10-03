using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// The icon and caption every Systemd command brings to a touch button, set by the plugin instead
/// of the host's standard look. The geometry follows the host's own icon-and-caption look, so a
/// Systemd button sits next to any other button without looking out of place.
/// </summary>
internal static class SystemdButtonLayouts
{
    // Material Design Icons code points, one per role so a command and its layout cannot drift apart.
    public const string Group = "\U000F08BB";          // mdi-cog-outline
    public const string Start = "\U000F040A";          // mdi-play
    public const string Stop = "\U000F04DB";           // mdi-stop
    public const string Restart = "\U000F0709";        // mdi-restart
    public const string Reload = "\U000F0453";         // mdi-reload
    public const string Toggle = "\U000F0425";         // mdi-power
    public const string ResetFailed = "\U000F14BC";    // mdi-alert-remove
    public const string Enable = "\U000F05E1";         // mdi-check-circle-outline
    public const string Disable = "\U000F073A";        // mdi-cancel
    public const string Mask = "\U000F0341";           // mdi-lock-outline
    public const string Unmask = "\U000F0FC7";         // mdi-lock-open-variant-outline
    public const string RunTimer = "\U000F1AE1";       // mdi-timer-play-outline
    public const string UnitsFolder = "\U000F1080";    // mdi-folder-cog-outline
    public const string AddFavorite = "\U000F1567";    // mdi-star-plus-outline
    public const string RemoveFavorite = "\U000F1568"; // mdi-star-minus-outline
    public const string FavoriteSlot = "\U000F04CE";   // mdi-star
    public const string Status = "\U000F05F6";         // mdi-heart-pulse
    public const string Name = "\U000F0316";           // mdi-label-outline
    public const string Description = "\U000F09ED";    // mdi-text-box-outline
    public const string SubState = "\U000F15AB";       // mdi-list-status
    public const string LoadState = "\U000F0E29";      // mdi-file-check-outline
    public const string FileState = "\U000F107C";      // mdi-file-cog-outline
    public const string Uptime = "\U000F051B";         // mdi-timer-outline
    public const string MainPid = "\U000F0EFE";        // mdi-identifier
    public const string Result = "\U000F023C";         // mdi-flag-checkered
    public const string NextRun = "\U000F00F0";        // mdi-calendar-clock
    public const string LastRun = "\U000F02DA";        // mdi-history
    public const string Instance = "\U000F1C9A";       // mdi-server-outline

    /// <summary>
    /// The tint of a persistent action's icon. Those change the unit file state and survive a
    /// reboot, so their buttons must not look like the runtime actions next to them.
    /// </summary>
    public const string PersistentColor = "#FFB300";

    // Pixel values for a 90 px key; the host scales them onto the key actually being written.
    private const double IconScale = 0.5;
    private const int IconOffsetY = -9;
    private const int CaptionSize = 11;
    private const int CaptionOffsetY = 27;
    private const int CaptionBoxWidth = 88;
    private const int CaptionBoxHeight = 22;

    // The tall variant leaves room for two lines of text: a menu leaf is named "unit — action",
    // and a display button replaces the caption with its value, which can be a name and a state.
    private const double TallIconScale = 0.36;
    private const int TallIconOffsetY = -19;
    private const int TallCaptionOffsetY = 17;
    private const int TallCaptionBoxHeight = 40;

    /// <summary>
    /// Translates a caption into the host's language. Set once the plugin is initialised; the layout
    /// is stored on the button at assignment, so it is the language at that moment that counts.
    /// </summary>
    internal static Func<string, string> Translate { get; set; } = static english => english;

    /// <summary>The icon centred above a caption.</summary>
    /// <param name="glyph">The icon.</param>
    /// <param name="caption">English caption; null keeps the name the host would show, which is the
    /// right thing for a unit command: a menu leaf carries the unit name there.</param>
    /// <param name="tall">Gives the caption two lines and shrinks the icon to make room.</param>
    /// <param name="color">The icon's tint, or null for the standard white.</param>
    public static ButtonLayoutDescriptor IconWithCaption(
        string glyph,
        string? caption,
        bool tall = false,
        string? color = null) => new()
    {
        Mode = ButtonLayoutMode.Custom,
        Layers =
        [
            new ButtonLayerDescriptor
            {
                Kind = ButtonLayerKind.Symbol,
                Glyph = glyph,
                IconScale = tall ? TallIconScale : IconScale,
                OffsetY = tall ? TallIconOffsetY : IconOffsetY,
                Color = color
            },
            new ButtonLayerDescriptor
            {
                Kind = ButtonLayerKind.Text,
                Text = caption == null ? null : Translate(caption),
                TextSize = CaptionSize,
                OffsetY = tall ? TallCaptionOffsetY : CaptionOffsetY,
                BoxWidth = CaptionBoxWidth,
                BoxHeight = tall ? TallCaptionBoxHeight : CaptionBoxHeight
            }
        ]
    };
}
