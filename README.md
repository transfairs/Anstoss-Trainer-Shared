# Anstoss-Trainer-Shared

The foundation shared by the Anstoss trainers:

- [A2G-Trainer](https://github.com/transfairs/A2G-Trainer) for *Anstoss 2 Gold*
- [A3-Trainer](https://github.com/transfairs/A3-Trainer) for *Anstoss 3*

Each trainer embeds this repository as the git submodule `Shared/`. Nothing here is built on its own.

## Contents

| Path | What | Used by |
|---|---|---|
| `AnstossTrainer.Shared.projitems` (+ `.shproj`) | Visual Studio *shared project*: process memory access (`ProcessMemory`), `Logger`, main-window base (`TrainerShell`), view base (`TrainerView`), game observer loop (`GameObserver`), `ViewScaler`. Compiled into each trainer's exe, so every trainer stays a single file. | `<Import Project="Shared\AnstossTrainer.Shared.projitems" />` in each trainer's csproj |
| `app.manifest` | Application manifest (asInvoker, DPI aware) | each trainer's `<ApplicationManifest>` |
| `TestSupport/StaThread.cs` | Runs WinForms test code on an STA thread | linked into each trainer's test project |
| `help/style.css`, `help/script.js` | Look and behaviour of the online help pages. Each page names its repository in `<body data-repo="owner/repo">` for version, download and changelog. | copied next to each trainer's `help/` by its `pages.yml` |
| `scripts/Generate-Changelog.ps1`, `scripts/New-ChangelogHtml.ps1` | Changelog from git tags, rendered as HTML | each trainer's `release.yml` |

Game-specific additions to shared classes stay in the trainers as `partial` parts
(e.g. `Controller/ProcessMemory.A2.cs`, `Controller/Logger.A3.cs`).

## How a change gets into the trainers

1. Change something here: directly, or inside a trainer's `Shared/` folder (it is a full clone of
   this repository; commit and push from there).
2. **CI** ([`ci.yml`](.github/workflows/ci.yml)) builds and tests every trainer with the new commit,
   on every push and pull request. A change that breaks a trainer fails here.
3. When master is green, CI notifies the trainers (`repository_dispatch`, needs the secret
   `TRAINER_DISPATCH_TOKEN`). Each trainer's `shared-update.yml` then moves its `Shared/` submodule to
   the new commit, runs its own tests and
   - commits the update to master if they pass (and redeploys its online help),
   - opens a pull request with the failing update if they don't.

   Without the token the same update runs daily on a schedule; it can also be started by hand
   (Actions → "Update Shared" → Run workflow).

### One-time setup

- Create a fine-grained personal access token with access to `A2G-Trainer` and `A3-Trainer`,
  permission **Contents: Read and write**, and store it in this repository as the Actions secret
  `TRAINER_DISPATCH_TOKEN`.
- In each trainer repository: *Settings → Actions → General → Workflow permissions* →
  "Read and write permissions" and "Allow GitHub Actions to create and approve pull requests".

### Working locally

```bash
git clone --recursive https://github.com/transfairs/A2G-Trainer.git   # or A3-Trainer
git submodule update --remote Shared                                  # pull the newest Shared by hand
```
