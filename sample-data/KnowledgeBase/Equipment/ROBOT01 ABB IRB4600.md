---
equipment_id: ROBOT01
equipment_name: Robot Cell 1
manufacturer: ABB
model: IRB4600
location: Cell A
criticality: High
tags:
  - robot
  - abb
  - manipulator
  - motion
---

# Robot Cell 1 - ABB IRB4600 Manipulator

## Purpose
The ROBOT01 manipulator is a 6-axis industrial robot installed in Cell A. Its primary manufacturing function is material handling, high-speed part transfer between infeed conveyor [[CONV01 Conveyor]] and machining fixtures, and end-of-arm tooling operations using pneumatic gripper mechanisms.

## Key Components
- **Manipulator Body**: ABB IRB4600-40/2.55 (payload 40kg, reach 2.55m).
- **Controller**: ABB IRC5 single cabinet controller located adjacent to main cell electrical cabinet.
- **End Effector Tooling**: Dual pneumatic angular gripper with magnetic proximity sensors for part clamp/unclamp detection.
- **Drive System**: Brushless AC servomotors with absolute serial encoders across axes 1 through 6.
- **Safety Interlock**: Integrated dual-channel safety stop connected to [[Safety Gate 2]] and Pilz safety relay via schematic [[E102 Safety Circuit Schematic]].

## Communication
- **Fieldbus**: PROFINET IO Device interfacing directly with master controller [[PLC01 Siemens S7-1500]].
- **I/O Mapping**:
  - Byte 0-3: Cell coordination handshakes (Cycle Start, At Home, Part Picked, Part Placed).
  - Byte 4-7: Gripper control outputs and digital sensor feedback inputs.
  - Byte 8-15: Speed override and motion status telemetry.
- **Standard Service Port**: Service Ethernet port X5 configured at IP `192.168.125.1` for RobotStudio diagnostics.

## Known Issues
- Periodic axis synchronisation drift following emergency stops or cell power loss; refer to [[FLT-001 Robot Home Position]].
- Gripper feedback timeout due to pneumatic sensor cabling strain on axis 5/6 bend radius; refer to [[Robot Gripper]].
- Interlock trip events triggered by gate opening during active motion subroutines; refer to [[ALM-205 Safety Gate Open]].

## Related Documents
- Equipment Manual: [[ROBOT01_ABB_IRB4600_Manual.pdf]]
- Wiring Schematic: [[E102 Safety Circuit Schematic]]
- Motion Logic: [[IRB4600 Motion Subroutines]]
- Maintenance Guide: [[Preventive Maintenance Robot]]
