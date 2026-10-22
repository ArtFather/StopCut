# Connecting StopCut to a real saw

This guide explains how to tie the app to the saw so that the stop, the saw and the operator work as one cell:
the app moves the stop, the saw is only allowed to cut when the stop is in position, and the app moves on to the
next cut by itself when the saw finishes a stroke.

> **Safety first.** Anything that can start, stop or enable a saw motor, a clamp or a guard must be designed and
> wired by a qualified electrician or controls technician, to your local code (for example NFPA 79 / IEC 60204-1,
> and ISO 13849 for safety functions). Emergency stops and guard interlocks are always **hardwired**. The PC, the
> app and any USB or network I/O are never part of the safety circuit; they only add convenience.

## 0. Your setup: pedal-operated saw, amp only positions the stop

Today the saw is started by a foot pedal and the TigerStop only moves the stop. That is fine and needs no
change to the saw to get started. The plan for this setup:

1. **Step 1, no change to the saw circuit:** mount one inductive proximity sensor that sees the saw head (or
   blade carriage) at rest. Wire it to an I/O module or the TigerStop I/O panel. Each full stroke (head leaves
   rest, comes back) = **Cut done**, and the app sends the next position. The pedal keeps working exactly as now.
   - Don't count pedal presses instead of strokes: a press without a full cut would skip a part.
   - If you want to read the pedal too, use a **spare contact** on the pedal or an interposing relay, never a
     splice into the saw's control wiring.
2. **Step 2, optional interlock:** put a relay contact (driven by the amp's saw-enable output or the I/O
   module) **in series with the pedal's start signal**, so the pedal does nothing while the stop is moving. This
   touches the saw's control circuit, so an electrician must do it, and the saw's own E-stop must stay
   independent of it.

## 1. What the app does today

- Talks to the TigerStop Amp III (firmware 5.23) over RS-232: `MG<pos>` to move, `MH` home, `MS`/`ME` stop,
  `P` position. The amp answers `MGF` when the stop arrives.
- The operator presses **Cut done** after each cut. With "Send next position automatically" on, the next
  position goes out right away.

So the one missing link with the saw is **"the cut happened"**, and optionally **"don't let the saw cut while
the stop is moving"**. Everything below adds those two signals.

## 2. The cycle we want

```
App: MG<pos>  ──►  Amp moves  ──►  Amp: MGF (in position)
                                        │
                                        ▼
                     Saw ENABLED (interlock released, ready light on)
                                        │
            Operator clamps stock, pulls/starts saw (or presses foot pedal)
                                        │
                                        ▼
       Saw head leaves home ... cuts ... returns home   ◄── sensor sees this
                                        │
                                        ▼
               App marks the cut done, saw DISABLED, next MG<pos>
```

A cut is counted when the saw head **leaves** its home/rest position and then **comes back** (a full stroke).
Counting only the return edge after a departure stops false counts from vibration or a bumped sensor.

## 3. Three ways to build it

| | Option A: TigerStop's own I/O | Option B: I/O module on the PC | Option C: small PLC |
|---|---|---|---|
| Extra hardware | TigerStop I/O / interconnect kit, 1–2 sensors | USB or Ethernet I/O module, 24 V supply, sensors | PLC, 24 V supply, sensors |
| Approx. cost | Ask TigerStop dealer | $150–$400 | $250–$700 |
| App change | Poll the amp's I/O over the same RS-232 link | Read/write the module (Modbus TCP is easiest) | Read/write the PLC over Modbus TCP |
| Best for | Simplest, fewest boxes, supported by TigerStop | Cheap, flexible, one manual saw | Auto clamps, pushers, several saws, logic that must keep running if the PC crashes |

### Option A: use the TigerStop's own I/O (recommended to check first)

