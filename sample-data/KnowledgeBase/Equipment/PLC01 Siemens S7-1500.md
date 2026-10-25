---
equipment_id: PLC01
equipment_name: Main Cell Master Controller
manufacturer: Siemens
model: S7-1516F-3 PN/DP
location: Cell A Cabinet 1
criticality: Critical
tags:
  - plc
  - siemens
  - s71500
  - automation
  - safety
---

# Main Cell Master Controller - PLC01 Siemens S7-1500

## Purpose
PLC01 is the central programmable logic controller for manufacturing Cell A. It coordinates all robotic material transfers, machine safety interlocks, vision quality inspections, conveyor tracking, pneumatic valves, and operator interface stations.

## Key Hardware Modules
- **CPU**: CPU 1516F-3 PN/DP (6ES7 516-3FN02-0AB0) with integrated safety processing capabilities (SIL 3 / PL e).
- **Digital Input Modules**:
  - Slot 4: DI 16x24VDC HF (addresses `I0.0` - `I1.7`), handling part presence sensors, cylinder reed switches, and general pushbuttons.
  - Slot 5: F-DI 16x24VDC Fail-Safe Digital Input module handling safety switches including `I:0/10` and `I:0/11` from [[Safety Gate 2]].
- **Digital Output Modules**:
  - Slot 6: DO 16x24VDC/0.5A HF (addresses `Q0.0` - `Q1.7`), controlling solenoids, stack lights, and VFD start coils.
- **Analog Input/Output Modules**:
  - Slot 7: AI 8xU/I/RTD/TC ST (addresses `PIW256` - `PIW270`).
  - Slot 8: AQ 4xU/I ST (addresses `PQW256` - `PQW262`), providing 0-10V analog speed reference to [[VFD01 Inverter]].

## Communication Networks
- **PROFINET Interface X1 (192.168.1.0/24)**:
  - `192.168.1.1`: PLC01 CPU
  - `192.168.1.10`: [[ROBOT01 ABB IRB4600]] IRC5 PROFINET Device
  - `192.168.1.20`: [[CAM01 Inspection]] Cognex In-Sight Smart Camera
  - `192.168.1.30`: ET 200SP Remote I/O station in operator console
- **PROFINET Interface X2 (Plant Network - 10.10.20.0/24)**:
  - Uplink to SCADA, MES, and Cell Knowledge Assistant edge gateway.

## Diagnostics & Known Interlocks
- All safety functions must be healthy (F-CPU passivated state cleared) before automatic cycle enable.
- Dual-channel safety discrepancy timers configured to 250 ms across inputs `I:0/10` and `I:0/11` on drawing [[E102 Safety Circuit Schematic]].

## Related Documents
- System Manual: [[PLC01_Siemens_S7-1500_Manual.pdf]]
- Ladder Logic: [[Conveyor Running Logic]]
- Power Distribution: [[E205 Cell Power Distribution]]
- Electrical Schematic: [[E102 Safety Circuit Schematic]]
