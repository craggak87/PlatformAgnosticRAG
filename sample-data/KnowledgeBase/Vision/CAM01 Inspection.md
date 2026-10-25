---
equipment_id: CAM01
equipment_name: Infeed Part Vision Inspection Camera
manufacturer: Cognex
model: In-Sight 7802M
location: Infeed Station Cell A
criticality: High
tags:
  - vision
  - cognex
  - inspection
  - quality
  - lighting
---

# Infeed Part Vision Inspection Camera - CAM01

## Purpose
CAM01 is an autonomous high-resolution monochrome vision system positioned directly above the infeed conveyor [[CONV01 Conveyor]]. It performs dimensional tolerance checks, orientation angle measurement, and surface defect inspection on aluminum forgings before robot pick commands are authorized.

## Key Components
- **Camera Sensor**: Cognex In-Sight 7802M monochrome 2-megapixel CMOS imager (1600x1200 resolution).
- **Optics**: Edmund Optics 25mm high-resolution fixed focal length lens with locking aperture and focus rings.
- **Lighting System**: Integrated high-intensity white LED ambient ring lighting unit (model CLR-100) mounted concentric with the optical axis.
- **Enclosure**: IP67 rated anodized aluminum protective housing with anti-reflective optical glass window.

## Communication & PLC Interface
- **Network**: PROFINET Industrial Ethernet interface communicating with [[PLC01 Siemens S7-1500]] at IP address `192.168.1.20`.
- **Trigger**: Hardware discrete trigger pulse from PLC output `Q0.3` synchronized with infeed part stop sensor PE-03.
- **Result Bits**:
  - `Part_OK`: Inspection passed.
  - `Part_REJ`: Inspection failed.
  - Result code word sent to PLC data block `DB20.DBW10`.

## Rejection Codes & Diagnostic Analysis
### Rejection Code REJ-03 (Edge Contrast / Geometry Failure)
When parts are rejected with code `REJ-03`, the vision tool algorithm fails to detect the perimeter edge boundary or fails edge contrast threshold criteria within the defined region of interest (ROI).
System diagnostic analysis identifies two dominant physical causes:
1. **Insufficient Ambient Ring Lighting**: Partial failure of the LED sectors within the ambient ring lighting assembly, loose 24V DC dimmer cabling, or power supply ripple causing illumination drop below 850 lux. Insufficient ambient ring lighting flattens contrast between the machined component bevel and the conveyor belt background.
2. **Lens Contamination**: Airborne oil mist, coolant splatter, or dust settlement on the front protective glass or lens surface creates optical blur and diffused light scattering. Lens contamination reduces edge gradient sharpness below the minimum 25-point edge detection threshold.

## Corrective Actions for REJ-03
1. **Inspect and Clean Optics**: Inspect the optical window and lens for contamination, fingerprints, or oil streaks. Clean with optical grade lens tissue and isopropyl alcohol (IPA).
2. **Check Ring Light Output**: Measure ambient ring lighting illuminance at the inspection plane using an optical lux meter (nominal specification: 900 to 1100 lux). Verify 24V DC supply at light connector pins 1 and 3.
3. **Calibrate Vision Job**: Open Cognex In-Sight Explorer, load job file `CellA_Forging_Rev4.job`, and verify edge detection tool contrast scores against known reference calibration targets.

## Related Documents
- Conveyor System: [[CONV01 Conveyor]]
- Master Controller: [[PLC01 Siemens S7-1500]]
- Power Distribution: [[E205 Cell Power Distribution]]
- Cycle Optimization: [[Cycle Time Optimization]]
