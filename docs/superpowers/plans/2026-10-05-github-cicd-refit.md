# GitHub CI/CD refit

**Status:** in progress. Written 2026-10-05; Q1–Q6 decided 2026-10-05 (all recommendations accepted); Steps 1–3
complete 2026-10-05; Step 4 implemented locally (hosted gates pending).

**Supersedes:** an uncommitted 2026-09-30 dual-host mirror plan, abandoned and deleted when the project moved
its single home to GitHub; its still-relevant findings are folded into §1 below.

**Goal:** the repository's CI/CD runs on GitHub and nowhere else — the core gate on every push/PR, the paired
Linux/Windows artifact builds, and tag-driven release promotion to GitHub Releases — with no build input that
depends on the former Gitea host.

**Approach:** *replace, don't duplicate.* Each Gitea-specific piece is ported to its GitHub equivalent and the
Gitea original is removed in the same change. Gitea is frozen and nothing here keeps it working.

**Invariants that must survive the port unchanged:**

- The three-asset release contract: `OME-linux-x64.tar.gz`, `OME-windows-x64.zip`, `RELEASE-SHA256SUMS`.
- Artifact names, package roots (`OME-linux-x64`, `OME-windows-x64`), and the per-target evidence files
  (`BUILD-INFO.json`, `SHA256SUMS`, `package-smoke.log`, `WINDOWS-VERIFICATION.json`, `*.sha256`).
- All of `publish_*_release.py`'s artifact/evidence validation (archive vs. external checksum, internal vs.
  external `BUILD-INFO.json`/`SHA256SUMS`, smoke marker, Windows structural verification, target commit).
- The authority boundary: build jobs are `contents: read` with no secrets; only the promotion job can write,
  and only for `v*` tags.
- The FFmpeg inputs stay pinned by exact size + SHA-256; only their download location changes.
- `tools/build-linux-x64.sh` / `tools/build-windows-x64.sh` are invoked unchanged (they are the local and CI
  entry points alike).

---

## 1. Inventory — every Gitea coupling (verified 2026-10-05)

| Location | Coupling | Disposition |
|---|---|---|
| `.gitea/workflows/core-validation.yml` | `gitea.workflow`, `gitea.ref`, `gitea.run_id`, `gitea.run_attempt`; `christopherhx/gitea-upload-artifact@v4` | Step 1: port to `.github/workflows/`, delete |
| `.gitea/workflows/linux-release-build.yml` | `gitea.*` contexts throughout; `christopherhx/gitea-{upload,download}-artifact@v4`; `secrets.GITEA_TOKEN`; Gitea 1.25 granular `releases: write`; `--server`/`--repository` from `gitea.*` | Step 4: port, delete |
| `tools/publish_gitea_release.py` (494 lines) | `GiteaApi` (`/api/v1/repos/...`, `Authorization: token`, multipart `attachment=` upload); `--server`; `GITEA_TOKEN`; "Gitea" in user-facing strings, including inside the otherwise host-neutral `promote_release` and `_validate_release` | Step 3: becomes `publish_github_release.py` |
| `tools/test_publish_gitea_release.py` | module import + `PublishGiteaReleaseTests`; `FakeApi` is already host-neutral | Step 3: renamed with the tool |
| `tools/test_release_workflow.py` | `WORKFLOW = .gitea/workflows/linux-release-build.yml`; asserts manifest `url` starts with `https://git.orfl.xyz/api/packages/conco/generic/ome-ffmpeg-sdk/`; asserts `releases: write` absent from build jobs | Steps 2 and 4 |
| `tools/validate.py` | `CORE_TESTS` lists `test_publish_gitea_release.py` | Step 3 |
| `native/age_movie_ffmpeg/dependency-{linux-x64,win64}.json` | `url` → Gitea generic package registry | Step 2 |
| `tools/install_godot_templates.py:25` | `User-Agent: ... (+https://git.orfl.xyz/conco/OpenMaidEngine)` | Step 2 |
| `docs/tools-reference.md` (~49–94, ~310–312), `docs/PROJECT-STRUCTURE.md` (41–42, 56, 66), `docs/platform-portability.md` (~55–164, ~268) | describe the Gitea workflows, tool, package host, and attachment ceiling as current | Step 5 |
| `docs/remake-architecture-and-roadmap.md` (~1004–1171) | historical CI/CD record | Step 5: **append** a new entry; do not rewrite history |
| Workspace `AGENTS.md` Conventions | "`.gitea/workflows` are legacy until replaced" | Step 5 |

