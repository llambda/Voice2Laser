# EZCAD2 Simulator

Small WPF test target for Voice2Laser. It deliberately builds as **EzCad2.exe**, because Voice2Laser locates the target process by that exact process name.

## Run

1. Open `Voice2Laser.sln` in Visual Studio 2022.
2. Build the solution.
3. Start the `EzCad2Simulator` project. Keep its window open.
4. Start `Voice2Laser`.
5. In Voice2Laser, refresh/check the EZCAD2 connection. The simulator should be detected as EZCAD2.
6. Enable live input only for this simulator test.
7. Put Voice2Laser in the foreground so the simulator is behind it.
8. Use the test microphone and speak commands. The simulator should log both `WM_KEYDOWN` and `WM_KEYUP` while remaining in the background.

## Expected behavior

- `anteprima` / F1: updates the simulated preview status.
- `start` / F2: records receipt only; **does not start a laser or control hardware**.
- `stop` / ESC: updates the simulated stop status.
- Direction commands: change the simulated X/Y coordinates.
- The message log shows timestamp, message type, virtual key, scan code, and extended-key flag.

## What this test proves (and does not prove)

A successful test proves that Voice2Laser can find a process named `EzCad2.exe` and that the simulator's top-level WPF window receives the posted key messages while it is not foreground. It does not prove that the real EZCAD2 application handles `PostMessage` the same way; real EZCAD2 may use child controls, a different input path, or additional internal state.

The simulator is intentionally harmless. It does not communicate with a laser, motion controller, USB device, or EZCAD2 hardware.
