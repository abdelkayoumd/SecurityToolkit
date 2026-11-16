# Security Toolkit roadmap

Written 2026-11-15, after release 1.3.0. Sizes are relative effort: S (a session), M (a few sessions), L (a release of its own).
Each release is shippable on its own; the order inside a release is the build order.

## Principles

- Read-only by default. Anything that writes goes through review, a restore file and undo.
- The user thinks of a person, a role or a table, not of a screen name.
- Every new screen uses the sentence pattern ("3 roles grant [Read] on 2 tables at [User] level or wider").
- Every release is checked on a real organization before it is tagged.

## 1.4.0 Finish what is built (folded into 1.5.0: no separate 1.4.0 release)

| # | Item | Size | Done when |
|---|------|------|-----------|
| 1 | Privilege finder: tick one, several or all privileges | done | Tried in XrmToolBox on a real org |
| 2 | New Excel export format (header, filter, colours, About sheet) | done | Sample opened in Excel and the colours approved |
| 3 | Privilege finder: check the Business Unit column (root role copy only?) | S, carried over to 2.0.0 | Result matches the role editor for a role copied to a child business unit |
| 4 | Verify Privileged users on a real org | done | Counts match a manual check of two users |
| 5 | Verify Record access on a real record, including a team-owned one | done | Answer matches the platform's "Check access" |
| 6 | New icon reaches NuGet and the Tool Library | done (1.5.0) | Released |

## 1.5.0 Fluency: stop starting from an empty screen (released 2026-11-16)

| # | Item | Size | Depends on | Done when |
|---|------|------|------------|-----------|
| 7 | Carry the selection: right-click a user, team, role or table in any grid and open another screen with it selected | built | | Works from every grid to every screen that takes that kind of item |
| 8 | Global search box: type a name, get the item and its actions | built | 7 | A user is reachable in one search plus one click from any screen |
| 9 | Remember state per environment: last screen, filters, splitters, recent users | built (last screen, options, recent items; the panes have no splitters to remember) | | Reopening the tool restores the last view |
| 10 | Consistency: one export command name, F5 refresh, Ctrl+E export, "loaded at 10:42" next to Refresh | built | | Same on all screens |

## 2.0.0 Simplify: fewer screens, same answers

A major version because the menu changes.

| # | Item | Size | Depends on | Done when |
|---|------|------|------------|-----------|
| 3 | Privilege finder Business Unit column (carried over from 1.4.0) | S | | Result matches the role editor for a role copied to a child business unit |
| 11 | User workspace: A user's access, Explain access and Record access as tabs of one screen (what, why, this record) | built | 7 | The three old menu entries are gone and nothing was lost |
| 12 | One Compare screen for two users or two roles | built | | One menu entry |
| 13 | One Assign screen for roles for users, roles for teams and team membership | built | | One menu entry, same review and undo |
| 14 | Sentence pattern on Explain access and Compare, using the shared dropdowns | built | 11, 12 | Each screen states its question and result in one sentence |
| 15 | Home, "How it works", README and screenshots updated for the new menu | S | 11 to 14 | No reference to a removed screen |

Target: 17 screens become about 10.

## 2.1.0 Deeper answers

| # | Item | Size | Depends on | Done when |
|---|------|------|------------|-----------|
| 16 | Non-table privileges in the Privilege finder (Export to Excel, Bulk Delete, Act on Behalf of Another User...) | M | | Roles holding a chosen miscellaneous privilege are listed |
| 17 | Redundant roles: roles a user holds that add nothing beyond their other roles | M | | Listed in the Cleanup report, with the remove action |
| 18 | "What if" preview: a user's access before and after adding or removing a role | M | 11 | Shown in the review step of every role change |
| 19 | Application users view | S | | Listed apart from people, with their roles |
| 20 | Column security profiles: who can read or update secured columns | L | | Profiles, members and secured columns per user |

## 2.2.0 Over time and across environments

| # | Item | Size | Depends on | Done when |
|---|------|------|------------|-----------|
| 21 | Snapshot: save the security state (users, teams, roles, privileges) to a file | M | | A snapshot of a real org saves and reloads |
| 22 | Drift: compare the org with a snapshot and list what changed | M | 21 | Added and removed roles, members and privileges are listed |
| 23 | Compare two environments (Dev and Prod): roles, privileges, team roles | L | 21 | Works with a second XrmToolBox connection, or a snapshot of the other environment |
| 24 | Access review pack: one workbook per business unit or manager for periodic certification | M | 2 | A reviewer can sign off from the workbook alone |

## Later, as a separate tool

| # | Item | Size | Why separate |
|---|------|------|--------------|
| 25 | Sync roles and team roles from one environment to another | L | It writes across environments: a different risk, and its own review flow. Reuses 23 for the comparison. |

## Not planned

- Last sign-in and inactive users: the data is not reliably available through the Dataverse API.
- An organization-wide audit of record sharing: too heavy to read on large organizations.
