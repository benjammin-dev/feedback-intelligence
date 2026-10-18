# Contributing

This document describes how work is done in this repository. It applies to every change, including changes made by the project owner.

## Workflow

1. **Never commit directly to `main`.** It's protected; all changes go through a pull request.
2. **Create a branch** from an up-to-date `main`, named `type/short-description`:
   - `feat/incremental-ingestion`
   - `fix/watermark-on-failure`
   - `docs/adr-0014`
3. **Keep pull requests small**: one logical change, ideally reviewable in under 15 minutes.
4. **Open a pull request** and fill in the template, including the self-review checklist and AI disclosure.
5. **Merge only when CI passes.** Use **Squash and merge** so each pull request becomes one clean commit on `main`.
6. **Delete the branch** after merging.

```bash
git switch main
git pull
git switch -c feat/my-change
# ...make changes...
git add .
git commit -m "feat: describe the change"
git push -u origin feat/my-change
gh pr create --fill
```

## Commit messages

Commits follow [Conventional Commits](https://www.conventionalcommits.org/): `type: short description in the imperative mood`.

| Type | Use for |
|---|---|
| `feat` | New behavior |
| `fix` | Bug fixes |
| `docs` | Documentation only |
| `test` | Adding or changing tests |
| `refactor` | Code changes that don't change behavior |
| `ci` | Workflows and automation |
| `chore` | Maintenance, such as dependency updates |

Reference requirement IDs where relevant, for example `feat: fetch issues since watermark (FR-ING-02)`.

## Self-review

There are no other reviewers on this project, so every pull request gets a deliberate self-review before merging:

- Read the full diff on GitHub, not just in the editor.
- Wait at least a few minutes, or come back later for larger changes, to read with fresh eyes.
- Complete every item in the pull request checklist honestly; leave an item unchecked and explain why if it doesn't apply.

## Code standards

- Code must pass `dotnet format --verify-no-changes` and build without warnings.
- Every public class and method has an XML doc comment explaining **what it does and why**.
- External services stay behind interfaces in `FeedbackIntel.Core` ([design](docs/design.md#3-components)).
- Never log issue text: run logs of a public repository are public. Log counts, IDs, and step names only.

## Tests

- New logic comes with unit tests; coverage for C# code stays at or above 80% (NFR-MNT-01).
- Every SQL transform has an expected-output test (NFR-MNT-02).
- Prompt changes must pass the evaluation set at 80% accuracy or better (FR-DEV-02).

## Documentation

- The Markdown files in `docs/` are official. When one changes, regenerate its PDF in the same pull request.
- Significant or hard-to-reverse decisions get a new [Architecture Decision Record](docs/adr/README.md). Accepted ADRs are never edited; a new ADR supersedes the old one.
- Changes to requirements, the data contract, or the design update the relevant document and its revision history.

## AI-assisted development

AI tools (such as GitHub Copilot and chat assistants) are used in this project to draft code, SQL, tests, documentation, and boilerplate. They speed up the work; they don't replace judgment. These rules apply to all AI-assisted changes:

1. **The author is responsible for every line.** AI-generated content is reviewed line by line and understood before it's committed. If you can't explain it, don't merge it.
2. **Disclose it.** Every pull request states which tools were used, for what, and what was changed after review.
3. **Verify, don't trust.** AI output is checked against the requirements and design, run locally, and covered by tests. Treat claims about library APIs, versions, and limits as unverified until checked.
4. **Never share secrets or personal data with AI tools.** No tokens, passwords, connection strings, or unredacted issue text in prompts.
5. **Security-sensitive code gets extra scrutiny.** Redaction, the data contract, AI output validation, and workflow permissions require tests that specifically exercise the risk, whoever wrote the code.
6. **Keep patterns consistent.** AI suggestions that don't match existing conventions in the codebase are rewritten to match, not accepted as a second style.
