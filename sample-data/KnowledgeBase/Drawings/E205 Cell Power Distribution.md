---
equipment_id: PLC01
equipment_name: Cell Power Distribution Schematic
manufacturer: EPLAN / Engineering Services
model: Drawing E205 Rev 2
location: Electrical Cabinet 1
criticality: High
tags:
  - drawing
  - electrical
  - schematic
  - power-distribution
  - e205
---

# Drawing E205 - Cell Power Distribution Schematic

## Drawing Metadata
- **Drawing Number**: E205
- **Sheet**: 1 of 6 (Primary Power Supply & Sub-Distribution)
- **Revision**: Rev 2.1 (Approved 2026-07-20)
- **Associated Panels**: Main Electrical Cabinet 1 (CP-01), Operator Station (CP-02).
- **Associated Equipment**: [[PLC01 Siemens S7-1500]], [[VFD01 Inverter]], [[ROBOT01 ABB IRB4600]], [[CAM01 Inspection]].

## Incoming Mains Supply
- **Mains Voltage**: 400V AC 3-phase + Neutral + PE (50 Hz), 63A main breaker `Q1`.
- **Surge Protection**: Dehn Class I+II modular surge protection device `SPD-01` on primary busbars.
- **Main Disconnect Switch**: 4-pole rotary lockout switch mounted on Cabinet 1 door per [[Lockout Tagout Cell A]].

## Sub-Circuit Distribution & Breaker Allocation
- **Branch Q2 (25A C-Curve)**: Feeds ABB IRC5 robot controller for [[ROBOT01 ABB IRB4600]].
- **Branch Q3 (16A C-Curve)**: Feeds [[VFD01 Inverter]] via line choke `L1` (supplying [[CONV01 Conveyor]]).
- **Branch Q4 (10A B-Curve)**: Feeds 24V DC regulated power supply `PS-01` (40A SITOP PSU8200) for [[PLC01 Siemens S7-1500]] CPU and general I/O modules.
- **Branch Q5 (10A B-Curve)**: Feeds 24V DC fail-safe power supply `PS-02` (20A) for safety relays and [[Safety Gate 2]] interlocks.
- **Branch Q6 (6A B-Curve)**: Feeds vision system [[CAM01 Inspection]], camera ring lighting, and network switches.

## Grounding & EMC Standards
- All shielded communication lines (PROFINET, analog 0-10V reference cables to VFD01) must have braid shields clamped directly to the copper EMC grounding busbar at entry into Cabinet 1.
- Star-point earthing executed to ensure zero ground loop voltage between PLC analog common and VFD analog return.

## Related Documents
- Safety Schematic: [[E102 Safety Circuit Schematic]]
- Electrical Note: [[Electrical/Main Power Distribution.md]]
- Inverter Specification: [[VFD01 Inverter]]
- Controller: [[PLC01 Siemens S7-1500]]
