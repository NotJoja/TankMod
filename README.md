# TankMod

A [R.E.P.O.](https://store.steampowered.com/app/3241660/REPO/) mod (BepInEx + [REPOLib](https://thunderstore.io/c/repo/p/Zehs/REPOLib/)) that adds a drivable tank to the shop.

- Built on the game's scooter vehicle logic, scaled to roughly the size of a shopping cart.
- Sit on top of the turret and drive; right click fires the cannon (7 rechargeable shots).
- Heavy, and it stays put when nobody is in it.

## Credits

- **Tank 3D model:** "Tank" by [amanpatel9725](https://sketchfab.com/amanpatel9725) on Sketchfab, licensed
  [CC-BY-4.0](http://creativecommons.org/licenses/by/4.0/).
  Source: https://sketchfab.com/3d-models/tank-a901e18b6a7e4624a0c2c54926cf8c9c
  Changes: converted to OBJ, reoriented and recentred, textures downscaled, parts merged by material.

See [CREDITS.md](CREDITS.md).

## Building

Requires the .NET SDK. `dotnet build -c Release` produces `TankMod.dll`; the model and textures are embedded in it.
