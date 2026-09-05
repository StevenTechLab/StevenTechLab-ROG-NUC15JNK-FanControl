# ROG NUC 2025 FanControl Plugin

Community plugin by **StevenTechLab** for ASUS ROG NUC 2025 / NUC15JNK
(tested on NUC15JNKU9X9).

The plugin exposes CPU and GPU temperatures and RPM readings to Fan Control and
provides a linked CPU/GPU control. The middle fan is currently read-only.

## Install

1. Install Fan Control from its official source and close it completely.
2. Copy `FanControl.ROGNUC15JNK.dll` into Fan Control's `Plugins` folder.
3. Copy `ROG-NUC15JNK-ENABLE-CONTROLS.TEST` into Fan Control's main folder.
4. Start Fan Control and create/select the linked CPU/GPU control.
5. Keep G-Helper's own custom fan curve disabled while Fan Control is controlling
   the fans.

The included PowerShell installer performs steps 2 and 3. It must be run as
Administrator. It does not include or redistribute Fan Control itself.

## Important hardware note

The ASUS firmware accepts complete BIOS fan-curve writes rather than a direct
real-time PWM command. A real change can therefore briefly reinitialize the
fans. The plugin keeps 0% passive operation and individual 17–30% steps, and
queues writes in the background so Fan Control remains responsive. The firmware
limitation cannot be removed by the plugin.

This is experimental hardware control. Monitor temperatures and keep the BIOS
thermal protections enabled. Use at your own risk.

## Support

This is free community software. Voluntary support is welcome:

https://paypal.me/StevenA001

This is voluntary support with no promised service or other consideration; it
is not a tax-deductible charitable donation.

## Scope and licensing

This repository contains only the author's plugin code and documentation. Fan
Control itself is separate proprietary software and is not included. The
plugin depends on Fan Control's public plugin API and third-party libraries
available from the user's Fan Control installation.

The plugin code is provided as-is, without warranty. ASUS, ROG, Intel, NVIDIA,
and Fan Control are trademarks of their respective owners.
