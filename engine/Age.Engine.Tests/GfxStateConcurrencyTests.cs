using System.Threading.Tasks;
using Age.Engine.Model;
using Xunit;

public class GfxStateConcurrencyTests
{
    [Fact]
    public void Snapshot_DoesNotThrow_WhileObjectsMutate()
    {
        var gfx = new GfxState();
        var stop = false;
        var writer = Task.Run(() =>
        {
            long h = 0;
            while (!stop)
            {
                h = (h + 1) % 64;
                gfx.BindDraw(h, 0, 0, 0, 10, 10, 0, 0);   // GetOrCreate + visible
                gfx.GetOrCreate(h + 100);                  // bare create
                if (h % 8 == 0) gfx.Release(h + 100);      // remove
            }
        });
        // Hammer the reader concurrently; a dictionary mutated during enumeration would throw here.
        for (int i = 0; i < 20000; i++) { var _ = gfx.SnapshotVisibleObjects(); }
        stop = true;
        writer.Wait();
    }
}
