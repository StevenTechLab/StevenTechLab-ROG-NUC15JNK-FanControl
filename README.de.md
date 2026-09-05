# ROG NUC 2025 FanControl-Plugin

Community-Plugin von **StevenTechLab** für ASUS ROG NUC 2025 / NUC15JNK.

## Installation

1. Fan Control vollständig schließen.
2. FanControl.ROGNUC15JNK.dll in den Plugins-Ordner kopieren.
3. ROG-NUC15JNK-ENABLE-CONTROLS.TEST in den Fan-Control-Hauptordner kopieren.
4. Fan Control starten und die gemeinsame CPU-/GPU-Steuerung auswählen.
5. Die eigene Lüfterkurve in G-Helper deaktivieren, solange Fan Control steuert.

Das PowerShell-Installationsskript erledigt die Kopierschritte automatisch und muss als Administrator ausgeführt werden. Fan Control selbst ist nicht enthalten.

## Funktionen und Grenzen

CPU- und GPU-Temperaturen sowie Drehzahlen werden ausgelesen. Die CPU-/GPU-Lüfter können gemeinsam gesteuert werden; der mittlere Lüfter ist derzeit nur lesbar. Die ASUS-Firmware verwendet vollständige BIOS-Kurven statt direkter PWM-Befehle. Änderungen können die Lüfter kurz neu initialisieren. Unterstützt werden passiver 0%-Betrieb und feine Stufen von 17–30%. Diese Firmware-Einschränkung kann der Plugin nicht beseitigen.

Temperaturen überwachen und BIOS-Schutzfunktionen aktiviert lassen. Nutzung auf eigene Gefahr.

## Unterstützung

Kostenlose Community-Software. Freiwillige Unterstützung: https://paypal.me/StevenA001

Fan Control ist eine separate proprietäre Software und nicht enthalten. Der Plugin wird ohne Gewährleistung bereitgestellt.
