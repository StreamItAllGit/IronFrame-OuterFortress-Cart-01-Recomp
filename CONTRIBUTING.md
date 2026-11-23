# Contributing

Contributions are welcome, including bug reports, documentation fixes, and pull requests.

## Ways to Contribute

- **Bug Reports:** Open an issue with your OS, `.NET` runtime version, `reEngine` / `reCart` versions, steps to reproduce, and sanitized log excerpts.
- **Pull Requests:** Keep changes focused on a single fix or feature, align with [Dev/docs/CHARTER.md](Dev/docs/CHARTER.md), and include tests for new behavior.
- **Documentation:** Improvements to clarity, guides, and clean-room examples are always appreciated.

## Dev Setup & Running Tests

```powershell
git clone https://github.com/StreamItAllGit/IronFrame-OuterFortress-Cart-01-Recomp.git
cd IronFrame-OuterFortress-Cart-01-Recomp
$env:APP_ISOLATED_TEST = "1"
dotnet test Dev\IronFrameCart01.slnx -c Release /warnaserror
```

## Pull Request Checklist

- [ ] All unit tests pass under `APP_ISOLATED_TEST=1` with zero warnings.
- [ ] Changes align with [Dev/docs/CHARTER.md](Dev/docs/CHARTER.md) and avoid duplicating `reCart` or `reEngine.Host` functionality.
- [ ] User-facing changes are noted in `CHANGELOG.md` under `[Unreleased]`.

## Clean-Room Rules

- **No Copyrighted Material:** Never commit or attach retail ROM images (`.nes`, `.prg`, `.chr`, `.fds`), BIOS files, extracted audio/textures, or Ghidra databases.
- **No Raw Disassembly or Retail Strings:** Do not paste copyrighted game text, verbatim scripts, or raw vendor disassembly into issues, code, or pull requests.
- **No Pre-Lifted Binaries:** Never commit `*.g.cs` or `*.Generated.dll` artifacts; recompilation recipes must execute cleanly on the player's own machine.
