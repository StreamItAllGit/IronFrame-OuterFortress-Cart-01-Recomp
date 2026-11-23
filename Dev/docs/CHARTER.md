# Project Charter: IronFrame OuterFortress (Cart) 01 (`IronFrame-OuterFortress-Cart-01-Recomp`)

**Status:** Active  
**Last Updated:** 2026-11-23  
**Primary Audience:** Players seeking authentic `60.0988 Hz` gameplay with opt-in modern presentation, and developers studying clean-room static recompilation.

---

## 1. Core Identity & Mission
**IronFrame-OuterFortress-Cart-01-Recomp** (package identifier `ironframe-outerfortress-cart-01`, cartridge `IronFrame-OuterFortress-Cart-01.recomp`) is a clean-room `.NET 8.0` static recompilation module built on `reCart`, `reEngine`, and `Foundation`, derived strictly from our read-only hardware and decompilation research oracle (`IronFrame-OuterFortress-Cart-01-Decomp`). Its mission is to deliver a bit-authentic, native `60.0988 Hz` static recompilation **start-to-escape** as a slim, legally clean-room package compiled locally on the player's machine against their own legally dumped ROM.

## 2. Who It Is For (and Not For)
- **Built For:**
  - Players who own a legal cartridge dump and want authentic gameplay with optional modern display, audio, controller, save-vault, and accessibility quality-of-life enhancements.
  - Contributors and researchers verifying how a read-only decompilation oracle powers a pure Ahead-of-Time (AOT) static recompilation module without shipping copyrighted assets.
- **Not Built For:**
  - Running unverified or arbitrary third-party ROMs as a general-purpose console emulator (raw-media bring-up belongs to the `reCart` platform engine, not this title module).
  - Distributing retail ROM images, extracted graphics/audio, pre-lifted retail code, or derivative game scripts.

## 3. Non-Negotiable Principles
1. **Authentic `60.0988 Hz` Simulation Default:** Original gameplay timing, guard AI, alert states, collision hitboxes, RNG, and hardware bank semantics remain untouched by default. Every modern enhancement is strictly opt-in and isolated to presentation or user-interface convenience.
2. **Static Recompilation First — Never an Emulator:** Certified gameplay runs as native host machine code compiled locally on the player's PC from clean-room lift recipes (`aot-recipe.json`) and their verified ROM. Interpreter execution is quarantined strictly to differential verification.
3. **Zero-Asset Clean-Room Distribution:** Public source trees, `.recomp` cartridges, installers, and releases contain zero copyrighted ROM bytes, extracted tiles/audio, pre-lifted retail C# bodies, or raw retail disassembly. Public manifests, package names, and top-level documentation follow coded naming rules, confining commercial trademarks strictly to non-affiliation notices.
4. **Slim Game-Module Layering:** This repository owns only title-specific lift recipes, hardware/memory hooks, BYO checksum validation, clean-room manuals/trackers, and a thin launcher/installer wrapper. Rendering backends, audio DSP, input routing, overlay menus, and core 6502/2C02/2A03 hardware belong to `reEngine` and `reCart`—never duplicate them here.
5. **Zero-Daemon User Respect:** Installs cleanly without startup entries, background services, or telemetry. User ROMs (`data\rom\`), personal settings, and save-states are preserved across upgrades and isolated during automated testing.

## 4. Hard Boundaries & Non-Goals
- **Explicit Non-Goals:**
  - Shipping copyrighted ROMs, CHR/PRG dumps, extracted sprites/music, or pre-compiled game `.dll` assemblies inside Git or `.recomp` packages.
  - Adding emulator run-ahead latency hacks, external third-party achievement network integrations, or silent fallback to an interpreter during certified gameplay.
  - Hosting unrelated hardware studies (e.g., home-computer port comparisons) or building a standalone zero-emulation engine-free port inside this repository (any future native port belongs in its own separate repository).
- **Ecosystem & Ownership Seams:**
  - **`Foundation`:** Shared OS, containment, installer, and retro helpers (strictly read-only downstream).
  - **`reEngine`:** Suite router, SDL3 windowing, GPU/software presenters, audio DSP, input prompts, overlay menu, `.re*` pack loader, and AOT cache coordinator.
  - **`reCart`:** Cartridge 6502->C# static lifter, local AOT cache compiler, RP2A03/RP2C02/UNROM hardware runtime, and differential oracle.
  - **`IronFrame-OuterFortress-Cart-01-Decomp`:** Read-only upstream research archive supplying verified clean-room schemas and `.decomp` metadata (`Dev\schemas\` only; zero raw workspace copies).
  - **`IronFrame-OuterFortress-Cart-01-Recomp`:** Slim title module, lift recipe generator, dual `.nes`/`.prg` BYO validator, clean-root launcher, installer, and title-specific tests/manuals.
- **Safety, Data & Operational Boundaries:**
  - Never mutate user `data\rom\` files or overwrite canonical user installs during tests or nightly builds.
  - Public repository pushes require explicit human review (`requireHumanReviewBeforePush`) and clean pre-push history verification.

## 5. Agent Steering & Trade-Off Heuristics
When planning, building, or reviewing changes, resolve ambiguity using these priority rules:
1. **Prefer Simulation Authenticity over Cosmetic Convenience:** If a visual, sub-tick interpolation, `>8`-sprite, or `16:9` Hor+ enhancement risks altering physics, `$2002` PPU flags, AI cones, or `60.0988 Hz` memory state, keep it strictly render-only behind an opt-in toggle and revert cleanly to `4:3` / `60.0988 Hz` when unpatched.
2. **Prefer Upstream Reuse (`reCart` / `reEngine` / `Foundation`) over Local Code:** Before writing a helper, presenter hook, input mapper, or packaging script, verify whether `reCart` or `reEngine.Host` already owns that layer.
3. **Prefer Local Clean-Room Recipes over Static Bundling:** Never check in generated game code or compiled game DLLs to save a build step; express game-specific transforms as deterministic clean-room recipes executed locally against the player's ROM.
4. **When in Doubt:** Preserve clean-room boundaries, keep diffs localized and reversible, verify against isolated tests, and ask the maintainer before expanding scope or altering public manifests.

## 6. Conceptual Mental Model & Tone
Think of `IronFrame-OuterFortress-Cart-01-Recomp` as an empty, precision-engineered **clean-room cartridge shell** plus an **assembly manual**: on its own it contains zero copyrighted ink or silicon, yet when paired on the player's own machine with their legal ROM, `reCart` and `reEngine` compile and run it natively at `60.0988 Hz` with crisp pixel presentation, responsive controls, and modern save/vault ergonomics. The experience should feel crisp, disciplined, and respectful of the original 1987 tactical espionage atmosphere.

## 7. Amendment Policy
This charter guides all scoping (`/scope`), planning (`/plan`), and implementation (`/dev`). Agents must flag conflicts between a requested feature and this charter rather than silently bypassing it. Updating core principles or hard boundaries requires explicit human approval.
