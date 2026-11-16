using Ome.Mods.Launch;

/// <summary><c>run-lock DATA_ROOT [--hold]</c>: take a data root's run lock as the engine's start does (launcher spec
/// §5.5), marking the start. Prints <c>held PATH</c> once it holds the lock; with <c>--hold</c> it keeps the lock until
/// its standard input closes (or the process is killed), otherwise it releases it at once. Exit code 0 when the lock
/// was taken, 3 when another process holds it, 2 on a usage error or a root that cannot hold the file. The run lock
/// tests start it as a second process; it loads no game.</summary>
internal static class RunLockCommand
{
    public static int Run(string[] args)
    {
        string? root = null;
        bool hold = false;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--hold") hold = true;
            else if (args[i].StartsWith("--", StringComparison.Ordinal) || root != null) return Usage();
            else root = args[i];
        }
        if (root == null) return Usage();

        if (!RunLock.TryAcquire(root, markStart: true, out RunLock? held, out RunLockRefusal? refusal))
        {
            Console.Error.WriteLine($"run-lock: {refusal.Message}");
            return refusal.HeldElsewhere ? 3 : 2;
        }
        using (held)
        {
            if (held.Warning != null) Console.Error.WriteLine($"run-lock: warning: {held.Warning}");
            Console.Out.WriteLine($"held {held.Path}");
            Console.Out.Flush();
            if (hold)
                while (Console.In.Read() >= 0) { }
        }
        return 0;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: Ome.Cli run-lock DATA_ROOT [--hold]");
        return 2;
    }
}
