# File reading measurements

Read SII catalogue files from game archives and report their loading time:

```powershell
dotnet run --project TruckLib.HashFs.Benchmarks/TruckLib.HashFs.Benchmarks.csproj -c Release -- --archives "<archives.txt>" --mode sii --iterations 5 --output "<results.json>"
```

- `--archives`: UTF-8 list of archive paths, one per line, highest priority first. Relative paths are relative to this list. Blank lines and `#` comments are ignored.
- `--mode sii`: load the road-look, model and prefab SII catalogues in `/def/world/`, including their referenced files.
- `--iterations`: number of timed batches after one warm-up batch.
- `--output`: JSON file for times, file counts, unit counts and content hashes.

To measure file extraction without parsing, use `--mode read`. Add `--directory /unit/hookup/` to read that directory's SII files. Alternatively, `--entries "<entries.txt>"` selects the exact virtual paths listed in a UTF-8 file, one per line; it replaces directory selection.

The tool reads the archives without changing them. It reports elapsed times, file counts and content hashes. SII mode also checks that the selected documents parse. It does not start the game or check how the game uses these files.
