# Godot regression harness

Build with `dotnet build src.Regression.csproj -p:Sts2Dir="<game directory>" -p:BaseLibDir="<BaseLib directory>"` and run the project in a Godot 4.5.1 .NET host compatible with the game's .NET 9 runtime. Supply runtime dependencies from your own installation; no binaries are bundled. Its project.godot uses the isolated SoundWorkshopRegression user directory. The optional `--startup-check` and `--visual-check` user arguments select additional checks. This source publication did not rerun this interactive harness.
