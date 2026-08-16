# Issue tracker: GitHub

Issues and specs for this repository live in GitHub Issues:
`Breezesea1/BzsOIDC`.

Use the `gh` CLI from inside the repository for issue operations.

## Conventions

- Create: `gh issue create --title "..." --body-file <file>`
- Read: `gh issue view <number> --comments`
- List: `gh issue list --state open --json number,title,body,labels`
- Comment: `gh issue comment <number> --body "..."`
- Label: `gh issue edit <number> --add-label "..."`
- Close: `gh issue close <number> --comment "..."`

## Pull requests as a triage surface

PRs as a request surface: no.

## Skill operations

When a skill says "publish to the issue tracker", create a GitHub issue.
When a skill says "fetch the relevant ticket", read the complete issue and comments.

Use GitHub native issue dependencies for blocking edges when available.
Otherwise record `Blocked by: #<number>` in the dependent issue body.
