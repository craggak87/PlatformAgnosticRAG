---
equipment_id: ROBOT01
equipment_name: Cell A Bottleneck & Cycle Time Optimization Project
improvement_id: IMP-2026-01
engineer: A.Huza
date: 2026-09-28
criticality: Medium
tags:
  - improvements
  - cycletime
  - optimization
  - throughput
  - robotics
---

# Cell A Cycle Time Optimization Project - IMP-2026-01

## Executive Summary
This engineering project addressed manufacturing throughput constraints in Cell A. Baseline cell cycle time of **34.2 seconds per component** constrained line throughput below target OEE targets. Following coordinated optimization across robot trajectory motion, camera trigger sequencing, and conveyor pacing, cycle time was reduced to **27.8 seconds per component**, delivering an **18.7% throughput improvement**.

## Identified Bottlenecks
1. **Robotic Wait on Vision**: [[ROBOT01 ABB IRB4600]] spent an average of 2.4 seconds idling at approach point `pPrePick` waiting for inspection results from [[CAM01 Inspection]].
2. **Gripper Dwell Overshoot**: Excessive timer setting in [[PLC01 Siemens S7-1500]] holding 400 ms dwell after gripper proximity sensor closure on input `I:0/15` (`I1.15`).
3. **Conveyor Infeed Deceleration**: [[CONV01 Conveyor]] operated at fixed 0.25 m/s speed driven by [[VFD01 Inverter]], creating transit lag between bulk feeder and camera inspection.

## Implemented Engineering Improvements
### 1. Vision On-The-Fly Triggering
- Re-architected camera triggering: [[CAM01 Inspection]] now triggers while the part is in motion across photo-eye PE-02 at an adjusted conveyor velocity of 0.35 m/s.
- Reduced camera exposure time from 15 ms to 8 ms and boosted ambient ring lighting strobe intensity, preventing motion blur while delivering verified dimensional results before robot approach.

### 2. Motion Subroutine Zone Optimization
- Refactored RAPID trajectories in [[IRB4600 Motion Subroutines]]:
  - Changed intermediate path points from `fine` stop points to fly-by corner zones `z30` and `z50`.
  - Tuned axis acceleration parameters `AccSet 85, 85` on non-payload return motions.
  - Eliminated unnecessary 400 ms PLC wait timer upon receipt of gripper closed confirmation bit `I:0/15`.

### 3. Dynamic Conveyor VFD Pacing
- Implemented multi-speed profile in [[Conveyor Running Logic]]:
  - Rapid transport (45 Hz / 0.40 m/s) when queue is empty.
  - Creep approach (15 Hz / 0.12 m/s) over final 80 mm onto mechanical escapement stop.

## Verification & Production Results
- Cycle time logged over 5,000 production cycles:
  - Mean cycle time: **27.8 sec** (sigma = 0.32 sec).
  - No increase in gripper false timeouts or vision false rejects (REJ-03).
  - Monthly capacity increased by 1,420 finished units per shift.

## Related Documents
- Robot Motion: [[IRB4600 Motion Subroutines]]
- Vision System: [[CAM01 Inspection]]
- Conveyor Control: [[Conveyor Running Logic]]
- Drive Inverter: [[VFD01 Inverter]]
