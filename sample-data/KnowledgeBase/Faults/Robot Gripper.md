---
fault_id: FLT-002
equipment_id: ROBOT01
criticality: High
fault_type: Pneumatic / Electrical
tags:
  - fault
  - gripper
  - timeout
  - sensor
  - pneumatics
  - robot
---

# Robot Gripper Fault - Timeout and Part Clamping Failure

## Symptoms
- The cell cycle aborts during robot part pick or drop operations with alarm `Gripper Timeout Fault`.
- The robot [[ROBOT01 ABB IRB4600]] halts over [[CONV01 Conveyor]] or fixture station waiting for clamp confirmation.
- Digital input indicators for gripper open (`I:0/15` / `I1.15`) or gripper closed fail to illuminate on the PLC input rack within the 1.2 second timeout window.

## Cause
The PLC sequence logic in [[PLC01 Siemens S7-1500]] commands the pneumatic gripper solenoid to actuate, but the confirmation signal from the end-of-arm tooling magnetic reed switch is not received before timer `T_GripTimeout` expires.

## Root Cause
Detailed maintenance investigations identify three predominant root causes:
1. **Cable Damage on I:0/15 and I1.15**: Continuous flexing, torsion, and repetitive bending of the robot dress pack along Axis 5 and Axis 6 degrades the internal conductor strands. Intermittent open circuits develop on the signal return line for input `I:0/15` (Siemens addressing `I1.15`).
2. **Pneumatic Valve Sticking**: The 5/2 directional pneumatic solenoid valve controlling gripper cylinder stroke sticks due to lubricant wash-out, particulate contamination, or moisture in the supply line from [[Pneumatic Main]].
3. **Proximity Sensor Failure**: The miniature magnetic proximity switch mounted in the pneumatic cylinder tie-rod slot fails internally, vibrates loose from its calibrated position, or suffers physical impact damage during handling.

## Diagnostics
1. **LED Status Inspection**: Observe the LED status on proximity sensors `GS-01` (Grip Open) and `GS-02` (Grip Closed) on the robot wrist while manually activating the solenoid override.
2. **PLC Input Status**: Open the TIA Portal watch table on [[PLC01 Siemens S7-1500]] and monitor inputs `I:0/15` (alias `I1.15`) and `I:0/16` (`I1.16`). If the sensor LED lights up but the PLC bit does not toggle, verify cable continuity across dress pack junction box `JB-R01`.
3. **Pneumatic Pressure Check**: Verify main cell air pressure at [[Pneumatic Main]] regulator gauge. Pressure must be at least 6.0 bar. If pressure drops below 5.5 bar during gripper actuation, see [[ALM-102 Low Pneumatic Pressure]].
4. **Manual Valve Override**: Depress the manual test detent on valve slice `V01-4` on the SMC manifold. If the gripper does not actuate crisply, clean or replace the spool assembly.

## Corrective Action
- If cable damage is found on `I:0/15` / `I1.15`, replace the flexible sensor lead using high-flex robotic polyurethane cabling (minimum 10 million flex cycle rating) per [[Gripper Sensor Routing]].
- If the pneumatic valve is sticking, flush the valve body with approved pneumatic cleaner, verify supply airline filtration, or replace the solenoid slice.
- If the proximity sensor is misaligned or defective, readjust sensor positioning until the LED activates reliably at 0.5 mm before end of mechanical stroke; secure clamp screw to 0.4 Nm.
- Reset the fault condition on the HMI and clear alarm via [[PLC01 Siemens S7-1500]].

## Prevention
- Adhere to the cable routing specifications documented in [[Gripper Sensor Routing]] to prevent pinching and excessive strain relief tension at Axis 6.
- Inspect pneumatic lubricator and desiccators monthly per [[Preventive Maintenance Robot]].

## Downtime
Historical downtime ranges from 15 minutes (sensor repositioning / valve free-up) to 90 minutes (dress pack cable replacement).

## References
- Robot Equipment: [[ROBOT01 ABB IRB4600]]
- Lessons Learned: [[Gripper Sensor Routing]]
- Pneumatics Supply: [[Pneumatics/Pneumatic Main.md]]
- Equipment Manual: [[ROBOT01_ABB_IRB4600_Manual.pdf]]
