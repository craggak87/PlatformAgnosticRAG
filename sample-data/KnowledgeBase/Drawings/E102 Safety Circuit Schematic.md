---
equipment_id: PLC01
equipment_name: Safety Circuit Electrical Schematic
manufacturer: EPLAN / Engineering Services
model: Drawing E102 Rev 3
location: Electrical Cabinet 1
criticality: Critical
tags:
  - drawing
  - electrical
  - schematic
  - safety
  - e102
---

# Drawing E102 - Safety Circuit Schematic

## Drawing Metadata
- **Drawing Number**: E102
- **Sheet**: 1 of 4 (Master Safety Interlock Architecture)
- **Revision**: Rev 3.2 (Approved 2026-08-15)
- **CAD File**: `EPLAN_CellA_E102_Rev3.edz`
- **Associated Equipment**: [[PLC01 Siemens S7-1500]], [[Safety Gate 2]], [[ROBOT01 ABB IRB4600]], Pilz Safety Relay `K1`.

## Circuit Overview
Drawing E102 documents the Category 4 / PL e fail-safe wiring architecture for manufacturing Cell A. It defines the series and parallel interlock loops linking operator emergency stop pushbuttons, perimeter safety gates, and light curtains directly to safety relay `K1` and fail-safe PLC inputs on [[PLC01 Siemens S7-1500]].

## Key Terminals & Dual-Channel Signal Lines
- **Power Rail 101**: +24V DC Failsafe Supply from safety power supply `PS-02`.
- **Line 104 (Channel 1 Input)**:
  - Connects Pilz PSENcode switch contact 1 via terminal TB1-21 to PLC input `I:0/10`.
  - Monitored by PLC safety test pulse `T0`.
- **Line 105 (Channel 2 Input)**:
  - Connects Pilz PSENcode switch contact 2 via terminal TB1-22 to PLC input `I:0/11`.
  - Monitored by PLC safety test pulse `T1`.
- **Discrepancy Monitoring**: Channel discrepancy time limit is configured in TIA Portal at 250 ms. Exceeding this limit logs a safety discrepancy fault and generates alarm [[ALM-205 Safety Gate Open]].
- **Contactor Outputs**:
  - `K1-13/14`: Redundant contactor series loop controlling 400V 3-phase power to robot drive unit `U1`.
  - `K1-23/24`: Contactor loop enabling safe torque off (STO) on [[VFD01 Inverter]].

## Safety Gate 2 Interlock Loop
As detailed in Sheet 2, Section D:
- Terminal TB1-21 feeds Safety Gate 2 Channel 1 to `I:0/10`.
- Terminal TB1-22 feeds Safety Gate 2 Channel 2 to `I:0/11`.
- Ground return TB1-20 connects the shielded drain wire to chassis PE.

## Maintenance Notes
- Always check test points `TP-S1` and `TP-S2` on drawing E102 before replacing any safety interlock sensor.
- When replacing cable runs, only low-capacitance shielded twisted pair cable (AWG 22) must be used.

## Related Documents
- Perimeter Interlock: [[Safety Gate 2]]
- Safety Gate Alarm: [[ALM-205 Safety Gate Open]]
- Main Power Schematic: [[E205 Cell Power Distribution]]
- Safety Manual: [[Safety_Interlock_Pilz_Manual.pdf]]
