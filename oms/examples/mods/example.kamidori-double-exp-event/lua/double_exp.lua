-- Kamidori's experience award as a subscriber of kamidori-base's event: no address, script or fingerprint, and no
-- vm capability. The event's amount field is the award, after the costume bonus.
function on_award(event)
  event.amount = event.amount * 2
end
