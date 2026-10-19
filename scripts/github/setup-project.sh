#!/usr/bin/env bash
# Creates (or updates) the "Groovefin" GitHub Project board and adds all open issues.
#
# Requirements: gh CLI logged in with the "project" scope:
#   gh auth refresh -s project
#
# Usage:
#   scripts/github/setup-project.sh                 # defaults: sysmike/groovefin, title "Groovefin"
#   OWNER=me REPO=groovefin TITLE=Groovefin scripts/github/setup-project.sh
#
# The script is idempotent: it reuses an existing project with the same title, and adding an
# issue that is already on the board is a no-op.
set -euo pipefail

OWNER="${OWNER:-sysmike}"
REPO="${REPO:-groovefin}"
TITLE="${TITLE:-Groovefin}"

echo "Project '$TITLE' for $OWNER/$REPO"

number="$(gh project list --owner "$OWNER" --format json --limit 100 \
  --jq ".projects[] | select(.title == \"$TITLE\") | .number" | head -n 1)"
if [[ -z "$number" ]]; then
  number="$(gh project create --owner "$OWNER" --title "$TITLE" --format json --jq '.number')"
  echo "Created project #$number"
else
  echo "Using existing project #$number"
fi

project_id="$(gh project view "$number" --owner "$OWNER" --format json --jq '.id')"
gh project link "$number" --owner "$OWNER" --repo "$OWNER/$REPO" >/dev/null 2>&1 || true

# Status column options: Todo / In progress / In review / Done.
status_field_id="$(gh project field-list "$number" --owner "$OWNER" --format json \
  --jq '.fields[] | select(.name == "Status") | .id')"
gh api graphql -f fieldId="$status_field_id" -f query='
  mutation($fieldId: ID!) {
    updateProjectV2Field(input: {
      fieldId: $fieldId,
      singleSelectOptions: [
        { name: "Todo",        color: GRAY,   description: "Not started" },
        { name: "In progress", color: YELLOW, description: "Assigned and being worked on" },
        { name: "In review",   color: BLUE,   description: "Pull request open" },
        { name: "Done",        color: GREEN,  description: "Merged or closed" }
      ]
    }) { projectV2Field { ... on ProjectV2SingleSelectField { id } } }
  }' >/dev/null
todo_option_id="$(gh project field-list "$number" --owner "$OWNER" --format json \
  --jq '.fields[] | select(.name == "Status") | .options[] | select(.name == "Todo") | .id')"
echo "Status options set"

gh issue list --repo "$OWNER/$REPO" --state open --limit 1000 --json url --jq '.[].url' |
while read -r url; do
  item_id="$(gh project item-add "$number" --owner "$OWNER" --url "$url" --format json --jq '.id')"
  gh project item-edit --id "$item_id" --project-id "$project_id" \
    --field-id "$status_field_id" --single-select-option-id "$todo_option_id" >/dev/null
  echo "  added $url"
done

cat <<MSG

Done. Finish the setup in the browser (Project → ⋯ → Workflows):
  - "Item closed"            → set Status to Done
  - "Pull request merged"    → set Status to Done
  - "Auto-add to project"    → repo $OWNER/$REPO, filter: is:issue is:open
MSG
