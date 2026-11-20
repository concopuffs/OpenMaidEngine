using Ome.Engine.Hosting;

namespace Ome.Engine.Natives;

/// <summary>
/// The two draws AGE 4.38D's 3D effects make from the game's generator (<see cref="CrtRandom"/>): the leaves, the snow
/// and the rain alike. Each consumes one <c>rand()</c>, so an effect advances op 0x60's stream as native's does.
/// </summary>
internal static class Scene3DRandom
{
    /// <summary>rand_unit_percent@0x471990: (rand() % 100) / 100, 0.00 to 0.99, rounded to a float.</summary>
    public static float Unit(CrtRandom random) => (float)(random.Next() % 100 / 100.0);

    /// <summary>rand_centered@0x4719c0: the unit draw less 0.5, -0.50 to 0.49, rounded to a float.</summary>
    public static float Centered(CrtRandom random) => (float)(Unit(random) - 0.5);
}

/// <summary>
/// The C runtime's <c>qsort</c> as AGE 4.38D links it (Visual Studio 2010's, <c>_qsort@0x587920</c>): a quicksort on the
/// median of the first, middle and last, which leaves ranges of eight or fewer to a selection sort that moves the
/// greatest to the end. It is not stable, so items the comparison calls equal end in its order, which the port keeps.
/// </summary>
internal static class MsvcQsort
{
    private const int Cutoff = 8;
    private const int StackSize = 30;   // 8 * sizeof(void*) - 2

    public static void Sort<T>(T[] items, Comparison<T> compare)
    {
        if (items.Length < 2) return;
        var lowStack = new int[StackSize];
        var highStack = new int[StackSize];
        int stack = 0;
        int lo = 0, hi = items.Length - 1;
        while (true)
        {
            int size = hi - lo + 1;
            if (size <= Cutoff) ShortSort(items, lo, hi, compare);
            else
            {
                int mid = lo + size / 2;
                if (compare(items[lo], items[mid]) > 0) Swap(items, lo, mid);
                if (compare(items[lo], items[hi]) > 0) Swap(items, lo, hi);
                if (compare(items[mid], items[hi]) > 0) Swap(items, mid, hi);
                int low = lo, high = hi;
                while (true)
                {
                    if (mid > low)
                        do low++; while (low < mid && compare(items[low], items[mid]) <= 0);
                    if (mid <= low)
                        do low++; while (low <= hi && compare(items[low], items[mid]) <= 0);
                    do high--; while (high > mid && compare(items[high], items[mid]) > 0);
                    if (high < low) break;
                    Swap(items, low, high);
                    if (mid == high) mid = low;
                }
                high++;
                if (mid < high)
                    do high--; while (high > mid && compare(items[high], items[mid]) == 0);
                if (mid >= high)
                    do high--; while (high > lo && compare(items[high], items[mid]) == 0);
                if (high - lo >= hi - low)
                {
                    if (lo < high) Push(lo, high);
                    if (low < hi)
                    {
                        lo = low;
                        continue;
                    }
                }
                else
                {
                    if (low < hi) Push(low, hi);
                    if (lo < high)
                    {
                        hi = high;
                        continue;
                    }
                }
            }
            if (--stack < 0) return;
            lo = lowStack[stack];
            hi = highStack[stack];
        }

        void Push(int low, int high)
        {
            lowStack[stack] = low;
            highStack[stack] = high;
            stack++;
        }
    }

    // shortsort: the greatest of lo..hi to hi, then the same for one fewer.
    private static void ShortSort<T>(T[] items, int lo, int hi, Comparison<T> compare)
    {
        while (hi > lo)
        {
            int max = lo;
            for (int at = lo + 1; at <= hi; at++)
                if (compare(items[at], items[max]) > 0) max = at;
            Swap(items, max, hi);
            hi--;
        }
    }

    private static void Swap<T>(T[] items, int a, int b) => (items[a], items[b]) = (items[b], items[a]);
}
