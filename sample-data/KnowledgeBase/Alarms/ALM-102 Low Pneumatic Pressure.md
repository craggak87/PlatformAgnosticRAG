---
alarm_id: ALM-102
severity: High
equipment_id: ROBOT01
tags:
  - alarm
  - pneumatics
  - pressure
  - regulator
  - manifold
---

# Alarm ALM-102 - Low Pneumatic Pressure Alarm

## Description
Alarm `ALM-102 Low Pneumatic Pressure` indicates that incoming compressed air pressure to the manufacturing cell has fallen below the minimum safe operational threshold. Low pneumatic pressure compromises gripper clamping force on [[ROBOT01 ABB IRB4600]] and can cause dropped parts or erratic cylinder motions.

## Trigger Condition
The pressure switch digital contact on the air preparation unit drops out when line pressure measured downstream of the main regulator falls below 5.5 bar (normal setpoint: 6.2 bar). The digital input bit in [[PLC01 Siemens S7-1500]] transitions to false, halting automatic cycle start.

## Likely Causes
1. **Main Regulator Pressure Drop Below 5.5 bar**: The primary filter-regulator on [[Pneumatic Main]] has experienced an upstream plant air header pressure drop, an unscheduled plant compressor cycle dip, or an internal diaphragm spring failure causing the delivery pressure to collapse below 5.5 bar.
2. **Air Leak in Valve Manifold**: Severe pneumatic leakage on the SMC valve manifold block, worn O-ring seals between valve slices, loose quick-connect push-in fittings, or split polyurethane tubing feeding end-of-arm tooling.
3. **Clogged Filter Element**: The 5-micron coalescing filter cartridge in the FRL unit on [[Pneumatic Main]] is heavily saturated with oil or condensate sludge, causing high delta-P under dynamic airflow demand.
4. **Exhaust Dump Valve Tripped**: The automated safety soft-start/quick-exhaust dump valve did not fully seat or lost pilot control voltage.

## Troubleshooting Steps
1. **Check System Pressure Gauge**: Read the mechanical analog pressure gauge on [[Pneumatics/Pneumatic Main.md]]. If indicated pressure is below 5.5 bar, adjust the regulator adjustment knob clockwise while monitoring the digital sensor.
2. **Acoustic Leak Detection**: Listen for audible hissing around the main manifold and valve islands. Inspect the flexible supply lines feeding the robot gripper.
3. **Inspect Filter Bowls**: Check the auto-drain condensate bowl on the air preparation unit; purge any accumulated moisture or oil.
4. **Verify PLC Pressure Switch Input**: Confirm that digital input `I0.7` (`PNEUMATIC_OK`) is energized at the PLC terminal strip when pressure is above 5.8 bar.

## Recovery
1. Rectify air leaks and ensure regulator maintains stable 6.0 to 6.5 bar line pressure.
2. Ensure pressure switch LED turns green.
3. Acknowledge and clear alarm ALM-102 on the HMI.
4. Resume cell production sequence.

## Related Documents
- Pneumatics System Note: [[Pneumatics/Pneumatic Main.md]]
- Gripper Troubleshooting: [[Robot Gripper]]
- System Manual: [[Pneumatics_SMC_SystemManual.pdf]]
- Preventive Maintenance: [[Preventive Maintenance Robot]]