Not coupled (port unchanged): `actions/checkout@v4`, `actions/setup-python@v6`, `actions/setup-dotnet@v4`,
`actions/cache@v4`, the MinGW `apt-get` step, `tools/godot-linux-x64.json` (already points at GitHub/godotengine),
the three FFmpeg bootstraps (they read only `url` and verify size + SHA-256), and the core gate itself
(`validate.py --level core` never touches the FFmpeg SDK).

**Correction carried over from the superseded plan:** the current workflows reference actions by major-version
tag. The roadmap's "all four official actions are pinned to full immutable release SHAs" (~line 997) describes
the earlier, since-replaced `.github/workflows/core-validation.yml`, not today's files.

---

## 2. Decisions

### Already decided

1. **GitHub is the only CI/CD host.** No dual maintenance; `.gitea/` is deleted as its replacements land.
2. **Tag push drives releases.** With a single host there is no cross-host race, so the Gitea semantics carry
   over: pushing a `v*` tag builds both targets and promotes. `workflow_dispatch` remains for build-only runs.
3. **The release tool is renamed, not generalized.** `publish_gitea_release.py` → `publish_github_release.py`
   via `git mv`, so history follows the file. No host-neutral core/adapter split — there is only one adapter.

### Q1–Q6 — decided 2026-10-05 (recommendations accepted)

| # | Decision |
|---|---|
| Q1 | FFmpeg SDK archives on a GitHub Release of this repository under a non-`v` tag (`deps-ffmpeg-btbn-autobuild-2026-08-17-13-05`); verify downloaded bytes against the manifests before switching; confirm LGPL re-hosting notes |
| Q2 | Every action pinned to a full commit SHA with a version comment; `.github/dependabot.yml` bumps them weekly |
| Q3 | All jobs on `ubuntu-24.04` |
| Q4 | Push `v0.1.0`–`v0.3.3` tags without GitHub releases, before Step 4 lands |
| Q5 | Draft → upload → verify → publish; then enable immutable releases |
| Q6 | `develop` is the GitHub default branch; workflows trigger on `develop` only |

The option analysis below is kept as the rationale.

**Q1 — Where do the FFmpeg SDK archives live?** (blocks Step 2)
BtbN prunes dated autobuilds, so `upstream_url` cannot be the build input. Options:

- **A (recommended): a GitHub Release on this repository** under a non-`v` tag such as
  `deps-ffmpeg-btbn-autobuild-2026-08-17-13-05`, carrying both archives (56,931,952-byte Linux `.tar.xz`
  and 70,837,934-byte win64 `.zip`, ~128 MB). The non-`v` tag cannot trigger the release workflow. Download
  URLs are stable: `https://github.com/concopuffs/OpenMaidEngine/releases/download/<tag>/<archive>`.
- B: a separate `OpenMaidEngine-deps` repository's releases — cleaner release list, one more repo to own.
- C: GitHub Packages — not a good fit for opaque archives (no generic registry).

Whichever is chosen: the bytes are uploaded from the existing verified copies, then **downloaded back and
checked against the manifest's `size`/`sha256` before the manifest URL changes.** Also confirm
`THIRD_PARTY_NOTICES.md` and the FFmpeg licensing notes cover publicly re-hosting the LGPL binaries from GitHub
(corresponding-source obligation) — re-hosting makes the project their distributor there.

**Q2 — Action pinning policy.** (blocks Step 1)
Recommended: pin every action to a full commit SHA with the version in a trailing comment, and add
`.github/dependabot.yml` for the `github-actions` ecosystem so pins are bumped by reviewed PRs. Alternative:
keep major-version tags (status quo). Pick one and apply it to every workflow file in this plan.

**Q3 — Runner image.** (blocks Step 1)
`ubuntu-latest` moves under us. The Linux FFmpeg build already fails if the bundle needs a glibc newer than the
manifest's `minimum_glibc` (2.28) (`native/age_movie_ffmpeg/build-linux-x64.sh` ~111–127), so an image bump
can break releases without any repository change. Recommended: pin `ubuntu-24.04` for all jobs and bump
deliberately. Verify on the first hosted run that the guard passes there.

**Q4 — Historical tags.** (blocks nothing; decide before the first release)
`v0.1.0`–`v0.3.3` exist locally but not on GitHub; their releases and artifacts live only on Gitea.
Recommended: push the tags (so `git describe` and history make sense) **without** creating GitHub releases for
them; the first GitHub release is the next version. Note that pushing an old `v*` tag *after* Step 4 lands
would trigger a build and promotion for it — push them before Step 4, or accept/cancel those runs.

