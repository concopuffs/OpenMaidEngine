-- The Himegari guide's experience patch as a subscriber of himegari-base's event: no address, script or
-- fingerprint, and no vm capability. The event's amount field is the award, after the multipliers.
function on_award(event)
  event.amount = event.amount * 2
end
