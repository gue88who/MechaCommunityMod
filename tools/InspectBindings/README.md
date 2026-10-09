# Inspect game binding metadata

Read type names, properties and method signatures without loading game assemblies
or initializing their native runtime:

```powershell
dotnet run --project tools/InspectBindings -- "C:/Program Files (x86)/Steam/steamapps/common/Mechabellum/BepInEx/interop/GRFight.dll" MechTeam ExpSystem
```

The first argument is an installed binding DLL; remaining arguments are type-name
substrings. This tool prints metadata only and does not modify the game.
