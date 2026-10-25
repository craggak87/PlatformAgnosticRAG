---
equipment_id: ROBOT01
date: 2026-10-13
engineer: A.Huza
criticality: Medium
tags:
  - lessons-learned
  - robot
  - gripper
  - cabling
  - sensors
---

# Lessons Learned - Robot Gripper Sensor Cable Routing and Strain Relief

## Event
Recurring intermittent gripper timeout alarms on [[ROBOT01 ABB IRB4600]] resulting in unexpected line stops and over 4.5 hours of accumulated downtime during Week 41. The issue was initially misdiagnosed as pneumatic cylinder seal leakage before the true root cause was identified.

## What Happened
During high-speed part transfer motions, the robot controller repeatedly tripped on alarm `Gripper Timeout` due to loss of the gripper open/closed confirmation signal wired to digital inputs `I:0/15` and `I1.15` in [[PLC01 Siemens S7-1500]] (see [[Robot Gripper]]).
Maintenance technicians repeatedly adjusted the pneumatic regulator pressure at [[Pneumatics/Pneumatic Main.md]] and replaced pneumatic valve slices on the SMC manifold without resolving the root failure.
Eventually, continuity logging during live motion demonstrated that the internal copper strands inside the M8 sensor cable had severed due to severe torsional fatigue across Axis 6 of the robot arm.

## What Was Missed
1. **Cable Bend Radius Violation**: The replacement sensor cable installed during previous overhaul had a minimum dynamic bend radius of 65 mm, but the mechanical tie-wrap anchor forced a bend radius of under 22 mm at the wrist articulation joint.
2. **Improper Fastener Clamping**: Standard nylon zip-ties were cinched tightly against the cable jacket without protective rubber grommets, creating a localized pinch point and accelerating copper work-hardening.
3. **Motion Envelope Testing**: Cable routing was only checked in the static home position, failing to observe severe cable tension when Axis 5 and Axis 6 rotated simultaneously to -135 degrees during part unload.

## Better Approach
- Installed a dedicated robotic dress pack with high-flex continuous-flex polyurethane cable (rated for > 10 million torsional cycles).
- Implemented a low-friction corrugated conduit bracket on the robot forearm with a spring-loaded retraction retractor that absorbs cable slack during wrist reorientation.
- Replaced rigid zip-ties with hook-and-loop velcro wraps and cushioned elastomer clamps.
- Programmed a diagnostic test routine in RAPID (`r_CableCheck`) that rotates all axes through extreme limits while continuously reading sensor input bits.

## Future Recommendation
- Standardize all end-of-arm tooling sensor wiring across all robotic cells to continuous-flex PUR rated cabling.
- Document maximum permissible twist angles and minimum bend radii directly on mechanical tooling assembly drawings.
- Include wrist cable inspection and dynamic continuity testing in the monthly preventive maintenance checklist per [[Preventive Maintenance Robot]].

## Related Documents
- Gripper Fault Record: [[Robot Gripper]]
- Robot Equipment: [[ROBOT01 ABB IRB4600]]
- Preventive Maintenance: [[Preventive Maintenance Robot]]
- Motion Routines: [[IRB4600 Motion Subroutines]]
