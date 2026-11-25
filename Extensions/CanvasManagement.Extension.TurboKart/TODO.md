# Turbo Kart GP — open tasks

Stand 25.11.2026. Build ok, alle 8 Strecken simulieren fehlerfrei.

## Erledigt (25.11.)
- [x] Sound neu: Motor-Tonhöhe folgt jetzt dem Tempo relativ zur Höchstgeschwindigkeit (vorher bei Tempo 72 von ~205 am Anschlag),
      drei Motor-Loops mit Equal-Power-Überblendung, nahtlose Loops (Skid hatte einen Knacks an der Naht), Lautheit per RMS statt Peak,
      Rivalen-Motor mit Doppler, Schotter-Rumpeln, Boost-Flamme, synthetische Effekte (Countdown, Runde, letzte Runde, Fanfare,
      Item-Roulette, Öl, Orb, Schild, Drift-Stufen, Bande, Sprung/Landung, Absturz, Gems, Falschrum).
      Unbenutzte Kenney-Clips (tick/go/lap/item/finish) entfernt.
- [x] Host-Mixer (`CanvasManagement/ExtensionVoiceMixer.cs`): Pegel/Pitch gleiten pro Sample, Loops blenden ein/aus, Clip-Wechsel
      überblenden, Summe über Limiter statt harter Clamp pro Stimme, 8 Loop-Slots, Stream bleibt 3 s mit Stille offen
      (vorher: Nachhol-Burst nach kurzen Pausen).
- [x] „REPLAY“ blinkt im Zieleinlauf-Replay, Renn-HUD-Texte währenddessen aus.
- [x] Animierte Deko: Ballons treiben, Möwen kreisen (2 Frames), Fahnen wehen (3 Frames je Farbe).
- [x] Marker-Texel: Wasserfälle (FlowMarker) laufen, Neon-Schilder (NeonMarker) flackern; Alpha wird auf 255 gesetzt.
- [x] Suchscheinwerfer (City immer, Stadion bei Nacht), Sternschnuppen (Space, City).
- [x] Kart-Spiegelung auf nasser Fahrbahn.
- [x] Rauchpartikel nicht mehr doppelt (als Quadrat) gezeichnet.

## Noch visuell zu prüfen
- [ ] Rampen-Sprung / Kamera-Neigung, Kamera-Shake-Stärke, Replay-Kameraposition.
- [ ] Nacht-Panorama + Dämmerung auf Beach/Canyon/Autumn.

## Ideen danach
- Lava Keep: Haarnadel (idx ~1086) etwas entschärfen, KI fällt dort nach Treffern noch gelegentlich.
- Fahrer-Animation (Jubeln im Ziel), Rückspiegel.
- Echter Controller-Test in Studio (Gamepad-API in `verpixeld/wwwroot/js/features/game-input.js`).

## Testen
- Allgemeines Vorschau-Harness (Frames, Kontaktbogen, Ton als WAV): `CanvasManagement/tools/ExtensionPreview`
  `dotnet run -c Release --project tools/ExtensionPreview -- TurboKart --seconds 60 --every 5000 --wav`
- Strecken-Harness liegt in `%TEMP%\TurboKartPreview` (nicht im Repo):
  `dotnet run -c Release -- tracks all 400,1200` · `-- screens` (GP-Ablauf bis Podest).
