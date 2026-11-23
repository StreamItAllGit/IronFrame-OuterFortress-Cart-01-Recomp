# Context: IronFrame-OuterFortress-Cart-01-Recomp

- **Project (4-Folder, `RECOMP`):** `v0.1.0` (`ironframe-outerfortress-cart-01`, downstream of read-only `IronFrame-OuterFortress-Cart-01-Decomp` `v0.4.0`; targets `reCart v0.1.3`, `reEngine v0.6.2`, `Foundation v0.5.5`). Zero `Corpus\` copy; zero C# edits outside `Dev/`.
- **Control:** [active.state](active.state) | [app.manifest](Dev/app.manifest.json) | [module](Dev/module.recomp.json)

## 1. Quartet
- [README](README.md) | [FEATURES](FEATURES.md) | [ROADMAP](ROADMAP.md) | [CHANGELOG](CHANGELOG.md)

## 2. Manual Shelf (`Dev/docs/`)
- **Project Charter & Boundaries:** [CHARTER](Dev/docs/CHARTER.md)
- [ARCHITECTURE](Dev/docs/ARCHITECTURE.md) | [INTERFACES](Dev/docs/INTERFACES.md) | [USER_GUIDE](Dev/docs/USER_GUIDE.md) | [OPERATIONS](Dev/docs/OPERATIONS.md)

## 3. Governance
- **Public Repo:** `isPublicRepo: true` (`StreamItAllGit/IronFrame-OuterFortress-Cart-01-Recomp`) | `requireHumanReviewBeforePush: false` | `APP_ISOLATED_TEST=1`
- **Discord `/post` Hook:** Every human-approved build/release and `/hotfix` pushes `/post` via `active.state` -> `discordPost` (`DISCORD_WEBHOOK_FOUNDATION`), tagging `@StreamDoesDev`.
- **Notion:** Manual-Only Sync (explicit human request only; never auto-sync)