**Q5 — Immutable releases.** (blocks Step 3)
GitHub's immutable-releases setting forbids asset changes after publication. The current tool creates a
**published** release first and uploads into it, which is incompatible with that setting and briefly exposes
a release with missing assets. Recommended regardless of the setting: switch to **draft → upload → verify →
publish** (Step 3), then enable immutable releases.

**Q6 — Default branch and `main`.** (blocks Step 1's trigger list)
Only `develop` is on GitHub. Recommended: make `develop` the GitHub default branch and trigger on `develop`
only; add `main` to the triggers if and when `main` is fast-forwarded and pushed.

---

## 3. Execution plan

Each step is independently landable and gated; do not combine a mechanical port with a behavior change.

### Step 0 — Repository settings (no code)

- Actions enabled; **Settings → Actions → Workflow permissions: read-only** default (jobs request writes
  explicitly); "Allow GitHub Actions to create and approve pull requests" off.
- Fork pull-request workflows: require approval for first-time contributors.
- Default branch per Q6. Optional: branch protection on `develop` requiring the core gate.

**Gate:** settings recorded in Step 5's docs.

### Step 1 — Core gate on GitHub

**Adds** `.github/workflows/core-validation.yml`; **deletes** `.gitea/workflows/core-validation.yml`.

| Gitea | GitHub |
|---|---|
| `group: core-${{ gitea.workflow }}-${{ gitea.ref }}` | `group: core-${{ github.workflow }}-${{ github.ref }}` |
| `christopherhx/gitea-upload-artifact@v4` | `actions/upload-artifact@v4` (pinned per Q2) |
| `core-validation-${{ gitea.run_id }}-${{ gitea.run_attempt }}` | `core-validation-${{ github.run_id }}-${{ github.run_attempt }}` |
| `runs-on: ubuntu-latest` | per Q3 |

Keep the triggers (`push`/`pull_request` on the Q6 branch list, `workflow_dispatch`), `permissions: contents:
read`, `persist-credentials: false`, the env block, step order, `--level core`, the `if: failure()` upload,
`if-no-files-found: ignore`, `retention-days: 7`. Use `pull_request`, never `pull_request_target`.

**Gate:** `actionlint` clean; the first push to `develop` runs green on GitHub; local
`py -3.11 -X utf8 tools/validate.py --level core` unchanged and green.

**Result (2026-10-05, local):** `.github/workflows/core-validation.yml` and `.github/dependabot.yml` added;
`.gitea/workflows/core-validation.yml` removed. The diff against the Gitea file is exactly the substitution
table above. Pins: `actions/checkout` v4.4.0 `11d5960a…`, `actions/setup-python` v6.3.0 `ece7cb06…`,
`actions/setup-dotnet` v4.3.1 `67a3573c…`, `actions/upload-artifact` v4.6.2 `ea165f8d…` (resolved from each
action's own tags). `actionlint` 1.7.12 (checksum-verified release binary): clean. Local `--level core`: green.
`tools-reference.md`, `PROJECT-STRUCTURE.md`, and `platform-portability.md` now describe the GitHub gate.
**Hosted (2026-10-05):** committed as `5d429b2` and pushed with tags `v0.1.0`–`v0.3.3` (Q4; no releases).
The first GitHub run, Actions run 37381034592, passed every step in under a minute on the hosted runner.
Step 0 settings confirmed by the user. **Step 1 is complete.**

### Step 2 — Re-host the FFmpeg SDK

**Blocked by:** Q1.

1. Create the dependency release (Q1) and upload both archives from the existing verified local copies.
2. Download each back on a clean `build/downloads/` and verify size + SHA-256 against the unchanged manifests.
3. Change only `url` in both manifests. `size`, `sha256`, `archive`, `release_tag`, `upstream_url`, and the
   other fields stay byte-identical. Decide whether `mirror_version` keeps its meaning (it names the Gitea
   package version today); if the GitHub tag embeds the same `btbn-autobuild-...` string, keep the field and the
   test's `/{mirror_version}/` path assertion still holds.
4. `tools/test_release_workflow.py`: change the host-prefix assertion to the GitHub prefix.
5. `tools/install_godot_templates.py`: User-Agent →
   `OpenMaidEngine-build/1.0 (+https://github.com/concopuffs/OpenMaidEngine)`
   (`tools/test_install_godot_templates.py` does not assert the string; no test change needed).

**Gate:** all three bootstraps (`bootstrap-linux-x64.sh`, `bootstrap-win64.sh`, `bootstrap-win64.ps1`) download
and verify from the new URL on a clean `build/downloads/`; `--level core` green; local Linux and Windows
artifact builds complete under WSL. Nothing in the tree still names `git.orfl.xyz` except historical docs.

**Result (2026-10-05, local):** the user created release `deps-ffmpeg-btbn-autobuild-2026-08-17-13-05` in the
web UI with both pinned archives (not marked latest, not immutable). Fresh anonymous downloads of both assets
match the manifests' size and SHA-256 exactly. Both manifests' `url` now point at that release (all other fields
byte-identical); `test_release_workflow.py` now asserts the exact derived URL instead of a host prefix plus a
`/{mirror_version}/` segment (the new path segment is `deps-ffmpeg-{mirror_version}`, which the old segment
check could not match); the Godot template downloader's User-Agent names the GitHub repository. From an empty
`build/downloads/` in a scratch copy, `bootstrap-win64.ps1` (Windows PowerShell 5.1) and both Bash bootstraps
(WSL) downloaded, verified, and extracted successfully. **Environment finding:** the Bash bootstraps' `curl
--remove-on-error` needs curl ≥ 7.83; this machine's WSL Ubuntu 20.04 (7.68) and Git Bash (7.69) lack it, so the
Bash runs used a test-only PATH shim that dropped that one flag; `ubuntu-24.04` (Q3) ships curl 8.x. Local WSL
artifact builds are unaffected because the archives are byte-identical and already cached; Step 4's first hosted
build exercises the real download. Core gate green. `tools-reference.md` and `platform-portability.md` updated.
**Open (Q1 licensing):** `THIRD_PARTY_NOTICES.md` has no FFmpeg section, and the deps release carries the BtbN
archives' bundled license text but no pointer to corresponding source; `platform-portability.md` already states
that release artifacts must carry the matching FFmpeg source/configuration and notices. Needs a decision before
the first `v*` release.

### Step 3 — Release tool for GitHub

**Blocked by:** Q5.

`git mv tools/publish_gitea_release.py tools/publish_github_release.py` and
`git mv tools/test_publish_gitea_release.py tools/test_publish_github_release.py`; update `validate.py`
`CORE_TESTS`. Keep every validation function and constant as is. Replace `GiteaApi` with `GithubApi`:

- **Base:** `https://api.github.com/repos/{owner}/{name}`; headers `Authorization: Bearer <token>`,
  `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`, the existing User-Agent. Drop
  `--server`. Token from `GITHUB_TOKEN` with the existing shape guard.
- **Find release:** `GET /releases/tags/{tag}` returns **published releases only — never drafts**. To resume a
  draft from an earlier failed run, also scan `GET /releases?per_page=100` for a draft whose `tag_name` matches.
  More than one matching draft is a hard error.
- **Create:** `POST /releases` with the existing payload but `"draft": true`.
- **List assets:** `GET /releases/{id}/assets?per_page=100`.
- **Upload:** `POST https://uploads.github.com/repos/{owner}/{name}/releases/{id}/assets?name=<asset>` with the
  raw bytes, `Content-Type: application/octet-stream`. Keep the credential off the process argument list: curl
  with `--config -` (stdin) and `--data-binary @<path>`, exactly as the current tool passes its token.
- **Interrupted uploads:** GitHub can leave an asset in a non-`uploaded` `state` after a failed upload, which
  blocks re-upload under the same name. While the release is still a draft, an existing asset whose `state`
  is not `uploaded` is deleted (`DELETE /releases/assets/{asset_id}`) and re-uploaded. An `uploaded` asset whose
  size differs is still refused, as today.
- **Publish:** after the final asset verification passes, `PATCH /releases/{id}` with `{"draft": false}`, then
  re-read and require `draft == false`.

`promote_release` / `_validate_release` changes:

- The release flow becomes find-or-create **draft** → reconcile assets → upload missing → verify the exact
  three-asset set and sizes → publish. An already-**published** release with the exact verified asset set is
  accepted as a completed idempotent re-run; a published release with missing or extra assets is a hard error
  (it cannot be fixed without unpublishing).
- `_validate_release` currently requires `draft == False` and `target_commitish == target`. Draft state is now
  expected before publish. **Verify at implementation time** what GitHub returns in `target_commitish` when the
  tag already exists (the API ignores `target_commitish` in that case). If it does not echo the SHA, validate the
  tag's commit separately (`GET /git/ref/tags/{tag}`, dereferencing an annotated tag via
  `GET /git/tags/{sha}`) instead of trusting that field.
- Replace "Gitea" in user-facing strings with "GitHub".

Tests (`test_publish_github_release.py`): keep every existing validation and collision test; extend `FakeApi`
for draft resume, multiple-draft refusal, non-`uploaded` asset replacement, publish-after-verify, idempotent
re-run against a complete published release, refusal on an incomplete published release, and an assertion that
the token never appears in a subprocess argument list.

**Gate:** the new test module and `--level core` are green.

**Result (2026-10-05, local):** `publish_github_release.py` and `test_publish_github_release.py` renamed with
`git mv`; `validate.py` `CORE_TESTS` updated. All ten artifact/evidence validation functions are unchanged
(AST-identical to the Gitea tool). `target_commitish` question resolved empirically: the web-UI deps release
reports `target_commitish == "develop"`, so the tool never trusts it; it resolves the tag via
`GET /git/ref/tags/{tag}` (dereferencing annotated tags; confirmed live that `v0.3.3` resolves to its commit)
and requires equality with `--target` before touching any release. Flow: published+complete → idempotent
success; published+incomplete → refused; otherwise find-or-create the single draft → delete non-`uploaded`
placeholders → upload missing → verify exact set/sizes/state → `PATCH draft=false, make_latest=true` → re-validate.
12 tests (4 adapted originals + 8 new) pass, including the token-off-argv check; core gate green.
`tools-reference.md` and `PROJECT-STRUCTURE.md` updated. The legacy `.gitea` release workflow still names the
old script; Step 4 moves and rewrites it. The live GitHub API path is first exercised by Step 4's throwaway tag.

### Step 4 — Release builds and promotion on GitHub

**Blocked by:** Steps 2 and 3; Q4's tag push done or deliberately deferred.

`git mv .gitea/workflows/linux-release-build.yml .github/workflows/release-build.yml` (the file builds both
targets; the old name was historical), then:

| Gitea | GitHub |
|---|---|
| `group: release-builds-${{ gitea.ref }}` | `group: release-builds-${{ github.ref }}` |
| `christopherhx/gitea-upload-artifact@v4` / `-download-artifact@v4` | `actions/upload-artifact@v4` / `actions/download-artifact@v4` (pinned per Q2) |
| `OME-linux-x64-${{ gitea.sha }}`, `OME-windows-x64-${{ gitea.sha }}` | `${{ github.sha }}` |
| `*-failure-${{ gitea.run_id }}-${{ gitea.run_attempt }}` | `${{ github.run_id }}-${{ github.run_attempt }}` |
| `if: startsWith(gitea.ref, 'refs/tags/v')` | `if: startsWith(github.ref, 'refs/tags/v')` |
| `permissions: contents: read` + `releases: write` (publish job) | `permissions: contents: write` (publish job only) |
| `GITEA_TOKEN: ${{ secrets.GITEA_TOKEN }}` | `GITHUB_TOKEN: ${{ github.token }}` |
| `publish_gitea_release.py --server ... --repository "${{ gitea.repository }}"` | `publish_github_release.py --repository "${{ github.repository }}"` |
| `--tag "${{ gitea.ref_name }}" --target "${{ gitea.sha }}"` | `--tag "${{ github.ref_name }}" --target "${{ github.sha }}"` |
| `runs-on: ubuntu-latest` | per Q3 |

For a tag push, `github.sha` is the tagged commit, so `--target` is correct; the publish job additionally asserts
`git rev-parse "refs/tags/${{ github.ref_name }}^{commit}"` equals `github.sha` before promoting.

Keep unchanged: triggers (`develop` push, `v*` tag push, `workflow_dispatch`), workflow-level `contents: read`,
`persist-credentials: false`, both build jobs' steps and caches, artifact retention (30 days verified, 7 days
failure), `if-no-files-found: error` on verified uploads. The Gitea-era 1 GiB-cgroup workaround in the Linux
build stays — it is harmless on larger runners.

