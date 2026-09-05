<a id="english"></a>

# ROG NUC 2025 FanControl Plugin

**Languages / Sprachen:** [English](#english) · [Deutsch](#deutsch) · [Deutsche Einzelseite](README.de.md)

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

## Version 1.0.1

This maintenance update suppresses tiny automatic one-percent oscillations in
the 17–30% quiet range. Exact manual steps such as 20%, 25%, and 30% remain
available, while needless BIOS curve rewrites — which can briefly restart the
fans — are avoided.

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

---

<a id="deutsch"></a>

# ROG NUC 2025 FanControl-Plugin

**Sprachen / Languages:** [Deutsch](#deutsch) · [English](#english) · [Deutsche Einzelseite](README.de.md)

Community-Plugin von **StevenTechLab** für ASUS ROG NUC 2025 / NUC15JNK
(getestet auf NUC15JNKU9X9).

Das Plugin stellt Fan Control CPU- und GPU-Temperaturen sowie Drehzahlen zur
Verfügung und bietet eine gemeinsame Steuerung für CPU- und GPU-Lüfter. Der
mittlere Lüfter ist derzeit nur lesbar.

## Installation

1. Fan Control über die offizielle Quelle installieren und vollständig schließen.
2. `FanControl.ROGNUC15JNK.dll` in den Ordner `Plugins` von Fan Control kopieren.
3. `ROG-NUC15JNK-ENABLE-CONTROLS.TEST` in den Fan-Control-Hauptordner kopieren.
4. Fan Control starten und die gemeinsame CPU-/GPU-Steuerung erstellen bzw.
   auswählen.
5. Die eigene Lüfterkurve von G-Helper deaktiviert lassen, solange Fan Control
   die Lüfter steuert.

Das enthaltene PowerShell-Installationsskript erledigt die Schritte 2 und 3.
Es muss als Administrator ausgeführt werden. Fan Control selbst ist nicht
enthalten und wird nicht weiterverteilt.

## Wichtiger Hinweis zur Hardware

Die ASUS-Firmware akzeptiert vollständige BIOS-Lüfterkurven statt eines
direkten PWM-Echtzeitbefehls. Eine echte Änderung kann die Lüfter deshalb kurz
neu initialisieren. Das Plugin unterstützt passiven 0%-Betrieb und einzelne
Stufen von 17–30 % und reiht Schreibvorgänge im Hintergrund ein, damit Fan
Control bedienbar bleibt. Diese Einschränkung der Firmware kann das Plugin
nicht aufheben.

## Version 1.0.1

Dieses Wartungsupdate unterdrückt winzige automatische Ein-Prozent-Schwankungen
im leisen Bereich von 17–30 %. Exakte manuelle Stufen wie 20 %, 25 % und 30 %
bleiben verfügbar, während unnötige BIOS-Kurvenänderungen – die die Lüfter kurz
neu starten können – vermieden werden.

Dies ist experimentelle Hardwaresteuerung. Temperaturen überwachen und die
thermischen BIOS-Schutzfunktionen aktiviert lassen. Nutzung auf eigene Gefahr.

## Unterstützung

Dies ist kostenlose Community-Software. Freiwillige Unterstützung ist willkommen:

https://paypal.me/StevenA001

Die Unterstützung ist freiwillig, ohne zugesagte Leistung oder Gegenleistung,
und keine steuerlich absetzbare Spende.

## Umfang und Lizenzierung

Dieses Repository enthält nur den Plugin-Code und die Dokumentation des Autors.
Fan Control ist separate proprietäre Software und nicht enthalten. Das Plugin
nutzt die öffentliche Plugin-API von Fan Control sowie Drittanbieter-Bibliotheken
aus der Fan-Control-Installation des Nutzers.

Der Plugin-Code wird ohne Gewährleistung bereitgestellt. ASUS, ROG, Intel,
NVIDIA und Fan Control sind Marken ihrer jeweiligen Eigentümer.
