---
equipment_id: CONV01
equipment_name: Infeed Conveyor Mechanical Assembly
manufacturer: Dorner
model: 2200 Series Flat Belt
location: Cell A Infeed
criticality: Medium
tags:
  - mechanical
  - conveyor
  - belt
  - tension
  - bearings
---

# Infeed Conveyor Mechanical Assembly - CONV01

## Mechanical Specifications
- **Conveyor Length**: 3200 mm between pulley centers.
- **Belt Width**: 300 mm.
- **Belt Material**: Habasit FNB-5E antistatic, low-friction polyester fabric with TPU coating.
- **Drive Pulley Diameter**: 50 mm crowned aluminum drive drum.
- **Transmission**: 2:1 reduction HTD timing belt connecting 0.75 kW motor shaft to drive roller.

## Belt Tensioning & Alignment Procedures
Proper mechanical belt tension is critical to prevent speed drift and slippage under payload (see [[CONV01 Conveyor]]):
1. **Initial Belt Tracking**:
   - Loosen idler take-up locknuts on tail roller.
   - Adjust dual tensioning screws until the belt centers itself within +/- 1.5 mm of conveyor guide rail edges during low-speed jog.
2. **Timing Belt Tension Check**:
   - Use an acoustic tension meter (Gates Sonic Tension Meter 508C) placed 5 mm above the span of the timing drive belt.
   - Pluck the belt lightly; natural vibration frequency must register between **42 Hz and 48 Hz** (target: 45 Hz).
   - If frequency measures < 38 Hz, tighten the motor mounting bracket adjuster bolts.
3. **Bed Friction & Wear Strips**:
   - Inspect UHMW-PE slide bed strips for excessive grooving or debris entrapment beneath the belt surface every 6 months.

## Lubrication & Bearing Maintenance
- **Drive Bearings**: Permanently sealed, deep-groove ball bearings (6204-2RS). No greasing required; replace immediately if acoustic stethoscope detects roughness or bearing temperature rises above 65°C.
- **Idler Rollers**: Clean dust seals and confirm free spinning by hand during line changeover.

## Known Mechanical Wear Points
- Belt edge fraying caused by misalignment against side guide brackets.
- Drive pulley rubber lagging glaze or wear resulting in reduced friction coefficient against conveyor belt underside.

## Related Documents
- Conveyor System: [[CONV01 Conveyor]]
- Drive Inverter: [[VFD01 Inverter]]
- Preventive Maintenance: [[Preventive Maintenance Robot]]
- Cycle Time: [[Cycle Time Optimization]]