`tools/test_release_workflow.py`: point `WORKFLOW` at the new path; the build-job read-only assertion checks
`contents: write` (not `releases: write`) is absent from both build jobs and still forbids `secrets.` there;
assert the publish job is tag-gated, holds `contents: write`, uses `github.token`, and calls
`publish_github_release.py`.

**Gate:**

1. A `develop` push builds and uploads both verified artifacts.
2. A throwaway tag (e.g. `v0.0.1-ci.1`) produces a published release with exactly the three assets, and
   `RELEASE-SHA256SUMS` matches both downloaded archives. Then delete that release and tag.
3. Re-running the promotion job on the same tag is a no-op success (idempotence on GitHub).

**Result (2026-10-05, local):** `git mv .gitea/workflows/linux-release-build.yml .github/workflows/release-build.yml`;
`.gitea/` no longer exists. A line diff against the Gitea file shows exactly the substitution table: `github.*`
contexts, `ubuntu-24.04` on all three jobs, every action SHA-pinned (adds `actions/cache` v4.3.0 `0057852b…` and
`actions/download-artifact` v4.3.0 `d3f86a10…` to the Step 1 pins), the official upload/download artifact actions,
`contents: write` replacing the Gitea-only `releases: write` on the publish job alone, `github.token` as
`GITHUB_TOKEN`, and `publish_github_release.py` without `--server`. The planned extra publish-job
`git rev-parse` tag check was dropped as redundant: Step 3's tool already verifies the tag's commit against
`--target` through the GitHub API, which is stronger than a check against the runner's own checkout.
`test_release_workflow.py` now reads the new path, asserts the GitHub publish wiring and that build jobs hold no
write permission or token, forbids `secrets.` anywhere in the file, and gains a cross-workflow policy test (full-SHA
pins with version comments, `ubuntu-24.04` only, no `pull_request_target`, no `.gitea/`). `actionlint` clean on both
workflows; 8 policy tests and the core gate green. `tools-reference.md`, `PROJECT-STRUCTURE.md`,
`platform-portability.md` (Gitea-era acceptance history kept and labelled as such), and workspace `AGENTS.md`
updated. **Pending:** hosted gates 1–3 (develop push build, throwaway-tag release, idempotent re-run).

