namespace Age.Engine.Model;

public sealed partial class GfxState
{
    /// <summary>The D3D render target selected by op 0x20d. -1 denotes the main backbuffer.</summary>
    public int CurrentRenderTargetSlot { get; private set; } = -1;
    /// <summary>Legacy mode-1 surface tile edge selected by op 0x248. The portable backend retains
    /// the native process-global value for parity but stores every surface as one contiguous image.</summary>
    public int TiledSurfaceEdgeLength { get; private set; }

    // ---- surfaces (image buffers per slot): ctx+0x52bd4[slot], from create/set-texture ----
    private readonly Dictionary<int, (long ResId, long ColorKey)> _surfaces = new();
    // Created surfaces have real pixels but no asset resource id. Keep their class separate from both
    // loaded textures and truly surfaceless objects because native mode-0 consumes packed alpha differently.
    private readonly HashSet<int> _createdSurfaces = new();
    // Native surface record +0x08. Ordinary create/load/release workers leave this bit unchanged;
    // the numbered-save restore loop consults it to decide which asset-backed surfaces to reopen.
    private readonly HashSet<int> _reloadableSurfaces = new();
    // A separate entry models the native CMovieToTexture object attached to a surface. A null value means
    // the movie object exists but its host decoder supplied no usable IMediaPosition stop time.
    private readonly Dictionary<int, long?> _movieStopTimesMs = new();

    public void SetTiledSurfaceEdgeLength(long edgeLength)
    {
        // Native stores the complete operand in one signed dword. It does not invalidate or rebuild
        // already-created mode-1 surfaces, nor does it mark the retained compositor dirty.
        lock (_lock) TiledSurfaceEdgeLength = unchecked((int)edgeLength);
    }

    public void SetSurface(int slot, long resId, long colorKey)
    {
        lock (_lock)
        {
            _surfaces[slot] = (resId, colorKey);
            _createdSurfaces.Remove(slot);
            _movieStopTimesMs.Remove(slot);
            MarkRetainedMutation();
        }
    }

    /// <summary>Set the native surface-record +0x08 reload policy. This is separate from loading a
    /// texture because AGE's ordinary create/load/release workers preserve the existing bit.</summary>
    public void SetSurfaceReloadOnRestore(int slot, bool reload)
    {
        lock (_lock)
        {
            if (reload) _reloadableSurfaces.Add(slot);
            else _reloadableSurfaces.Remove(slot);
        }
    }

    /// <summary>Opcode 0x259 script-entry lifecycle: clear record +0x08 for every surface.
    /// Native also clears the adjacent unknown +0x0c field, which the port does not otherwise model.</summary>
    public void ClearSurfaceReloadPolicies()
    {
        lock (_lock) _reloadableSurfaces.Clear();
    }

    /// <summary>Op 0x236 handoff: retain the initialized movie graph's IMediaPosition stop time. Null
    /// deliberately distinguishes a movie surface with unavailable metadata from an empty movie slot.</summary>
    public void SetMovieStopTime(int slot, long? stopTimeMs)
    {
        lock (_lock) _movieStopTimesMs[slot] = stopTimeMs;
    }

    /// <summary>Op 0x23f query. False means no movie object occupies the slot; true with a null value
    /// means the movie exists but its stop-time query failed or returned unusable metadata.</summary>
    public bool TryGetMovieStopTime(int slot, out long? stopTimeMs)
    {
        lock (_lock) return _movieStopTimesMs.TryGetValue(slot, out stopTimeMs);
    }

    /// <summary>Op 0x20d: select a surface as the D3D render target; values at or above 1000 restore the
    /// device backbuffer in the native engine.</summary>
    public void SelectRenderTarget(long slot)
    {
        lock (_lock) CurrentRenderTargetSlot = slot is >= 0 and < 1000 ? (int)slot : -1;
    }

    /// <summary>Op 0x23d: release the transient surface range while retaining system-owned low slots.</summary>
    public void ReleaseSurfaceRange(int firstSlot, int count)
    {
        lock (_lock)
        {
            int end = checked(firstSlot + count);
            for (int slot = firstSlot; slot < end; slot++)
            {
                _surfaces.Remove(slot);
                _createdSurfaces.Remove(slot);
                _movieStopTimesMs.Remove(slot);
                _surfaceTransitions.Remove(slot);
                _movieMaskTransitions.Remove(slot);
            }
            if (CurrentRenderTargetSlot >= firstSlot && CurrentRenderTargetSlot < end)
                CurrentRenderTargetSlot = -1;
            MarkRetainedMutation();
        }
    }

    public void CreateSurface(int slot)
    {
        lock (_lock)
        {
            _surfaces[slot] = (0, -1); // create-texture: real mutable pixels, no asset id or color key
            _createdSurfaces.Add(slot);
            _movieStopTimesMs.Remove(slot);
            MarkRetainedMutation();
        }
    }

    public void ClearSurface(int slot)
    {
        lock (_lock)
        {
            _surfaces.Remove(slot);
            _createdSurfaces.Remove(slot);
            _movieStopTimesMs.Remove(slot);
            _surfaceTransitions.Remove(slot);
            _movieMaskTransitions.Remove(slot);
            MarkRetainedMutation();
        }
    }
}
