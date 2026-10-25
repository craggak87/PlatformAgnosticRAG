---
equipment_id: ROBOT01
equipment_name: ABB IRB4600 RAPID Motion Routines
manufacturer: ABB
model: IRB4600
location: Cell A
criticality: High
tags:
  - robot
  - rapid
  - motion
  - subroutine
  - abb
---

# ABB IRB4600 RAPID Motion Subroutines

## Overview
This document specifies the standard RAPID motion routines, tool data declarations, and home verification algorithms executed by [[ROBOT01 ABB IRB4600]] in Cell A under supervisory control by [[PLC01 Siemens S7-1500]].

## Standard Coordinate Systems & Tool Data
- **Tool Center Point (tooldata `tGripper`)**:
  - `tGripper.tframe.trans`: `[0.0, 0.0, 245.8]` mm from mounting flange.
  - `tGripper.tframe.rot`: `[1.0, 0.0, 0.0, 0.0]` (standard Euler orientation).
  - `tGripper.tload.mass`: 4.2 kg (includes gripper body, pneumatic cylinders, and sensor harness).
- **Work Object (wobjdata `wobjConveyor`)**:
  - Calibrated against the pick datum reference point on [[CONV01 Conveyor]].

## Core Motion Subroutines
### 1. `r_HomeCheck`
- **Purpose**: Verifies that the manipulator is physically within the safe home position envelope before enabling automatic cycle start.
- **Logic**: Reads joint target `jtHome` (`[0, -20, 25, 0, 45, 0]`) with joint tolerance `\Joint:=5`. If actual axis position deviates or revolution counter is lost (see [[FLT-001 Robot Home Position]]), execution jumps to error handler and posts `ERR_NOT_AT_HOME` to PLC.

### 2. `r_PickPartFromConveyor`
- **Purpose**: High-speed approach, deceleration, grasping, and lift-off of inspected components from the infeed conveyor.
- **Sequence**:
  1. Requests robot zone entry clearance bit to [[PLC01 Siemens S7-1500]].
  2. Moves via `MoveJ` with zone `z50` to pre-pick position `pPrePick` (150 mm above part).
  3. Executes linear move `MoveL` with fine zone `fine` and speed `v200` to `pPickActual`.
  4. Calls gripper clamp routine `r_CloseGripper`.
  5. Waits for digital input `diGripperClosed` (`I:0/15` / `I1.15`) within 1.2s timeout window. If timeout occurs, triggers fault [[Robot Gripper]].
  6. Executes `MoveL` retreat to `pPrePick` at speed `v1000`.

### 3. `r_PlaceInFixture`
- Places oriented part into precision machining clamp, triggers pneumatic clamp sequence, releases gripper, and confirms unclamp signal before rapid exit.

## Error Recovery Routines
- In the event of a safety gate trip [[ALM-205 Safety Gate Open]], the controller stores the current trajectory vector and halts motion using category 1 controlled path deceleration.
- On safety reset, the routine `r_ResumeMotion` executes a safe retreat vector before resuming cycle.

## Related Documents
- Robot Equipment: [[ROBOT01 ABB IRB4600]]
- Homing Fault: [[FLT-001 Robot Home Position]]
- Gripper Fault: [[Robot Gripper]]
- Robot Manual: [[ROBOT01_ABB_IRB4600_Manual.pdf]]
