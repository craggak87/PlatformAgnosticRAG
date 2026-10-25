---
plc_platform: Siemens
controller: PLC01
equipment_id: CONV01
criticality: Medium
tags:
  - plc
  - conveyor
  - logic
  - lad
  - siemens
---

# Conveyor Running Logic - Function Block FB10

## Description
Function Block `FB10_ConveyorControl` in [[PLC01 Siemens S7-1500]] manages the automated sequencing, speed regulation, and safe stopping of infeed conveyor [[CONV01 Conveyor]]. It processes part detection sensors, downstream robot clearance interlocks, and emergency stop statuses to command [[VFD01 Inverter]].

## Inputs
- `I0.0` (PE-01): Part infeed presence sensor.
- `I0.1` (PE-02): Conveyor buffer queue full sensor.
- `I0.2` (PE-03): Part at pick station stop sensor.
- `I0.5`: VFD01 Ready feedback contact.
- `I0.6`: VFD01 Inverter Running feedback.
- `I:0/10` & `I:0/11`: Safety Gate 2 closed contacts (via F-DI safety DB).
- `DB10.DBX0.0`: Auto Mode active flag from cell sequence manager.
- `DB10.DBD4`: Target speed setpoint in mm/s (converted to 0-27648 analog word).

## Outputs
- `Q0.0`: VFD01 Start / Run Forward command.
- `Q0.1`: VFD01 Fault Reset pulse.
- `Q0.2`: Infeed pneumatic part clamp cylinder extend.
- `PQW256`: Analog speed reference word (0-10V DC to VFD01 terminal 13).
- `DB10.DBX1.0`: Conveyor running status bit to HMI.

## Interlocks
1. **Safety Interlock**: If [[Safety Gate 2]] opens or any emergency stop in [[E102 Safety Circuit Schematic]] trips, output `Q0.0` is immediately forced to FALSE via fail-safe hardware and software interlocks.
2. **Robot In-Zone Clearance**: If [[ROBOT01 ABB IRB4600]] is actively traversing the conveyor pick zone (`Robot_In_PickZone = TRUE`), the conveyor motor decelerates to a stop and part clamp extends.
3. **Queue Full Interlock**: If PE-02 detects continuous part presence for > 5 seconds, conveyor pauses to prevent upstream part crushing.
4. **VFD Fault Trip**: If VFD01 signals a fault (e.g. overcurrent F004), output `Q0.0` drops out and alarm [[ALM-205 Safety Gate Open]] / drive fault is posted.

## Related Alarms
- Drive Overcurrent Fault: [[VFD01 Inverter]]
- Conveyor Part Timeout: `ALM-110 Conveyor Jam Timeout`
- Low Air Pressure: [[ALM-102 Low Pneumatic Pressure]]

## Related Tags
- `#plc`
- `#conveyor`
- `#vfd`
- `#interlock`
- `#siemens`