### Step 5 — Remove Gitea remnants and close out docs

- Delete the now-empty `.gitea/` directory.
- `docs/tools-reference.md`: CI sections describe `.github/workflows/*`; the publisher row becomes
  `publish_github_release.py`; the test rows are renamed; the FFmpeg manifest text names the GitHub host; drop the
  "Gitea package version" immutability wording in favor of the Q1 tag/asset rule.
- `docs/PROJECT-STRUCTURE.md`: tree shows `.github/workflows/` (+ `dependabot.yml` if Q2 chose it) and the
  renamed tool/test.
- `docs/platform-portability.md`: hosted CI is GitHub Actions; state the runner image (Q3), the authority
  boundary, the draft→publish flow, and the current pinning policy (Q2). Remove the Gitea attachment-ceiling
  rationale or mark it historical.
- `docs/remake-architecture-and-roadmap.md`: **append** a dated entry recording the move to GitHub CI/CD and
  that the earlier `.github/workflows/core-validation.yml` effectively returns. Do not rewrite the Gitea-era
  entries.
- `THIRD_PARTY_NOTICES.md`: per Q1's licensing check.
- Workspace `AGENTS.md`: drop the "`.gitea/workflows` are legacy" clause.
- Status memory: CI/CD now on GitHub; next item.

