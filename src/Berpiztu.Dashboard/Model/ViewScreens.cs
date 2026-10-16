namespace Berpiztu.Dashboard.Model;

/// <summary>
/// How a page meets the screens it is shown on, desktop and mobile: set in
/// design on its landscape view, the desktop's, and kept with it, so a page
/// whose landscape view serves a phone too needs no portrait view at all.
/// </summary>
/// <param name="LandscapeOnPortrait">
/// Set: a screen taller than wide, a phone held upright, shows the landscape
/// view too, whether a portrait view is laid out or not. Not set, it shows
/// the portrait view.
/// </param>
/// <param name="LandscapeFit">
/// How the view shown is fitted on a screen wider than tall where the device
/// has chosen none in its zoom window; the device's own choice, once made,
/// is kept and wins. Null is <see cref="LandscapeDefault"/>.
/// </param>
/// <param name="PortraitFit">
/// The same on a screen taller than wide. Null is <see cref="PortraitDefault"/>.
/// </param>
public sealed record ViewScreens(bool? LandscapeOnPortrait = null, ViewFit? LandscapeFit = null, ViewFit? PortraitFit = null)
{
    /// <summary>A wide screen filled with the view's aspect kept, where nothing else is set.</summary>
    public const ViewFit LandscapeDefault = ViewFit.Fill;

    /// <summary>A screen standing upright shows each card at its own size (Fill squeezed the cards that hold several charts), where nothing else is set.</summary>
    public const ViewFit PortraitDefault = ViewFit.Fluid;

    /// <summary>The view a screen of this orientation shows.</summary>
    public string ViewOn(bool portrait) => portrait && LandscapeOnPortrait != true ? DashboardView.Portrait : DashboardView.Landscape;

    /// <summary>How the view opens on a screen of this orientation, where the device keeps no fitting of its own.</summary>
    public ViewFit FitOn(bool portrait) => portrait ? PortraitFit ?? PortraitDefault : LandscapeFit ?? LandscapeDefault;
}
