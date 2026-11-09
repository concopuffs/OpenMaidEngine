namespace Ome.Engine.Model;

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

    // Native surface-record tables (docs/sys4-format-notes.md "numbered save"): the gfx manager keeps a live
    // 1,000 x 0x14 table at +0x700 and a page snapshot at +0x5520. Workers write live fields in place
    // (load: id/key/created=0; blank create: id -1, created 1; release: id -1 only; drawing into a surface:
    // created 1), op 0x71 copies live -> snapshot, and context_state_serialize writes the snapshot. The
    // records are bookkeeping only: rendering uses _surfaces/_createdSurfaces above.
    public const int SurfaceRecordCount = 1000;
    private const int SurfaceRecordDwords = 5;
    private const int RecordResourceId = 0, RecordColorKey = 1, RecordReload = 2, RecordPolicy = 3,
        RecordCreated = 4;
    private readonly int[] _surfaceRecords = NewBootSurfaceRecordTable();
    private readonly int[] _surfaceRecordSnapshot = NewSurfaceRecordTable();

    private static int[] NewSurfaceRecordTable()
    {
        var table = new int[SurfaceRecordCount * SurfaceRecordDwords];
        ResetSurfaceRecordTable(table);
        return table;
    }

    // engine_initialize_graphics_from_settings creates the composite scratch surfaces 0x24/0x25 at boot.
    private static int[] NewBootSurfaceRecordTable()
    {
        int[] table = NewSurfaceRecordTable();
        table[0x24 * SurfaceRecordDwords + RecordCreated] = 1;
        table[0x25 * SurfaceRecordDwords + RecordCreated] = 1;
        return table;
    }

    // gfx_surface_record_tables_init@0x472700: zero both tables, then resource id -1 in every record.
    private static void ResetSurfaceRecordTable(int[] table)
    {
        Array.Clear(table);
        for (int slot = 0; slot < SurfaceRecordCount; slot++) table[slot * SurfaceRecordDwords] = -1;
    }

    private void WriteSurfaceRecord(int slot, int field, int value)
    {
        if ((uint)slot < SurfaceRecordCount) _surfaceRecords[slot * SurfaceRecordDwords + field] = value;
    }

    /// <summary>Native 0x1f9/0x249 colorkey packing: a negative operand is "no key" (0), otherwise
    /// 0xff000000 | RGB.</summary>
    public static int PackNativeSurfaceColorKey(long colorKey)
    {
        int value = unchecked((int)colorKey);   // the handler tests the fetched DWORD's sign
        return value < 0 ? 0 : unchecked((int)(0xff000000 | (uint)value & 0xffffff));
    }

    /// <summary>Record a content write into <paramref name="slot"/>: the native fill, copy, scale-copy,
    /// transition and composite workers all set the created flag (+0x10) on their destination.</summary>
    public void MarkSurfaceContentWritten(int slot)
    {
        lock (_lock) WriteSurfaceRecord(slot, RecordCreated, 1);
    }

    /// <summary>Record a gfx_surface_create_blank whose pixels the host owns (op 0x70's layout surface, a
    /// missing 0x223/0x24f target): resource id -1, created flag 1.</summary>
    public void RecordBlankSurfaceCreated(int slot)
    {
        lock (_lock)
        {
            WriteSurfaceRecord(slot, RecordResourceId, -1);
            WriteSurfaceRecord(slot, RecordCreated, 1);
        }
    }

    /// <summary>The 0x223/0x24f producers create their target surface only when the slot is empty (or
    /// sized differently, which this model cannot see).</summary>
    public void RecordTransitionTargetCreated(int slot)
    {
        lock (_lock)
        {
            if (_surfaces.ContainsKey(slot)) return;
            WriteSurfaceRecord(slot, RecordResourceId, -1);
            WriteSurfaceRecord(slot, RecordCreated, 1);
        }
    }

    /// <summary>Op 0x71: copy the live surface records into the page snapshot that numbered saves write
    /// (op_0x71_reset_adv_text_layout@0x41e540 memcpy ctx+0x46d14 -> ctx+0x4bb34).</summary>
    public void SnapshotSurfaceRecords()
    {
        lock (_lock) Array.Copy(_surfaceRecords, _surfaceRecordSnapshot, _surfaceRecords.Length);
    }

    /// <summary>The 20,000 snapshot bytes context_state_serialize copies into a numbered save.</summary>
    public byte[] CaptureNativeSurfaceRecords()
    {
        lock (_lock)
        {
            var bytes = new byte[_surfaceRecordSnapshot.Length * 4];
            Buffer.BlockCopy(_surfaceRecordSnapshot, 0, bytes, 0, bytes.Length);
            return bytes;
        }
    }

    public void SetTiledSurfaceEdgeLength(long edgeLength)
    {
        // Native stores the complete operand in one signed dword. It does not invalidate or rebuild
        // already-created mode-1 surfaces, nor does it mark the retained compositor dirty.
        lock (_lock) TiledSurfaceEdgeLength = unchecked((int)edgeLength);
    }

    /// <summary>Ops 0x1f9 (mode 0) and 0x249 (<paramref name="rawTextureMode"/>, mode 1). Like
    /// gfx_surface_load_asset@0x477c40, the record gets the resource id (-1 in mode 1), the packed key and
    /// a cleared created flag; the reload flag is left alone.</summary>
    public void SetSurface(int slot, long resId, long colorKey, bool rawTextureMode = false)
    {
        lock (_lock)
        {
            WriteSurfaceRecord(slot, RecordResourceId, rawTextureMode ? -1 : unchecked((int)resId));
            WriteSurfaceRecord(slot, RecordColorKey, PackNativeSurfaceColorKey(colorKey));
            WriteSurfaceRecord(slot, RecordCreated, 0);
            _surfaces[slot] = (resId, colorKey);
            _createdSurfaces.Remove(slot);
            _movieStopTimesMs.Remove(slot);
            MarkRetainedMutation();
        }
    }

    /// <summary>Op 0x258 (gfx_surface_set_persistence_flags@0x4159c0): bit 0 replaces the record +0x08
    /// reload policy and bit 1 the adjacent +0x0c field, in both the live and the snapshot table. This is
    /// separate from loading a texture because AGE's create/load/release workers preserve both.</summary>
    public void SetSurfacePersistenceFlags(int slot, long flags)
    {
        lock (_lock)
        {
            if ((flags & 1) != 0) _reloadableSurfaces.Add(slot);
            else _reloadableSurfaces.Remove(slot);
            if ((uint)slot >= SurfaceRecordCount) return;
            int at = slot * SurfaceRecordDwords;
            _surfaceRecords[at + RecordReload] = _surfaceRecordSnapshot[at + RecordReload] = (int)(flags & 1);
            _surfaceRecords[at + RecordPolicy] = _surfaceRecordSnapshot[at + RecordPolicy] =
                (int)((flags >> 1) & 1);
        }
    }

    /// <summary>Opcode 0x259 script-entry lifecycle: clear record +0x08 and +0x0c for every surface in
    /// both tables.</summary>
    public void ClearSurfaceReloadPolicies()
    {
        lock (_lock)
        {
            _reloadableSurfaces.Clear();
            for (int at = 0; at < _surfaceRecords.Length; at += SurfaceRecordDwords)
            {
                _surfaceRecords[at + RecordReload] = _surfaceRecordSnapshot[at + RecordReload] = 0;
                _surfaceRecords[at + RecordPolicy] = _surfaceRecordSnapshot[at + RecordPolicy] = 0;
            }
        }
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
                WriteSurfaceRecord(slot, RecordResourceId, -1);   // retained_gfx_release_surface
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
            WriteSurfaceRecord(slot, RecordResourceId, -1);   // gfx_surface_create_blank@0x477370
            WriteSurfaceRecord(slot, RecordCreated, 1);
            _surfaces[slot] = (0, -1); // create-texture: real mutable pixels, no asset id or color key
            _createdSurfaces.Add(slot);
            _movieStopTimesMs.Remove(slot);
            MarkRetainedMutation();
        }
    }

    /// <summary>Op 0x1fa. retained_gfx_release_surface resets only the record's resource id; the color key
    /// and created flag stay until the slot is loaded or created again.</summary>
    public void ClearSurface(int slot)
    {
        lock (_lock)
        {
            WriteSurfaceRecord(slot, RecordResourceId, -1);
            _surfaces.Remove(slot);
            _createdSurfaces.Remove(slot);
            _movieStopTimesMs.Remove(slot);
            _surfaceTransitions.Remove(slot);
            _movieMaskTransitions.Remove(slot);
            MarkRetainedMutation();
        }
    }
}
