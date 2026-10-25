---
equipment_id: CONV01
equipment_name: Infeed Belt Conveyor
manufacturer: Dorner
model: 2200 Series
location: Cell A Infeed
criticality: Medium
tags:
  - conveyor
  - dorner
  - infeed
  - vfd
  - mechanical
---

# Infeed Belt Conveyor - CONV01

## Purpose
CONV01 is a continuous flat-belt infeed conveyor responsible for transporting raw automotive aluminum forgings from the bulk loading station to the optical inspection zone under [[CAM01 Inspection]] and the pick location for [[ROBOT01 ABB IRB4600]].

## Key Components
- **Conveyor Bed**: Heavy-duty extruded aluminum frame with low-friction wear strips.
- **Drive Motor**: 0.75 kW 3-phase 400V AC induction motor coupled via timing belt transmission to the head drive pulley.
- **Variable Frequency Drive**: Controlled by [[VFD01 Inverter]] located inside cabinet [[Main Power Distribution]].
- **Photoelectric Sensors**:
  - PE-01 (Part Infeed Detection): Optical retroreflective sensor wired to PLC input `I0.0`.
  - PE-02 (Part Queue Full): Diffuse sensor wired to PLC input `I0.1`.
  - PE-03 (Pick Stop Station): Retroreflective sensor wired to PLC input `I0.2`.

## Communication & Control
- Speed regulation is performed by [[PLC01 Siemens S7-1500]] analog output channel `AQ0` providing a 0-10V DC analog speed reference signal to [[VFD01 Inverter]] terminal 13.
- Run/Stop commanding is managed by relay output `Q0.0` over the conveyor running interlock circuit described in [[Conveyor Running Logic]].

## Known Issues
### Speed Drift Under Load
During heavy production batches where consecutive forgings accumulate on the infeed bed, CONV01 experiences noticeable speed drift under load. Typical line speed decreases from the calibrated 0.35 m/s down to 0.22 m/s without an commanded setpoint change. 
Investigation reveals two primary root causes:
1. **VFD Analog Speed Reference Voltage Droop**: High-impedance wiring runs or grounding noise between PLC analog module and VFD terminals can induce an analog reference signal drop of up to 1.8V under electrical load.
2. **Drive Belt Tension Issue**: Slack in the timing drive belt connecting the motor shaft to the head drive roller causes mechanical slip under peak torque loading. Periodic retensioning to 45 Hz acoustic frequency is required per [[Infeed Conveyor Mechanical]].

## Related Documents
- Drive Inverter: [[VFD01 Inverter]]
- PLC Logic: [[Conveyor Running Logic]]
- Mechanical Specs: [[Infeed Conveyor Mechanical]]
- Power Distribution: [[E205 Cell Power Distribution]]
