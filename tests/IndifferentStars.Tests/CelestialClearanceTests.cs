using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
namespace IndifferentStars.Tests;
public sealed class CelestialClearanceTests
{
    [Fact]
    public void UsesNearestSurfaceAndActualRadius()
    {
        var catalog=WorldCatalog.Generate(20261101);
        var nearPlanet=SurfaceNavigation.Nearest(catalog,catalog.LocalFrameOrigin);
        Assert.Equal(catalog.Planet,nearPlanet.Body);Assert.Equal(1_400_000,nearPlanet.DistanceMetres,3);
        var nearStar=SurfaceNavigation.Nearest(catalog,catalog.Star.SystemPosition+new Vec3d(catalog.Star.Radius+500_000,0,0));
        Assert.Equal(catalog.Star,nearStar.Body);Assert.Equal(500_000,nearStar.DistanceMetres,3);
    }
}