The Amp 5.x serial spec ([TigerStop SDK wiki](https://github.com/TigerStop/TigerStopSDK/wiki/RS-232:-TigerStop-Serial))
already has I/O commands:

- `O` returns the I/O state, for example `SE=OFF,DM=ON,TA=OFF,L1=ON,L2=ON,FS=OFF,FST=OFF,SC=ON,...`.
  `SE` is the **saw enable** output (the spec calls IO pin 1 "saw enable"). `FS` and `SC` look like a foot
  switch and a saw-cycle input; **confirm the exact meaning and terminal numbers with TigerStop support for your
  serial number**, since this depends on the I/O panel fitted.
- `WB` / `WW` write the amp outputs (saw enable, clamps 1 and 2, beeper, brake, TigerSaw pusher foot).
- `LW` reads a word from the comm-panel I/O.

TigerStop sells a **TigerStop I/O panel** and a **standard interconnect kit** that bring these signals to
terminals. With that fitted:

1. Wire the amp's **saw enable** output to an interposing relay whose contact is in series with the saw's
   **start** circuit (never the motor power directly). The amp then only lets the saw start when the stop is
   stopped in position. This is the standard TigerStop saw interlock and works even without the app.
2. Wire a saw-head **home sensor** (section 4) to the saw-cycle input.
3. The app polls `O` a few times a second while waiting for a cut and treats "SC went active then inactive" as
   **Cut done**.

Ask the dealer for: the I/O panel / interconnect kit part number for an Amp III, the wiring diagram, and whether
the saw-cycle input auto-advances the stop in the TigerStop's own list mode (useful as a fallback).

### Option B: an I/O module on the PC

Good when the amp has no I/O panel. Pick an industrial **24 V, optically isolated** module. Modbus TCP over
Ethernet is the most robust (no USB dropouts, long cable runs, easy to use from C# with the free NModbus library).

| Module | Interface | I/O | Notes |
|---|---|---|---|
| Advantech **ADAM-6050** | Modbus TCP | 12 DI / 6 DO, isolated | ~$200–250, very common, DIN rail |
| Advantech **ADAM-6060** | Modbus TCP | 6 DI / 6 relay | Relay outputs, drive pilot lights or an interposing relay directly |
| Moxa **ioLogik E1212** | Modbus TCP | 8 DI / 8 DIO | Industrial, ~$300 |
| Advantech **USB-4750** | USB | 16 DI / 16 DO, isolated | If you must use USB |
| Waveshare **Modbus RTU/POE relay** boards | RS-485 or Ethernet | 8 relay + 8 DI | Very cheap (~$40–80); fine for a prototype, less rugged |

Avoid hobby USB-GPIO boards (non-isolated 3.3/5 V) on a saw: motor noise will give false cut counts.

### Option C: a small PLC

Use a PLC when there are pneumatic clamps, a pusher, a light curtain, or when the sequence must keep running
correctly if the PC is closed. The PLC runs the cycle and the app just sends targets / reads "cut done" over
Modbus TCP.

| PLC | Approx. cost | Notes |
|---|---|---|
| AutomationDirect **CLICK PLUS** (e.g. C2-03CPU + C2-08ND3 / C2-08TD1) | $200–$350 | Free software, built-in Modbus TCP, cheapest real PLC in North America |
| AutomationDirect **Productivity1000** | $300–$500 | Free software, more capable |
| Siemens **LOGO! 8** (12/24RCE) | $200–$300 | Common in Europe, Modbus TCP built in |
| **Arduino Opta** (Finder) | $150–$250 | Programmed in Arduino / IEC 61131, Modbus TCP |

## 4. Sensors

All recommendations are **24 V DC, 3-wire PNP (sourcing)**, which suits every module and PLC above
(check NPN/PNP against your input card).

| Purpose | Sensor | Example parts | Where |
|---|---|---|---|
| **Saw cycle / cut done** (most important) | Inductive proximity sensor, M12 or M18 | Omron E2E, ifm IG/IFS, AutomationDirect PBM/AM1 (~$20–60) | Fixed bracket that sees a steel flag on the saw arm/head when it is fully back at rest |
| Alternative cut detection | Mechanical limit switch | Omron D4N, Telemecanique XCK | Same place; good for dusty chop saws |
| Alternative for saws you can't sensor | Current switch on the motor lead | AutomationDirect ACS / Veris H708 | Detects blade under load; less precise, use with a timer |
| **Stock against the stop** (optional) | Small photo-eye or roller limit switch | Banner Q12, Omron E3Z, roller lever switch | In the TigerStop's stop face so the saw is only enabled when stock is pushed home |
| **Clamp closed** (pneumatic clamp) | Reed switch on cylinder, or pressure switch | Festo SME/SMT, SMC D-M9 | On the clamp cylinder |
| Foot pedal (optional cycle start) | Industrial foot switch with guard | Linemaster 511, Schneider XPE | Floor |
| Stack light / "ready" lamp | 24 V LED tower | Patlite, Banner TL50 | Green = in position, saw enabled; red = moving / fault |

Debounce every input in software (20–50 ms) and keep sensor cables away from motor leads.

## 5. Safety circuit (hardwired, not in the PC)

- **E-stop** mushroom buttons wired to a **safety relay** (e.g. Pilz PNOZ s3, Banner ES-FA-9AA, Schneider
  XPSUAF) that drops the saw motor contactor **and** the TigerStop's E-stop/motor enable input.
- **Guard / blade cover** interlock switches (Schmersal AZ16, Euchner) in the same safety relay if the saw has
  guards.
- **Light curtain** (e.g. Banner EZ-SCREEN LS) if any part of the cycle becomes automatic (auto-feed, power stroke).
- The software **saw enable** is a *permissive* in series with the start button, after the safety relay. It may
  stop the saw from starting; it must never be relied on to stop it.
- Lockout/tagout point for both the saw and the TigerStop.

## 6. High-level wiring

```
            24 V DC PSU (e.g. Mean Well HDR-60-24 / NDR-75-24) on DIN rail
              │+24 V                                   │0 V (common)
   ┌──────────┼──────────────┬────────────────────┐    │
   │          │              │                    │    │
 Saw-home   Stock-present  Clamp reed        Foot pedal │
 prox (PNP) eye (PNP)      switch                       │
   │ signal   │ signal       │ signal           │       │
   ▼          ▼              ▼                  ▼       │
 ┌──────────────────────────────────────────────────┐  │
 │  TigerStop I/O panel  or  I/O module  or  PLC    │──┘
 │  DI0 saw home  DI1 stock  DI2 clamp  DI3 pedal   │
 │  DO0 saw enable ─► interposing relay coil (24 V) │
 │  DO1 green lamp  DO2 red lamp                    │
 └──────────────────────────────────────────────────┘
          │ Ethernet / RS-232                 interposing relay N.O. contact
          ▼                                         │
      Windows PC (this app)       Saw START circuit: E-stop safety relay ─ START button ─ relay contact ─ motor contactor coil
```

Practical notes:
- Use DIN rail, ferrules, and a small enclosure; label every wire.
- Interposing relays (e.g. Phoenix PLC-RSC-24DC/21, Finder 38 series) isolate the I/O from the saw's control voltage.
- Shielded cable for RS-232 and keep it under ~15 m; use a quality USB-RS232 adapter (FTDI chipset) if the PC has no COM port.

## 7. What would change in the app (later)

Nothing is changed now. When the hardware is chosen, the app would need:

1. An **I/O settings page**: source (TigerStop `O` polling / Modbus TCP address / PLC), which input is
   "saw home", which output is "saw enable", debounce time.
2. A small **I/O service** that polls inputs (every 50–100 ms) and raises a `CutCompleted` event on a full
   saw stroke.
3. In the cutting flow: on `MGF` turn saw enable **on**; on `CutCompleted` call the existing **Cut done**
   (which already sends the next position); turn saw enable **off** before every `MG` move.
4. Faults: if the saw strokes while the stop is moving, or no `MGF` arrives, stop and show a warning.

## 8. Suggested starting kit (one manual saw, about $400–600 plus electrician)

- Ask TigerStop for the Amp III **I/O panel / interconnect kit**, or buy an **Advantech ADAM-6050**.
- 1 × M18 PNP inductive prox (saw home) + bracket and steel flag.
- 1 × 24 V DIN power supply, 1 × 24 V interposing relay, terminal blocks, small enclosure.
- Optional: stock-present photo-eye, green/red stack light, foot pedal.
- Existing or new E-stop with safety relay, fitted by an electrician.

Start with only the saw-home sensor for auto **Cut done**; add the saw-enable interlock once that works reliably.
