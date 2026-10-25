---
equipment_id: PLC01
equipment_name: Cell A Standard Operating Procedure - LOTO
criticality: Critical
procedure_type: Safety / EHS
engineer: A.Huza
date: 2026-10-10
tags:
  - procedures
  - loto
  - safety
  - electrical
  - pneumatics
---

# Standard Operating Procedure - Lockout Tagout (LOTO) for Cell A

## Purpose & Scope
This procedure establishes mandatory energy isolation protocols for manufacturing Cell A prior to executing mechanical servicing, electrical maintenance, robot grease change, or physical entry into dangerous operating envelopes.

## Hazardous Energy Sources
Cell A contains four hazardous energy sources that must be isolated and dissipated:
1. **400V 3-Phase Electrical Mains**: Powers panel [[Electrical/Main Power Distribution.md]] and drives for [[ROBOT01 ABB IRB4600]] and [[VFD01 Inverter]].
2. **24V DC Auxiliary Control Power**: Powers [[PLC01 Siemens S7-1500]], sensors, and safety interlocks.
3. **Pneumatic Compressed Air (6.2 bar)**: Stored in receiver bowls and piping feeding [[Pneumatics/Pneumatic Main.md]].
4. **Gravitational / Kinetic Potential Energy**: Robot arm tooling, suspended components, and conveyor belt tension.

## Step-by-Step Isolation Sequence
1. **Preparation & Notification**:
   - Inform the shift production supervisor and adjacent line operators that Cell A will be powered down for servicing.
   - Command robot [[ROBOT01 ABB IRB4600]] to execute safe home routine `r_HomeCheck` (refer to [[IRB4600 Motion Subroutines]]) and stop in mechanical park position.
2. **Normal Machine Stop**:
   - Press the illuminated yellow "Cycle Stop" pushbutton on the main HMI panel. Wait for infeed conveyor [[CONV01 Conveyor]] to halt.
3. **Electrical Energy Isolation**:
   - Switch the red/yellow rotary main isolator switch `Q1` on panel CP-01 to the **OFF** position.
   - Apply a personal padlock and danger tag to the hasp on `Q1`. Keep key in personal possession.
4. **Pneumatic Energy Isolation & Dissipation**:
   - Rotate the yellow manual shut-off lockout ball valve on [[Pneumatics/Pneumatic Main.md]] to the **CLOSED** position.
   - The integrated exhaust port will automatically dump downstream air pressure.
   - Apply a second personal padlock to the pneumatic valve lockout flange.
   - Verify downstream pressure gauge reads **0.0 bar**.
5. **Zero Energy Verification (Try-Step)**:
   - Attempt to start the cell using the HMI and green "Cycle Start" pushbuttons. Confirm zero movement and zero motor hum.
   - Open Cabinet 1 door and use an approved, calibrated multimeter to perform 3-point voltage test (Line-Line, Line-Neutral, Line-Earth) confirming **0.0V AC**.
   - Inspect mechanical stops and verify robot arm cannot drop under gravity.

## Restoration to Service
1. Clear all tools, replacement parts, and maintenance equipment from the cell floor.
2. Ensure all safety fencing panels are secured and [[Safety Gate 2]] is closed.
3. Remove pneumatic padlock and slowly reopen the pneumatic isolation valve to recharge line pressure to 6.2 bar.
4. Remove electrical padlock on `Q1` and rotate to **ON**.
5. Press the blue "Safety Reset" button to clear safety relay trip state per [[E102 Safety Circuit Schematic]].
6. Acknowledge alarms and verify healthy status on [[PLC01 Siemens S7-1500]].

## Related Documents
- Safety Interlock: [[Safety Gate 2]]
- Power Distribution: [[Electrical/Main Power Distribution.md]]
- Pneumatic System: [[Pneumatics/Pneumatic Main.md]]
- Electrical Schematic: [[E102 Safety Circuit Schematic]]
