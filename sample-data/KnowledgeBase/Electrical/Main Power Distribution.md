---
equipment_id: PLC01
equipment_name: Main Electrical Panel Enclosure CP-01
manufacturer: Rittal / Siemens
model: TS8 System Enclosure
location: Cell A Cabinet 1
criticality: High
tags:
  - electrical
  - panel
  - power
  - distribution
  - e205
---

# Main Power Distribution Panel - CP-01

## Purpose
Enclosure CP-01 houses the incoming 400V 3-phase power distribution, primary switchgear, auxiliary 24V DC power supplies, motor drive inverter [[VFD01 Inverter]], master controller [[PLC01 Siemens S7-1500]], and safety relay logic for manufacturing Cell A.

## Power Supply Specifications
- **Mains Input**: 400V AC +/- 10%, 50 Hz, 3-phase, neutral, and protective earth (PE).
- **Full Load Amperage (FLA)**: 52 Amperes.
- **Main Isolator**: 63A 4-pole rotary lockable switch operated via external door handle per [[Lockout Tagout Cell A]].
- **Surge Protective Device**: Type 1+2 surge arrester protecting against lightning transients and grid switching surges.

## Sub-Panel Distribution
- **Busbar System**: Rittal RiLine 60mm compact busbar system (rated 250A).
- **VFD Feeder**: Fed through motor circuit breaker `Q3` and 3-phase line reactor to [[VFD01 Inverter]].
- **Robot Feeder**: Circuit breaker `Q2` wired to ABB IRC5 controller terminal block via heavy-duty flexible armored cable.
- **DC Power Generation**:
  - `PS-01`: Siemens SITOP PSU8200 (400V 3AC input, 24V DC / 40A output) for PLC, sensor bus, and solenoid valves.
  - `PS-02`: Puls Dimension (24V DC / 20A output) dedicated strictly to fail-safe safety relays and [[Safety Gate 2]] interlocks.

## Environmental Control & Maintenance
- **Thermal Management**: Rittal TopTherm thermoelectric air conditioning unit mounted on cabinet side door, maintaining internal cabinet temperature between 28°C and 32°C.
- **Filter Mats**: Checked and vacuumed monthly during cell preventive maintenance.
- **Thermal Imaging Inspection**: Annual thermographic scan of busbar joints and breaker terminals to identify loose high-resistance terminations.

## Related Documents
- Schematic: [[E205 Cell Power Distribution]]
- Safety Circuit: [[E102 Safety Circuit Schematic]]
- Drive Inverter: [[VFD01 Inverter]]
- Controller: [[PLC01 Siemens S7-1500]]
- Lockout Procedure: [[Lockout Tagout Cell A]]
