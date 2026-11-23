## Summary

Describe your change, why it is needed, and how it preserves `60.0988 Hz` simulation authenticity and clean-room boundaries.

## Clean-Room & Security Checklist

- [ ] **Zero Retail Media / Code:** Contains zero copyrighted ROM files (`.nes`, `.prg`, `.bin`), graphics, audio, or pre-generated `*.Generated.dll` / `*.g.cs` files.
- [ ] **Clean-Room Boundaries:** Any upstream schema changes remain strictly metadata-only (no commercial trademarks in source identifiers or docs outside `## License & Legal`).
- [ ] **No Hardcoded Workstation Paths:** Verified zero machine-specific paths or PII literals.
- [ ] **Zero-AutoStart & Containment:** Preserves `AllowAutoStart = false`, `APP_ISOLATED_TEST=1` containment, and Release `--automation-port` `ExitCode = 2`.

## Verification

```powershell
$env:APP_ISOLATED_TEST = "1"
dotnet test Dev\IronFrameCart01.slnx -c Release /warnaserror
```

- Unit tests run and pass (`0` warnings, `0` skipped)
- Updated relevant documentation / tests if CLI flags or contracts changed
