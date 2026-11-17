using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Xunit;

/// <summary>The graphics native keeps decoded catalog images within its budget, and a surface whose image it let go
/// shows it again when drawn (backlog, "The port's image and texture caches never evict").</summary>
public class GraphicsImageCacheTests
{
    [Fact]
    [Trait("Category", "Workspace")]
    public void DecodedImagesStayWithinTheBudgetAndADroppedOneIsDecodedAgainWhenDrawn()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var resources = new ResourceMap(catalog);
        var images = catalog.EnumerateAssets()
            .Where(asset => asset.Asset.Name.EndsWith(".AGF", StringComparison.OrdinalIgnoreCase))
            .Select(asset => (asset.PackedId, Image: TryDecode(resources, asset.Asset)))
            .Where(pair => pair.Image is { Width: >= 200, Height: >= 200 })
            .Take(6).ToList();
        Assert.Equal(6, images.Count);
        long budget = images[4].Image!.Pixels.LongLength + images[5].Image!.Pixels.LongLength;
        var graphics = new Graphics(resources, new FrameClock(), new Sys4LogicalCanvas(800, 600),
                                    decodedImageBudgetBytes: budget);

        for (int index = 0; index < images.Count; index++) graphics.LoadSurface(10 + index, images[index].PackedId, 0);

        // The last two loaded fill the budget; the four before them went.
        Assert.Equal(budget, graphics.DecodedImageBytes);
        Assert.Equal(2, graphics.DecodedImageCount);
        Assert.True(graphics.TryGetSurfaceSize(10, out var size));
        Assert.Equal((images[0].Image!.Width, images[0].Image!.Height), size);

        SurfaceTexture first = Assert.IsType<SurfaceTexture>(graphics.ResolveSurfaceTexture(10, images[0].PackedId));
        Assert.Equal(images[0].Image!.Pixels, first.Image.Pixels);
        Assert.True(graphics.DecodedImageBytes <= budget);
    }

    private static RgbaImage? TryDecode(ResourceMap resources, AssetEntry asset)
    {
        try { return resources.DecodeTexture(asset); }
        catch (Exception) { return null; }   // a movie, which shares the AGF extension
    }
}
