-- The Lua version of the Himegari guide's experience patch (docs/modding/games/himegari.md, "Patch points").
-- The point is reached once per award, after the multipliers; L[0x1] holds the amount the unit receives.
function on_award(ctx)
  maid.vm.l[0x1] = maid.vm.l[0x1] * 2
end
