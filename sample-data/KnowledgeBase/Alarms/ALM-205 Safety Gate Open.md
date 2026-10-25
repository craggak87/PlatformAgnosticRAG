---
alarm_id: ALM-205
severity: Critical
equipment_id: ROBOT01
tags:
  - alarm
  - safety
  - gate
  - interlock
  - e102
---

# Alarm ALM-205 - Safety Gate Open Interlock Trip

## Description
Alarm `ALM-205 Safety Gate Open` is raised when the perimeter safety fencing barrier interlock on Cell A opens or trips during active operation. This immediately drops the 24V DC safety circuit loop, tripping the primary Pilz safety relay and dropping power to robot servo contactors and conveyor drive motor outputs.

## Trigger Condition
The alarm triggers when the dual-channel safety contacts on [[Safety Gate 2]] open or fail to maintain electrical continuity into safety inputs `I:0/10` and `I:0/11` on [[PLC01 Siemens S7-1500]], as wired per schematic [[E102 Safety Circuit Schematic]].

## Likely Causes
1. **Safety Gate Interlock Open**: An operator or maintenance engineer opened the physical perimeter door without requesting an orderly cell pause cycle via the operator entry pushbutton.
2. **Safety Circuit Loop Disruption**: Loose terminal terminations, corroded quick-disconnect M12 safety connectors, or wire fatigue along the safety circuit loop between safety gate 2 interlock switch and the electrical panel.
3. **Switch Actuator Misalignment**: Physical mechanical sag in the perimeter gate hinges causing the RFID magnetic tongue actuator to disengage from the receiver switch head (distance exceeding 5 mm limit).
4. **Channel Discrepancy Fault**: One contact open while the second remains closed for longer than 250 ms, causing an internal safety relay channel cross-fault lockout per drawing [[E102 Safety Circuit Schematic]].

## Troubleshooting Steps
1. **Verify Physical Perimeter Closure**: Check that Safety Gate 2 is firmly latched, handle turned, and locking bolt engaged.
2. **Inspect Switch Status LEDs**: Examine the multicolor diagnostic LED on the Pilz PSENcode interlock switch on the gate post:
   - Green: Normal closed operation, both channels healthy.
   - Flashing Red: Channel discrepancy or timing error.
   - Solid Red: Actuator absent or circuit broken.
3. **Trace Schematic E102**: Using drawing [[E102 Safety Circuit Schematic]], trace the dual-channel loop lines 104 and 105 through test points `TP-S1` and `TP-S2` back to inputs `I:0/10` and `I:0/11`.
4. **Inspect Safety Relay**: Check safety relay `K1` in Cabinet 1 for `RUN` and `CH1`/`CH2` status indicators.

## Recovery
1. Physically close and latch Safety Gate 2.
2. Verify gate alignment is within tolerance.
3. Press the illuminated blue "Safety Reset" pushbutton on the cell operator console to reset relay `K1`.
4. Acknowledge and clear alarm ALM-205 on the HMI terminal.
5. Re-engage drives and restart cell automatic sequence.

## Related Documents
- Safety Interlock Details: [[Safety Gate 2]]
- Schematic: [[E102 Safety Circuit Schematic]]
- Manual: [[Safety_Interlock_Pilz_Manual.pdf]]
- Cell Procedures: [[Lockout Tagout Cell A]]