**Gate:** `git grep -i gitea` finds only historical roadmap/plan text and the frozen remote's mention in
`AGENTS.md`; no doc names a path that does not exist; the canonical-documents map is still accurate.

---

## 4. Sequencing

Q2, Q3, Q6 → Step 0 → Step 1 → (Q1) Step 2 → (Q5) Step 3 → (Q4 tags pushed) Step 4 → Step 5.

Step 1 is independent of everything after it and gives GitHub CI immediately. Steps 2 and 3 can proceed in
either order or in parallel; Step 4 needs both.

## 5. Non-targets

- Keeping anything working on Gitea, or a shared/templated multi-host workflow.
- Changing build scripts, package layout, artifact names, or the three-asset contract.
- Backfilling GitHub releases for `v0.1.0`–`v0.3.3`.
- Native Windows runners, code signing, installers, or a Windows execution smoke (the Windows artifact stays
  structurally verified on Linux, as today).
- A download fallback of any kind that skips size + SHA-256 verification.

## 6. Definition of done

A push to `develop` runs the core gate and both artifact builds on GitHub; pushing a `v*` tag publishes a GitHub
release with exactly `OME-linux-x64.tar.gz`, `OME-windows-x64.zip`, and `RELEASE-SHA256SUMS`, all verified;
every build input resolves from GitHub or the upstream projects' own hosts; and the repository contains no live
Gitea coupling.
