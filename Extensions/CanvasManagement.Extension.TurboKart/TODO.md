# Turbo Kart GP — open tasks

Stand 16.11.2026. Build ok, alle 8 Strecken simulieren fehlerfrei, NAS-Image gebaut.

## Fertig, aber noch nicht visuell geprüft
- [ ] Rampen (`Track.PlaceRamps/BuildRamps`, `RaceFx.RampPhysics/Land`): Sprung, Lande-Boost, Kamera-Neigung (`CamPitch`) im Vorschau-Bild prüfen; Rampenhöhe/Absprung (`34 + speed*0.38`) feintunen.
- [ ] Kamera-Shake (Bande, Treffer, Landung, Absturz): Stärke prüfen.
- [ ] Zieleinlauf-Replay (`RaceFx.StartReplay/UpdateReplay`): Seitenkamera-Position, Zeitlupe 0.65x, Kinobalken. Fehlt noch: Text „REPLAY“ (blinkend) in `GameScreens.RaceText`, wenn `race.Replaying`; normale HUD-Texte währenddessen ausblenden.
- [ ] Regen in Neon City / Stadion ansehen (Streifen, Spritzer, Blitz, nasse Fahrbahn, Gischt).
- [ ] Nacht-Panorama + Dämmerungs-Tönung auf Stadium/Beach/Canyon/Autumn prüfen.
- [ ] Feuerwerk im Ziel, Lava-Eruptionen, Driftrauch, Reifenspuren.

## Begonnen, noch nicht fertig
- [ ] Animierte Deko im Renderer verdrahten (Sprites + Platzierung existieren schon):
  - `DrawScenery`: Ballons langsam treiben lassen (`Pos + sin(clock*0.05)*30`), Möwen kreisen (`Pos + (cos,sin)(clock*0.4+phase)*40`, Frame `(clock*6)&1`), Fahnen-Frames (`Kinds[FlagPole][variant*3 + frame]`, aktuell wird nur `variant % 15` genutzt).
  - `DrawQuad`: `Art.FlowMarker` (Wasserfälle in Canyon-Klippen) animieren, `Art.NeonMarker` (City-Schilder) flackern lassen. Ohne Behandlung erscheinen die Marker-Texel aktuell mit falschem Alpha → prüfen!
  - Suchscheinwerfer im Himmel (City immer, Stadium bei `Darkness > 0.5`), Sternschnuppen (Space, City).
- [ ] Kart-Spiegelung auf nasser Fahrbahn (`DrawKart(.., rain)` — Parameter ist da, Spiegelung fehlt): gespiegelte Zeilen unter `groundY`, Alpha ~0.3, nur auf Bodenpixeln (`_depth == MaxValue`).
- [ ] Gischt/Rauch: `Particle.Smoke` wird in `DrawSmoke` gezeichnet, aber `DrawParticles(false)` zeichnet Smoke-Partikel zusätzlich als Quadrate → dort `if (p.Smoke) continue;` ergänzen.

## Ideen danach
- Lava Keep: Haarnadel (idx ~1086) etwas entschärfen, KI fällt dort nach Treffern noch gelegentlich.
- Fahrer-Animation (Jubeln im Ziel), Rückspiegel.
- Echter Controller-Test in Studio (Gamepad-API in `verpixeld/wwwroot/js/features/game-input.js`).

## Testen
Vorschau-Harness liegt in `%TEMP%\TurboKartPreview` (nicht im Repo):
`dotnet run -c Release -- tracks all 400,1200` · `-- screens` (GP-Ablauf bis Podest).
