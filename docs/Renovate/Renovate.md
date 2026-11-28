# Renovate

## Secrets

The self-hosted Renovate run needs one GitHub Actions secret.

- `RENOVATE_TOKEN` — a PAT used by the workflow. A PAT (not `GITHUB_TOKEN`)
  lets Renovate-triggered changes, such as automerge, start further required
  workflow runs. The workflow keeps a minimal `permissions:` block
  (`contents: read`, `packages: read`) because the PAT does the
  repository writes.

There is no private package feed, so no Renovate host rules are configured;
public packages on nuget.org require none.
### Validating the config

Run this command in the repo root:

```powershell
npx --yes --package renovate -- renovate-config-validator
```

## Workflow triggers

The self-hosted workflow runs on a daily cron, on `workflow_dispatch`, and on
pushes to the default branch that touch certain paths. Get the path filter
wrong and Renovate silently stops reacting to the things that matter.

- **Watch both config files.** `renovate-config.json` is the runner config
  passed to the action via `configurationFile:`; `renovate.json` holds the
  `packageRules`. A filter listing only the runner config means editing a
  package rule never triggers a run, so the change sits dormant until the next
  cron.
- **Watch the shared dependency manifests.** When a dependency PR merges, every
  other open dependency PR is suddenly one commit behind the default branch,
  and PRs that touch the same manifest become conflicted. Triggering Renovate
  on a push to those manifests lets it rebase the rest immediately instead of
  leaving them stuck until the next cron. In a .NET repo with Central Package
  Management that means `Directory.Packages.props` (at the repo root here) and `**/dotnet-tools.json`.
- **Add a concurrency group, do not cancel.** The merge trigger makes
  overlapping runs likely, and Renovate mutates branches. Use
  `group: renovate` with `cancel-in-progress: false`: an in-progress run may
  predate the merge that triggered the new one, so cancelling it could discard
  the rebase you wanted.

```yaml
concurrency:
  group: renovate
  cancel-in-progress: false
```

Before adding manifest paths, sanity-check the extra load: replay recent
default-branch history against the proposed filter and compare the resulting
run count to the measured run duration. On a busy monorepo this worked out to
roughly 2-3 extra runs a day at 1.4-5.1 minutes each.

There is no feedback loop to worry about as long as manifest changes reach the
default branch only through merged PRs, because the Renovate run itself writes
only to `renovate/*` branches.

### Keeping PRs mergeable

This repo has no companion workflow that updates open PR branches; Renovate
rebases its own branches when the manifest trigger above or the daily cron
runs. If non-Renovate PRs start falling behind, add an up-to-date workflow
(for example `paritytech/up-to-date-action` on pushes to the default branch),
pinned to a commit SHA rather than `@main`, since it would run privileged with
the PAT.
### If you switch to `GITHUB_TOKEN`

The workflow uses a PAT today. Switching to `GITHUB_TOKEN` needs a broader
`permissions:` block (`contents`, `issues`, `pull-requests` and `statuses`
write) and the repository setting *Allow GitHub Actions to create and approve
pull requests*; without them Renovate fails with `platform-unknown-error` or
`integration-unauthorized`, and `GITHUB_TOKEN` cannot trigger further required
workflow runs for automerge.
### Automerging

For automerge to actually merge PRs, GitHub's auto-merge feature must be
enabled on the repository, and any protected branch must allow it.

- <https://docs.renovatebot.com/key-concepts/automerge/>
- <https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/managing-auto-merge-for-pull-requests-in-your-repository#managing-auto-merge>
