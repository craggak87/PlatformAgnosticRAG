---
fault_id: FLT-001
equipment_id: ROBOT01
criticality: High
fault_type: Mechanical / Electrical
tags:
  - fault
  - robot
  - calibration
  - home-position
  - limit-switch
---

# Robot Home Position Fault - FLT-001

## Symptoms
- The cell cycle halts during startup or home routine initialization with alarm `FLT-001 Robot Home Position Failure`.
- The robot manipulator [[ROBOT01 ABB IRB4600]] stops in manual or automatic mode and displays IRC5 system error 50024 ("Corner zone execution failed") or error 50133 ("Axis calibration lost").
- The robot arm is physically stopped outside the home position boundary box (+/- 1.5 mm tolerance).

## Cause
Investigation indicates the IRC5 controller failed to satisfy the home position synchronization conditions during routine checking of the axis resolvers against recorded revolution counter offsets.

## Root Cause
Diagnostic review of historical occurrences reveals three root causes:
1. **Axis Calibration Loss**: Battery backup exhaustion in the serial measurement board (SMB) located in the robot base, or unexpected power disconnection while an axis was moving, wiping resolver revolution counter sync data.
2. **Limit Switch Misalignment**: The physical home position confirmation limit switch bracket on Axis 2 / Axis 3 shifted due to vibration or mechanical shock during high-acceleration transfer maneuvers.
3. **Mechanical Stop Wear**: Repeated hard positioning contacts against the internal polyurethane mechanical stops have worn down the physical datum reference point by > 2 mm.

## Diagnostics
1. Check IRC5 Teach Pendant event logs under `System Event Messages -> Motion`.
2. Inspect revolution counter update status in `Calibration -> SMB Memory` on the FlexPendant.
3. Visually inspect the mechanical vernier zero marks on Axis 1 through Axis 6.
4. Verify limit switch alignment and confirm digital input signal transition in [[PLC01 Siemens S7-1500]].

## Corrective Action
1. Jog the robot in axis-by-axis manual mode at < 5% speed to align the physical alignment marks on all six joints.
2. On the IRC5 FlexPendant, navigate to `Calibration -> Update Revolution Counters`, select the uncalibrated axis, and confirm calibration.
3. Check and tighten mounting bolts on the home confirmation proximity sensors. Re-align brackets if misalignment is detected.
4. Inspect mechanical stops for grooving or compression damage; replace worn buffer blocks per [[Preventive Maintenance Robot]].
5. Execute the homing verification program `r_HomeCheck` in [[IRB4600 Motion Subroutines]].

## Prevention
- Replace SMB lithium battery pack every 24 months during scheduled preventive maintenance.
- Apply thread-locking compound (Loctite 243) on all limit switch mounting fasteners.
- Audit zero-mark alignment weekly during shift changeover.

## Downtime
Typical MTTR is 20 to 45 minutes when revolution counter update is performed by a qualified maintenance technician.

## References
- Robot Equipment: [[ROBOT01 ABB IRB4600]]
- Subroutines: [[IRB4600 Motion Subroutines]]
- Maintenance Guide: [[Preventive Maintenance Robot]]
- User Manual: [[ROBOT01_ABB_IRB4600_Manual.pdf]]
