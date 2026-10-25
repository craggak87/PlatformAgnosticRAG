---
equipment_id: VFD01
equipment_name: Conveyor Inverter Drive
manufacturer: Rockwell Automation / Allen-Bradley
model: PowerFlex 525
location: Cell A Cabinet 1
criticality: High
tags:
  - vfd
  - drive
  - inverter
  - electrical
  - motor
---

# Conveyor Inverter Drive - VFD01

## Purpose
VFD01 is an Allen-Bradley PowerFlex 525 variable frequency drive rated for 1.5 kW (2.0 HP) output at 400V 3-phase. It supplies adjustable frequency power to the infeed conveyor motor on [[CONV01 Conveyor]], providing soft acceleration, controlled deceleration, and variable transport velocity.

## Key Components
- **Power Module**: 25B-D4P0N104 series frame A power section with integrated IGBT switching.
- **Control Module**: Detachable control module with multi-language LED keypad and USB programming interface.
- **Dynamic Braking Resistor**: 100 Ohm 300W braking resistor mounted external to the drive housing for rapid stop dissipation.
- **Line Reactor**: 3% impedance line reactor on the 400V 3-phase mains side supplied from [[E205 Cell Power Distribution]].

## Control & Wiring Interface
- **Digital Inputs**:
  - Terminal 01 (Stop): Wired to safety contact loop and emergency stop relay.
  - Terminal 02 (Start / Run Forward): Sourced from [[PLC01 Siemens S7-1500]] digital output `Q0.0`.
  - Terminal 04 (Fault Reset): Sourced from PLC digital output `Q0.1`.
- **Analog Input**:
  - Terminal 13 (0-10V DC In): Analog speed reference voltage from PLC module `AQ0`.
  - Terminal 14 (Analog Common): Shielded analog reference ground.

## Known Issues & Diagnostics
### Fault F004 Overcurrent
When the drive trips with display code `F004 Overcurrent`, the drive software has detected that output current has exceeded the instantaneous current trip limit (typically 200% of drive rated current).
Troubleshooting diagnostics identify three primary causes:
1. **Motor Winding Short**: Phase-to-phase or phase-to-ground insulation breakdown inside the conveyor drive motor stator windings. Perform megger insulation resistance testing (must measure > 50 Megohms at 1000V DC) and balance check on phase winding resistances.
2. **Excessive Mechanical Load on CONV01**: Mechanical jamming, seized drive bearings, or belt binding on [[CONV01 Conveyor]] imposing locked-rotor load conditions on startup. Check conveyor roller free rotation manually.
3. **Acceleration Time Too Short**: Parameter `P041 [Accel Time 1]` set too aggressively (e.g., < 0.8 seconds). If heavy forgings rest on the belt, high inertia causes instantaneous overcurrent before the motor reaches synchronous frequency. Minimum recommended acceleration time is 2.5 seconds.

## Related Documents
- Conveyor System: [[CONV01 Conveyor]]
- User Manual: [[VFD01_PowerFlex525_UserManual.pdf]]
- Power Distribution: [[E205 Cell Power Distribution]]
- Logic Program: [[Conveyor Running Logic]]
