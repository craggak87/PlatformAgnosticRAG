---
equipment_id: ROBOT01
equipment_name: Robot Preventive Maintenance Protocol
maintenance_type: Preventive
engineer: A.Huza
date: 2026-10-15
criticality: High
tags:
  - maintenance
  - robot
  - lubrication
  - battery
  - inspection
---

# Preventive Maintenance Protocol - ROBOT01 ABB IRB4600

## Scope & Frequency
This maintenance protocol defines routine inspection, lubrication, and electrical check procedures required every 500 operating hours (monthly) and 6000 operating hours (annual) for robot manipulator [[ROBOT01 ABB IRB4600]] in Cell A.

## Monthly Inspection Checklist (500 Hours)
1. **Visual Inspection**:
   - Inspect mechanical body and casting for oil seepage around gearboxes (Axes 1 through 6).
   - Check condition of wrist cable harness and pneumatic hoses; inspect for abrasion or twisting per [[Gripper Sensor Routing]].
2. **End-of-Arm Tooling & Gripper**:
   - Inspect magnetic proximity switch clamp tightness and cable strain relief on inputs `I:0/15` / `I1.15` (see [[Robot Gripper]]).
   - Clean gripper finger gripping pads; replace polyurethane friction inserts if worn.
   - Verify pneumatic lubricator oil level and drain condensed water at [[Pneumatics/Pneumatic Main.md]].
3. **Safety Verification**:
   - Test functional tripping of [[Safety Gate 2]] interlocks and verify alarm [[ALM-205 Safety Gate Open]] triggers promptly.
   - Check teach pendant enabling device three-position deadman switch functionality.

## Annual Servicing Protocol (6000 Hours)
1. **Gearbox Oil Sampling & Replacement**:
   - Drain, sample, and replenish gearbox lubricants on Axes 1, 2, and 3 using ABB approved synthetic gear grease (Shell Gadus S2 V220AD 2).
2. **Measurement System Battery Pack**:
   - Replace the SMB lithium battery unit (part number 3HAC044075-001) in the manipulator base before voltage drops below 3.4V DC to prevent axis calibration loss (see [[FLT-001 Robot Home Position]]).
3. **Brake Hold Torque Test**:
   - Perform automated brake holding torque check on all axes via RobotStudio service routine `BrakeCheck`.
4. **Mechanical Stop & Vernier Inspection**:
   - Verify mechanical stop buffer condition on Axis 1 and Axis 2.
   - Verify alignment of vernier zero marks across all axes in home position.

## Sign-Off & Verification
- Lead Maintenance Engineer: A.Huza
- Date Completed: 2026-10-15
- All calibration offsets verified in IRC5 controller memory.

## Related Documents
- Equipment Overview: [[ROBOT01 ABB IRB4600]]
- Homing Fault Guide: [[FLT-001 Robot Home Position]]
- Gripper Troubleshooting: [[Robot Gripper]]
- Robot Manual: [[ROBOT01_ABB_IRB4600_Manual.pdf]]
